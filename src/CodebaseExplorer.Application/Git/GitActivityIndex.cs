using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Model;

namespace CodebaseExplorer.Application.Git;

/// <summary>
/// Aggregates per-file Git statistics to model elements at query time (GIT-005, GIT_INTEGRATION.md §5), shared by the
/// graph overlay and the Component Inspector so both show the same numbers. The files of an element are: a file itself;
/// for a project the files it owns; for a module the files that declare an entity of the module or of one of its
/// sub-modules; for an entity the files of its declarations and of its nested entities. External dependencies have none.
/// </summary>
/// <remarks>
/// Per-commit data is not stored, so the aggregate counts <b>file changes</b> (the sum of the files' commit counts: a
/// commit touching three files of a project counts three times), not distinct commits; distinct authors cannot be
/// aggregated and are reported per file only.
/// </remarks>
public sealed class GitActivityIndex
{
    private readonly ArchitectureModel _model;
    private readonly GitActivity _activity;
    private readonly ILookup<ElementId?, ElementId> _filesByProject;
    private readonly Dictionary<ElementId, HashSet<ElementId>> _directModuleFiles = [];
    private readonly ILookup<ElementId, ElementId> _childModules;
    private readonly ILookup<ElementId, ElementId> _nestedEntities;
    private readonly Dictionary<ElementId, HashSet<ElementId>> _moduleFiles = [];

    public GitActivityIndex(ArchitectureModel model, GitActivity activity)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(activity);
        _model = model;
        _activity = activity;
        _filesByProject = model.Files.ToLookup(f => f.ProjectId, f => f.Id);
        _childModules = model.Modules.Where(m => m.ParentModuleId is not null).ToLookup(m => m.ParentModuleId!.Value, m => m.Id);
        _nestedEntities = model.Entities.Where(e => e.ContainingEntityId is not null).ToLookup(e => e.ContainingEntityId!.Value, e => e.Id);
        foreach (var entity in model.Entities)
        {
            if (!_directModuleFiles.TryGetValue(entity.ModuleId, out var files))
            {
                files = [];
                _directModuleFiles[entity.ModuleId] = files;
            }

            files.UnionWith(entity.Declarations.Select(d => d.FileId));
        }
    }

    public GitActivitySnapshot Snapshot => _activity.Snapshot;

    /// <summary>Activity of the element, or <see langword="null"/> if it is no file, project, module or entity of the model.</summary>
    public ElementActivity? For(ElementId elementId)
    {
        var files = FilesOf(elementId);
        if (files is null)
        {
            return null;
        }

        var changes = files.Select(f => _activity.Files.GetValueOrDefault(f)).OfType<GitFileChanges>().ToList();
        return new ElementActivity(
            changes.Sum(c => c.CommitCount),
            changes.Count,
            changes.Sum(c => c.LinesAdded),
            changes.Sum(c => c.LinesDeleted),
            changes.Max(c => c.LastCommitAt),
            changes.Count == 1 ? changes[0].DistinctAuthorCount : null);
    }

    private IEnumerable<ElementId>? FilesOf(ElementId id)
    {
        if (_model.FindFile(id) is not null)
        {
            return [id];
        }

        if (_model.FindProject(id) is not null)
        {
            return _filesByProject[id];
        }

        if (_model.FindModule(id) is not null)
        {
            return ModuleFiles(id);
        }

        if (_model.FindEntity(id) is not null)
        {
            var files = new HashSet<ElementId>();
            AddEntityFiles(id, files);
            return files;
        }

        return null;
    }

    private HashSet<ElementId> ModuleFiles(ElementId moduleId)
    {
        if (_moduleFiles.TryGetValue(moduleId, out var cached))
        {
            return cached;
        }

        var files = new HashSet<ElementId>(_directModuleFiles.GetValueOrDefault(moduleId) ?? []);
        foreach (var child in _childModules[moduleId])
        {
            files.UnionWith(ModuleFiles(child));
        }

        _moduleFiles[moduleId] = files;
        return files;
    }

    private void AddEntityFiles(ElementId entityId, HashSet<ElementId> files)
    {
        files.UnionWith(_model.FindEntity(entityId)!.Declarations.Select(d => d.FileId));
        foreach (var nested in _nestedEntities[entityId])
        {
            AddEntityFiles(nested, files);
        }
    }
}

/// <summary>Aggregated Git activity of one element (<see cref="GitActivityIndex"/>).</summary>
/// <param name="FileChanges">Sum of the commit counts of the element's files.</param>
/// <param name="ChangedFiles">Files of the element with at least one commit in the window.</param>
/// <param name="LinesAdded">Sum of added lines.</param>
/// <param name="LinesDeleted">Sum of deleted lines.</param>
/// <param name="LastChangeAt">Newest change of any of its files.</param>
/// <param name="DistinctAuthors">Distinct authors — only for a single changed file (counts cannot be aggregated).</param>
public sealed record ElementActivity(
    int FileChanges,
    int ChangedFiles,
    long LinesAdded,
    long LinesDeleted,
    DateTimeOffset? LastChangeAt,
    int? DistinctAuthors);
