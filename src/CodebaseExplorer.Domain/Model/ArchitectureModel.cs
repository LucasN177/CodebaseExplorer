using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;

namespace CodebaseExplorer.Domain.Model;

/// <summary>
/// Read-only snapshot of the architecture model of one repository for one analysis run
/// (DOMAIN_MODEL.md §6). Elements are ordered by natural key (ordinal) for deterministic output.
/// </summary>
/// <remarks>
/// Construction never fails because of inconsistent data; <see cref="Validate"/> reports every
/// cross-element violation and <see cref="EnsureValid"/> throws with all of them. Lookups return the
/// first occurrence when an id occurs more than once.
/// </remarks>
public sealed class ArchitectureModel
{
    private readonly Dictionary<ElementId, RepositoryFile> _files = [];
    private readonly Dictionary<ElementId, CodeProject> _projects = [];
    private readonly Dictionary<ElementId, CodeModule> _modules = [];
    private readonly Dictionary<ElementId, CodeEntity> _entities = [];
    private readonly Dictionary<ElementId, ExternalDependency> _externalDependencies = [];
    private readonly Dictionary<ElementId, CodeRelationship> _relationships = [];
    private readonly List<ModelViolation> _identityViolations = [];
    private IReadOnlyList<ModelViolation>? _violations;

    private ArchitectureModel(
        RepositoryId repositoryId,
        AnalysisRunId analysisRunId,
        IReadOnlyList<RepositoryFile> files,
        IReadOnlyList<CodeProject> projects,
        IReadOnlyList<CodeModule> modules,
        IReadOnlyList<CodeEntity> entities,
        IReadOnlyList<ExternalDependency> externalDependencies,
        IReadOnlyList<CodeRelationship> relationships)
    {
        RepositoryId = repositoryId;
        AnalysisRunId = analysisRunId;
        Files = files;
        Projects = projects;
        Modules = modules;
        Entities = entities;
        ExternalDependencies = externalDependencies;
        Relationships = relationships;

        var keysById = new Dictionary<ElementId, NaturalKey>();
        Index(files, _files, f => f.Id, f => f.NaturalKey, keysById);
        Index(projects, _projects, p => p.Id, p => p.NaturalKey, keysById);
        Index(modules, _modules, m => m.Id, m => m.NaturalKey, keysById);
        Index(entities, _entities, e => e.Id, e => e.NaturalKey, keysById);
        Index(externalDependencies, _externalDependencies, d => d.Id, d => d.NaturalKey, keysById);
        Index(relationships, _relationships, r => r.Id, r => r.NaturalKey, keysById);
        // Evidence ids embed their relationship id; only distinct relationships are checked, so a
        // duplicated relationship is reported once and not once per evidence record.
        Index(_relationships.Values.SelectMany(r => r.Evidence).ToArray(), new Dictionary<ElementId, Evidence.AnalysisEvidence>(),
            e => e.Id, e => e.NaturalKey, keysById);
    }

    public RepositoryId RepositoryId { get; }

    public AnalysisRunId AnalysisRunId { get; }

    public IReadOnlyList<RepositoryFile> Files { get; }

    public IReadOnlyList<CodeProject> Projects { get; }

    public IReadOnlyList<CodeModule> Modules { get; }

    public IReadOnlyList<CodeEntity> Entities { get; }

    public IReadOnlyList<ExternalDependency> ExternalDependencies { get; }

    public IReadOnlyList<CodeRelationship> Relationships { get; }

    public bool IsValid => Validate().Count == 0;

    /// <summary>Creates a snapshot; inconsistencies are reported by <see cref="Validate"/>, not here.</summary>
    public static ArchitectureModel Create(
        RepositoryId repositoryId,
        AnalysisRunId analysisRunId,
        IEnumerable<RepositoryFile> files,
        IEnumerable<CodeProject> projects,
        IEnumerable<CodeModule> modules,
        IEnumerable<CodeEntity> entities,
        IEnumerable<ExternalDependency> externalDependencies,
        IEnumerable<CodeRelationship> relationships)
    {
        if (repositoryId.Value == Guid.Empty)
        {
            throw new ArgumentException("A model requires a repository id.", nameof(repositoryId));
        }

        if (analysisRunId.Value == Guid.Empty)
        {
            throw new ArgumentException("A model requires an analysis run id.", nameof(analysisRunId));
        }

        return new ArchitectureModel(
            repositoryId,
            analysisRunId,
            Sorted(files, f => f.NaturalKey, nameof(files)),
            Sorted(projects, p => p.NaturalKey, nameof(projects)),
            Sorted(modules, m => m.NaturalKey, nameof(modules)),
            Sorted(entities, e => e.NaturalKey, nameof(entities)),
            Sorted(externalDependencies, d => d.NaturalKey, nameof(externalDependencies)),
            Sorted(relationships, r => r.NaturalKey, nameof(relationships)));
    }

