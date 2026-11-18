using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;

namespace CodebaseExplorer.Graph.Projection;

/// <summary>
/// The nodes visible for a query (GRAPH-006): projects and (optionally) external dependencies as roots;
/// children of expanded nodes; and for any model element the deepest visible node that contains it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>An expanded project shows its top-level namespaces; an expanded module its child namespaces and the
/// types declared directly in it. Types (and members) are not expandable in M3.</item>
/// <item>Namespace chains are compressed: a namespace without types and with exactly one child namespace is skipped
/// (<c>Advanced</c> → <c>Advanced.Core</c> shows <c>Advanced.Core</c>), so the user does not have to click through
/// empty levels.</item>
/// <item>Expanded ids that are unknown, invisible or not expandable are ignored; the result depends only on the
/// model and the query, so expanding and collapsing again yields an identical view.</item>
/// </list>
/// </remarks>
internal sealed class VisibleTree
{
    private readonly ArchitectureModel _model;
    private readonly Dictionary<string, string?> _parentOf = new(StringComparer.Ordinal);
    private readonly ILookup<ElementId?, CodeModule> _childModules;
    private readonly ILookup<ElementId, CodeModule> _topModules;
    private readonly ILookup<ElementId, CodeEntity> _typesByModule;
    private readonly Dictionary<ElementId, int> _filesByProject;
    private readonly Dictionary<ElementId, int> _entitiesByModule;
    private readonly HashSet<ElementId> _omittedRoots = [];
    private readonly int _budget;
    private readonly GraphFilter _filter;
    private int _rootCount;
    private int _childNodeCount;
    private string? _aggregateId;
    private readonly Dictionary<ElementId, ElementId> _groupOf = [];

    /// <summary>True if the repository has no projects and the view shows its folders (GRAPH-009).</summary>
    public bool IsFolderView { get; private set; }