    public RepositoryFile? FindFile(ElementId id) => _files.GetValueOrDefault(id);

    public CodeProject? FindProject(ElementId id) => _projects.GetValueOrDefault(id);

    public CodeModule? FindModule(ElementId id) => _modules.GetValueOrDefault(id);

    public CodeEntity? FindEntity(ElementId id) => _entities.GetValueOrDefault(id);

    public ExternalDependency? FindExternalDependency(ElementId id) => _externalDependencies.GetValueOrDefault(id);

    public CodeRelationship? FindRelationship(ElementId id) => _relationships.GetValueOrDefault(id);

    /// <summary>True if an element of the referenced kind with the referenced id exists.</summary>
    public bool Contains(ElementReference reference) => reference.Kind switch
    {
        ElementKind.File => _files.ContainsKey(reference.Id),
        ElementKind.Project => _projects.ContainsKey(reference.Id),
        ElementKind.Module => _modules.ContainsKey(reference.Id),
        ElementKind.Entity => _entities.ContainsKey(reference.Id),
        ElementKind.ExternalDependency => _externalDependencies.ContainsKey(reference.Id),
        _ => false,
    };

    /// <summary>All violated cross-element invariants (empty if the model is consistent).</summary>
    public IReadOnlyList<ModelViolation> Validate() => _violations ??= ComputeViolations();

    /// <exception cref="ModelValidationException">The model has at least one violation.</exception>
    public void EnsureValid()
    {
        var violations = Validate();
        if (violations.Count > 0)
        {
            throw new ModelValidationException(violations);
        }
    }

    private List<ModelViolation> ComputeViolations()
    {
        var violations = new List<ModelViolation>(_identityViolations);

        ValidateRepositoryOwnership(violations);
        ValidateFiles(violations);
        ValidateProjects(violations);
        ValidateModules(violations);
        ValidateEntities(violations);
        ValidateRelationships(violations);

        return violations;
    }

    private void ValidateRepositoryOwnership(List<ModelViolation> violations)
    {
        var owners = Files.Select(f => (f.Id, f.RepositoryId))
            .Concat(Projects.Select(p => (p.Id, p.RepositoryId)))
            .Concat(Modules.Select(m => (m.Id, m.RepositoryId)))
            .Concat(Entities.Select(e => (e.Id, e.RepositoryId)))
            .Concat(ExternalDependencies.Select(d => (d.Id, d.RepositoryId)));

        foreach (var (id, repositoryId) in owners)
        {
            if (repositoryId != RepositoryId)
            {
                violations.Add(new(ModelViolationCode.WrongRepository, id,
                    $"Element belongs to repository {repositoryId}, the model to {RepositoryId}."));
            }
        }
    }

    private void ValidateFiles(List<ModelViolation> violations)
    {
        foreach (var file in Files)
        {
            if (file.ProjectId is not { } projectId)
            {
                continue;
            }

            if (FindProject(projectId) is not { } project)
            {
                violations.Add(new(ModelViolationCode.MissingProject, file.Id, $"File '{file.Path}' references missing project {projectId}."));
            }
            else if (!file.Path.IsWithin(project.RootDirectory))
            {
                violations.Add(new(ModelViolationCode.FileOutsideProject, file.Id,
                    $"File '{file.Path}' lies outside the root '{project.RootDirectory}' of project '{project.Name}'."));
            }
        }
    }

    private void ValidateProjects(List<ModelViolation> violations)
    {
        foreach (var project in Projects)
        {
            if (project.ProjectFileId is { } manifestId && !_files.ContainsKey(manifestId))
            {
                violations.Add(new(ModelViolationCode.MissingFile, project.Id,
                    $"Project '{project.Name}' references missing manifest '{project.ProjectFilePath}'."));
            }

            foreach (var membership in project.SolutionMemberships)
            {
                if (!_files.ContainsKey(membership.SolutionFileId))
                {
                    violations.Add(new(ModelViolationCode.MissingFile, project.Id,
                        $"Project '{project.Name}' references missing solution file {membership.SolutionFileId}."));
                }
            }
        }
    }