    private VisibleTree(ArchitectureModel model, int budget, GraphFilter filter)
    {
        _model = model;
        _budget = budget;
        _filter = filter;
        _childModules = model.Modules.Where(m => m.ParentModuleId is not null).ToLookup(m => m.ParentModuleId);
        _topModules = model.Modules.Where(m => m.ParentModuleId is null).ToLookup(m => m.ProjectId);
        _typesByModule = model.Entities.Where(e => e.ContainingEntityId is null && filter.Shows(e)).ToLookup(e => e.ModuleId);
        _filesByProject = model.Files.Where(f => f.ProjectId is not null).GroupBy(f => f.ProjectId!.Value).ToDictionary(g => g.Key, g => g.Count());
        _entitiesByModule = model.Entities.GroupBy(e => e.ModuleId).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Root nodes (projects, external dependencies) left out by the node budget (GRAPH-008).</summary>
    public int OmittedRootCount => _omittedRoots.Count;

    /// <summary>Children not shown because expanding their parent would exceed the node budget (GRAPH-008).</summary>
    public int RefusedChildCount { get; private set; }

    /// <summary>Ids of expanded nodes whose expansion was refused by the node budget.</summary>
    public List<string> RefusedExpansions { get; } = [];

    /// <summary>Visible nodes by id.</summary>
    public Dictionary<string, GraphNodeDto> Nodes { get; } = new(StringComparer.Ordinal);

    /// <summary>Node ids, parents before children.</summary>
    public List<string> Order { get; } = [];

    /// <summary>The filter the tree was built with (GRAPH-007).</summary>
    public GraphFilter Filter => _filter;

    public static VisibleTree Build(ArchitectureModel model, GraphQuery query, int? budget = null)
    {
        var tree = new VisibleTree(model, budget ?? query.NodeBudget, new GraphFilter(model, query.Filters));
        var expanded = query.Expanded
            .Select(id => id is { Length: 32 } && Guid.TryParseExact(id, "N", out var guid) && guid != Guid.Empty ? ElementId.Parse(id) : (ElementId?)null)
            .OfType<ElementId>()
            .ToHashSet();

        if (model.Projects.Count == 0)
        {
            tree.AddFolders(expanded); // folder fallback: nothing was recognized as a project (WF-01)
            tree.AddAggregate();
            return tree;
        }

        var projects = model.Projects.Where(tree._filter.Shows).OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Id.ToString(), StringComparer.Ordinal).ToArray();
        var dependencies = query.IncludeExternalDependencies
            ? model.ExternalDependencies.Where(tree._filter.Shows).OrderBy(d => d.DisplayName, StringComparer.Ordinal).ThenBy(d => d.Id.ToString(), StringComparer.Ordinal).ToArray()
            : [];

        if (query.Level == GraphLevels.Repository)
        {
            // L0: projects grouped by workspace, solution or ecosystem; groups expand into their projects (GRAPH-009).
            var groups = projects
                .GroupBy(p => GroupOf(model, p))
                .Select(g => (Id: ElementId.FromNaturalKey(NaturalKey.FromCanonicalString($"graph-group:{model.RepositoryId}:{g.Key.Key}")), g.Key.Label, Projects: g.ToArray()))
                .OrderBy(g => g.Label, StringComparer.Ordinal)
                .ToArray();
            foreach (var group in groups)
            {
                foreach (var project in group.Projects)
                {
                    tree._groupOf[project.Id] = group.Id;
                }
            }

            tree.SelectRoots([.. groups.Select(g => g.Id), .. dependencies.Select(d => d.Id)]);
            foreach (var group in groups.Where(g => !tree._omittedRoots.Contains(g.Id)))
            {
                var id = group.Id.ToString();
                var expand = expanded.Contains(group.Id) && tree.Fits(id, group.Projects.Length);
                tree.Add(new GraphNodeDto
                {
                    Id = id,
                    Kind = GraphNodeKinds.Group,
                    Label = group.Label,
                    Sublabel = group.Projects.Length == 1 ? "1 project" : $"{group.Projects.Length} projects",
                    Ecosystem = group.Projects.Select(p => p.Ecosystem).Distinct().Count() == 1 ? group.Projects[0].Ecosystem.ToKeyToken() : null,
                    ChildCount = group.Projects.Length,
                    IsExpandable = true,
                    IsExpanded = expand,
                    Metrics = new GraphNodeMetricsDto(0, 0, group.Projects.Length),
                }, parent: null);

                if (expand)
                {
                    foreach (var project in group.Projects)
                    {
                        tree.AddProject(project, expanded.Contains(project.Id), expanded, id);
                    }
                }
            }
        }
        else
        {
            tree.SelectRoots([.. projects.Select(p => p.Id), .. dependencies.Select(d => d.Id)]);
            foreach (var project in projects.Where(p => !tree._omittedRoots.Contains(p.Id)))
            {
                tree.AddProject(project, expanded.Contains(project.Id), expanded);
            }
        }

        foreach (var dependency in dependencies.Where(d => !tree._omittedRoots.Contains(d.Id)))
        {
            {
                tree.Add(new GraphNodeDto
                {
                    Id = dependency.Id.ToString(),
                    Kind = GraphNodeKinds.ExternalDependency,
                    Label = dependency.DisplayName,
                    Ecosystem = dependency.Ecosystem.ToKeyToken(),
                    Metrics = new GraphNodeMetricsDto(0, 0, 0),
                }, parent: null);
            }
        }

        tree.AddAggregate();
        return tree;
    }

    /// <summary>The "N more" node for roots left out by the node budget (GRAPH-008).</summary>
    private void AddAggregate()
    {
        if (_omittedRoots.Count == 0)
        {
            return;
        }

        _aggregateId = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString($"graph-aggregate:{_model.RepositoryId}:roots")).ToString();
        Add(new GraphNodeDto
        {
            Id = _aggregateId,
            Kind = GraphNodeKinds.Aggregate,
            Label = $"{_omittedRoots.Count} more",
            Sublabel = "Hidden by the node budget; use filters or the neighborhood view",
            ChildCount = _omittedRoots.Count,
            Metrics = new GraphNodeMetricsDto(0, 0, _omittedRoots.Count),
        }, parent: null);
    }

    /// <summary>
    /// The group of a project at the repository level (GRAPH-009): its npm/yarn/pnpm workspace, else the first .NET
    /// solution that lists it (ordinal path), else its ecosystem.
    /// </summary>
    private static (string Key, string Label) GroupOf(ArchitectureModel model, CodeProject project)
    {
        if (project.Kind == ProjectKind.NodeWorkspaceRoot)
        {
            return ($"workspace:{project.Name}", $"{project.Name} (workspace)");
        }

        if (project.Properties.TryGetValue("workspace", out var workspace))
        {
            return ($"workspace:{workspace}", $"{workspace} (workspace)");
        }

        var solution = project.SolutionMemberships
            .Select(m => model.FindFile(m.SolutionFileId)?.Path)
            .OfType<RelativePath>()
            .OrderBy(p => p.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (!solution.IsRoot)
        {
            return ($"solution:{solution.Value}", solution.FileName);
        }

        return project.Ecosystem switch
        {
            Ecosystem.DotNet => ("ecosystem:dotnet", ".NET projects"),
            Ecosystem.Node => ("ecosystem:node", "Node packages"),
            Ecosystem.Python => ("ecosystem:python", "Python projects"),
            _ => ("ecosystem:other", "Other projects"),
        };
    }

    /// <summary>
    /// Folder fallback (GRAPH-009, WF-01): top-level folders (and the repository root's own files) as roots; an expanded
    /// folder shows its subfolders. Size = files below the folder.
    /// </summary>
    private void AddFolders(HashSet<ElementId> expanded)
    {
        IsFolderView = true;
        var filesPerFolder = new Dictionary<string, int>(StringComparer.Ordinal);
        var children = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var rootFiles = 0;
        foreach (var file in _model.Files)
        {
            var segments = file.Path.Value.Split('/');
            if (segments.Length == 1)
            {
                rootFiles++;
                continue;
            }

            for (var depth = 1; depth < segments.Length; depth++)
            {
                var folder = string.Join('/', segments.Take(depth));
                filesPerFolder[folder] = filesPerFolder.GetValueOrDefault(folder) + 1;
                var parent = depth == 1 ? string.Empty : string.Join('/', segments.Take(depth - 1));
                if (!children.TryGetValue(parent, out var set))
                {
                    children[parent] = set = new SortedSet<string>(StringComparer.Ordinal);
                }

                set.Add(folder);
            }
        }

        var roots = children.GetValueOrDefault(string.Empty)?.ToArray() ?? [];
        SelectRoots([.. roots.Select(FolderIdOf), .. rootFiles > 0 ? [FolderIdOf(".")] : Array.Empty<ElementId>()]);

        foreach (var folder in roots.Where(f => !_omittedRoots.Contains(FolderIdOf(f))))
        {
            AddFolder(folder, parentId: null);
        }

        if (rootFiles > 0 && !_omittedRoots.Contains(FolderIdOf(".")))
        {
            Add(new GraphNodeDto
            {
                Id = FolderId("."),
                Kind = GraphNodeKinds.Folder,
                Label = "(repository root)",
                Sublabel = rootFiles == 1 ? "1 file directly in the root" : $"{rootFiles} files directly in the root",
                Metrics = new GraphNodeMetricsDto(0, 0, rootFiles),
            }, parent: null);
        }

        void AddFolder(string folder, string? parentId)
        {
            var subfolders = children.GetValueOrDefault(folder)?.ToArray() ?? [];
            var id = FolderId(folder);
            var expand = subfolders.Length > 0 && expanded.Contains(FolderIdOf(folder)) && Fits(id, subfolders.Length);
            Add(new GraphNodeDto
            {
                Id = id,
                ParentId = parentId,
                Kind = GraphNodeKinds.Folder,
                Label = folder[(folder.LastIndexOf('/') + 1)..],
                Sublabel = folder,
                ChildCount = subfolders.Length,
                IsExpandable = subfolders.Length > 0,
                IsExpanded = expand,
                Metrics = new GraphNodeMetricsDto(0, 0, filesPerFolder.GetValueOrDefault(folder)),
            }, parentId);

            if (expand)
            {
                foreach (var subfolder in subfolders)
                {
                    AddFolder(subfolder, id);
                }
            }
        }

    }

    private ElementId FolderIdOf(string folder) => ElementId.FromNaturalKey(NaturalKey.FromCanonicalString($"graph-folder:{_model.RepositoryId}:{folder}"));

    private string FolderId(string folder) => FolderIdOf(folder).ToString();

    /// <summary>
    /// Node budget for the root level (GRAPH_ENGINE.md §4): if there are more roots than the budget, the
    /// <c>budget - 1</c> roots with the highest degree (fan-in + fan-out at the root level) stay and the others are
    /// collapsed into one "N more" aggregate node that also takes their edges.
    /// </summary>
    private void SelectRoots(IReadOnlyList<ElementId> roots)
    {
        if (roots.Count <= _budget)
        {
            _rootCount = roots.Count;
            return;
        }

        var degree = roots.ToDictionary(r => r, _ => 0);
        foreach (var relationship in _model.Relationships)
        {
            if (RootOf(relationship.Source) is { } source && RootOf(relationship.Target) is { } target
                && source != target && degree.ContainsKey(source) && degree.ContainsKey(target))
            {
                degree[source]++;
                degree[target]++;
            }
        }

        var order = roots.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        var kept = roots.OrderByDescending(r => degree[r]).ThenBy(r => order[r]).Take(Math.Max(0, _budget - 1)).ToHashSet();
        _omittedRoots.UnionWith(roots.Where(r => !kept.Contains(r)));
        _rootCount = kept.Count + 1;
    }

    /// <summary>The project (or external dependency) an element belongs to at the root level.</summary>
    private ElementId? RootOf(ElementReference reference)
    {
        var root = reference.Kind switch
        {
            ElementKind.Project or ElementKind.ExternalDependency => reference.Id,
            ElementKind.File => _model.FindFile(reference.Id)?.ProjectId,
            ElementKind.Module => _model.FindModule(reference.Id)?.ProjectId,
            ElementKind.Entity => _model.FindEntity(reference.Id)?.ProjectId,
            _ => null,
        };
        return root is { } id && _groupOf.TryGetValue(id, out var group) ? group : root;
    }

    /// <summary>True if <paramref name="children"/> more nodes still fit into the node budget; records a refusal otherwise.</summary>
    private bool Fits(string parentId, int children)
    {
        if (_rootCount + _childNodeCount + children <= _budget)
        {
            return true;
        }

        RefusedChildCount += children;
        RefusedExpansions.Add(parentId);
        return false;
    }

    /// <summary>The id of the deepest visible node containing the element, or <see langword="null"/> if it is not shown.</summary>
    public string? VisibleNodeOf(ElementReference reference)
    {
        switch (reference.Kind)
        {
            case ElementKind.Project:
                return Visible(reference.Id);
            case ElementKind.ExternalDependency:
                return Visible(reference.Id);
            case ElementKind.File:
                return _model.FindFile(reference.Id)?.ProjectId is { } owner ? Visible(owner) : null;
            case ElementKind.Module:
                return _model.FindModule(reference.Id) is { } module ? VisibleModuleOrProject(module) : null;
            case ElementKind.Entity:
                var entity = _model.FindEntity(reference.Id);
                if (entity is not null && !_filter.Shows(entity))
                {
                    return null; // hidden by a filter (e.g. test code): its relationships are not shown either
                }

                for (var current = entity; current is not null; current = current.ContainingEntityId is { } container ? _model.FindEntity(container) : null)
                {
                    if (Visible(current.Id) is { } visible)
                    {
                        return visible;
                    }
                }

                return entity is not null && _model.FindModule(entity.ModuleId) is { } entityModule ? VisibleModuleOrProject(entityModule) : null;
            default:
                return null;
        }
    }

    /// <summary>True if <paramref name="ancestor"/> is a (transitive) compound parent of <paramref name="node"/>.</summary>
    public bool IsAncestor(string ancestor, string node)
    {
        for (var current = _parentOf.GetValueOrDefault(node); current is not null; current = _parentOf.GetValueOrDefault(current))
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private string? Visible(ElementId id) =>
        Nodes.ContainsKey(id.ToString()) ? id.ToString()
        : _omittedRoots.Contains(id) ? _aggregateId
        : _groupOf.TryGetValue(id, out var group) ? Visible(group) // a project inside a collapsed group
        : null;

    private string? VisibleModuleOrProject(CodeModule module)
    {
        for (var current = module; current is not null; current = current.ParentModuleId is { } parent ? _model.FindModule(parent) : null)
        {
            if (Visible(current.Id) is { } visible)
            {
                return visible;
            }
        }

        return Visible(module.ProjectId);
    }

    private void AddProject(CodeProject project, bool isExpanded, HashSet<ElementId> expanded, string? parentId = null)
    {
        var children = _topModules[project.Id].Select(Compress).OrderBy(m => m.QualifiedName, StringComparer.Ordinal).ToArray();
        var expand = isExpanded && children.Length > 0 && Fits(project.Id.ToString(), children.Length);
        var id = Add(new GraphNodeDto
        {
            Id = project.Id.ToString(),
            ParentId = parentId,
            Kind = GraphNodeKinds.Project,
            Label = project.Name,
            Sublabel = project.RootDirectory.ToString(),
            Language = project.PrimaryLanguage.Value,
            Ecosystem = project.Ecosystem.ToKeyToken(),
            ChildCount = children.Length,
            IsExpandable = children.Length > 0,
            IsExpanded = expand,
            Metrics = new GraphNodeMetricsDto(0, 0, _filesByProject.GetValueOrDefault(project.Id)),
            Badges = project.IsPlaceholder() ? [GraphBadges.Missing] : [],
        }, parentId);

        if (expand)
        {
            foreach (var module in children)
            {
                AddModule(project, module, parentId: id, parentQualifiedName: null, expanded);
            }
        }
    }

    private void AddModule(CodeProject project, CodeModule module, string parentId, string? parentQualifiedName, HashSet<ElementId> expanded)
    {
        var childModules = _childModules[module.Id].Select(Compress).OrderBy(m => m.QualifiedName, StringComparer.Ordinal).ToArray();
        var types = _typesByModule[module.Id].OrderBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.Id.ToString(), StringComparer.Ordinal).ToArray();
        var childCount = childModules.Length + types.Length;
        var expand = expanded.Contains(module.Id) && childCount > 0 && Fits(module.Id.ToString(), childCount);

        // Relative to the parent: namespaces and Python modules are dotted, TS/JS folders and files are paths.
        var label = parentQualifiedName is not null
            && (module.QualifiedName.StartsWith(parentQualifiedName + ".", StringComparison.Ordinal) || module.QualifiedName.StartsWith(parentQualifiedName + "/", StringComparison.Ordinal))
            ? module.QualifiedName[(parentQualifiedName.Length + 1)..]
            : module.QualifiedName;
        var id = Add(new GraphNodeDto
        {
            Id = module.Id.ToString(),
            ParentId = parentId,
            Kind = GraphNodeKinds.Module,
            Label = label,
            Sublabel = module.QualifiedName,
            Language = project.PrimaryLanguage.Value,
            Ecosystem = project.Ecosystem.ToKeyToken(),
            ChildCount = childCount,
            IsExpandable = childCount > 0,
            IsExpanded = expand,
            Metrics = new GraphNodeMetricsDto(0, 0, CountEntities(module)),
        }, parentId);

        if (!expand)
        {
            return;
        }

        foreach (var child in childModules)
        {
            AddModule(project, child, id, module.QualifiedName, expanded);
        }

        foreach (var type in types)
        {
            Add(new GraphNodeDto
            {
                Id = type.Id.ToString(),
                ParentId = id,
                Kind = GraphNodeKinds.Entity,
                Label = type.Name,
                Sublabel = type.Kind.ToString().ToLowerInvariant(),
                Language = type.Language.Value,
                Ecosystem = project.Ecosystem.ToKeyToken(),
                Metrics = new GraphNodeMetricsDto(0, 0, type.Metrics?.LineCount ?? 0),
            }, id);
        }
    }

    /// <summary>Skips namespaces without types that only contain one child namespace.</summary>
    private CodeModule Compress(CodeModule module)
    {
        var current = module;
        while (!_typesByModule[current.Id].Any() && _childModules[current.Id].ToArray() is [var only])
        {
            current = only;
        }

        return current;
    }

    private int CountEntities(CodeModule module)
    {
        var count = _entitiesByModule.GetValueOrDefault(module.Id);
        return count + _childModules[module.Id].Sum(CountEntities);
    }

    private string Add(GraphNodeDto node, string? parent)
    {
        Nodes.Add(node.Id, node);
        Order.Add(node.Id);
        if (parent is not null)
        {
            _childNodeCount++;
        }

        _parentOf[node.Id] = parent;
        return node.Id;
    }
}