    private void ValidateModules(List<ModelViolation> violations)
    {
        foreach (var module in Modules)
        {
            if (!_projects.ContainsKey(module.ProjectId))
            {
                violations.Add(new(ModelViolationCode.MissingProject, module.Id,
                    $"Module '{module.QualifiedName}' references missing project {module.ProjectId}."));
            }

            if (module.ParentModuleId is not { } parentId)
            {
                continue;
            }

            if (!_modules.ContainsKey(parentId))
            {
                violations.Add(new(ModelViolationCode.MissingModule, module.Id,
                    $"Module '{module.QualifiedName}' references missing parent module {parentId}."));
            }

            if (IsInCycle(module.Id, id => FindModule(id)?.ParentModuleId))
            {
                violations.Add(new(ModelViolationCode.ContainmentCycle, module.Id,
                    $"Module '{module.QualifiedName}' is part of a parent-module cycle."));
            }
        }
    }

    private void ValidateEntities(List<ModelViolation> violations)
    {
        foreach (var entity in Entities)
        {
            if (!_projects.ContainsKey(entity.ProjectId))
            {
                violations.Add(new(ModelViolationCode.MissingProject, entity.Id,
                    $"Entity '{entity.QualifiedName}' references missing project {entity.ProjectId}."));
            }

            if (!_modules.ContainsKey(entity.ModuleId))
            {
                violations.Add(new(ModelViolationCode.MissingModule, entity.Id,
                    $"Entity '{entity.QualifiedName}' references missing module {entity.ModuleId}."));
            }

            foreach (var declaration in entity.Declarations)
            {
                if (!_files.ContainsKey(declaration.FileId))
                {
                    violations.Add(new(ModelViolationCode.MissingFile, entity.Id,
                        $"Entity '{entity.QualifiedName}' is declared in missing file {declaration.FileId}."));
                }
            }

            if (entity.ContainingEntityId is not { } containerId)
            {
                continue;
            }

            if (!_entities.ContainsKey(containerId))
            {
                violations.Add(new(ModelViolationCode.MissingContainingEntity, entity.Id,
                    $"Entity '{entity.QualifiedName}' references missing containing entity {containerId}."));
            }

            if (IsInCycle(entity.Id, id => FindEntity(id)?.ContainingEntityId))
            {
                violations.Add(new(ModelViolationCode.ContainmentCycle, entity.Id,
                    $"Entity '{entity.QualifiedName}' is part of a containment cycle."));
            }
        }
    }

    private void ValidateRelationships(List<ModelViolation> violations)
    {
        foreach (var relationship in Relationships)
        {
            foreach (var endpoint in new[] { relationship.Source, relationship.Target }.Distinct())
            {
                if (!Contains(endpoint))
                {
                    violations.Add(new(ModelViolationCode.MissingRelationshipEndpoint, relationship.Id,
                        $"{relationship.Kind} relationship references missing {endpoint.Kind} {endpoint.Id}."));
                }
            }

            foreach (var evidence in relationship.Evidence)
            {
                if (evidence.Location is { } location && !_files.ContainsKey(location.FileId))
                {
                    violations.Add(new(ModelViolationCode.MissingFile, evidence.Id,
                        $"Evidence '{evidence.Rule}' of a {relationship.Kind} relationship points to missing file {location.FileId}."));
                }
            }
        }
    }

    private void Index<T>(
        IReadOnlyList<T> elements,
        Dictionary<ElementId, T> lookup,
        Func<T, ElementId> idOf,
        Func<T, NaturalKey> keyOf,
        Dictionary<ElementId, NaturalKey> keysById)
    {
        foreach (var element in elements)
        {
            var id = idOf(element);
            var key = keyOf(element);

            if (keysById.TryGetValue(id, out var existingKey))
            {
                _identityViolations.Add(existingKey == key
                    ? new(ModelViolationCode.DuplicateElement, id, $"Element '{key}' occurs more than once.")
                    : new(ModelViolationCode.NaturalKeyCollision, id, $"Natural keys '{existingKey}' and '{key}' share id {id}."));
                continue;
            }

            keysById[id] = key;
            lookup[id] = element;
        }
    }

    private static bool IsInCycle(ElementId start, Func<ElementId, ElementId?> parentOf)
    {
        var visited = new HashSet<ElementId> { start };
        var current = parentOf(start);

        while (current is { } id)
        {
            if (id == start)
            {
                return true;
            }

            if (!visited.Add(id))
            {
                return false; // a cycle further up that does not include start
            }

            current = parentOf(id);
        }

        return false;
    }

    private static T[] Sorted<T>(IEnumerable<T> elements, Func<T, NaturalKey> keyOf, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(elements, parameterName);

        var array = elements.ToArray();
        if (array.Any(e => e is null))
        {
            throw new ArgumentException("Model collections must not contain null.", parameterName);
        }

        return [.. array.OrderBy(e => keyOf(e).Value, StringComparer.Ordinal)];
    }
}
