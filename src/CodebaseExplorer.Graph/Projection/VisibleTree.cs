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

    private VisibleTree(ArchitectureModel model)
    {
        _model = model;
        _childModules = model.Modules.Where(m => m.ParentModuleId is not null).ToLookup(m => m.ParentModuleId);
        _topModules = model.Modules.Where(m => m.ParentModuleId is null).ToLookup(m => m.ProjectId);
        _typesByModule = model.Entities.Where(e => e.ContainingEntityId is null).ToLookup(e => e.ModuleId);
    }

    /// <summary>Visible nodes by id.</summary>
    public Dictionary<string, GraphNodeDto> Nodes { get; } = new(StringComparer.Ordinal);

    /// <summary>Node ids, parents before children.</summary>
    public List<string> Order { get; } = [];

    public static VisibleTree Build(ArchitectureModel model, GraphQuery query)
    {
        var tree = new VisibleTree(model);
        var expanded = query.Expanded
            .Select(id => id is { Length: 32 } && Guid.TryParseExact(id, "N", out var guid) && guid != Guid.Empty ? ElementId.Parse(id) : (ElementId?)null)
            .OfType<ElementId>()
            .ToHashSet();

        foreach (var project in model.Projects.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Id.ToString(), StringComparer.Ordinal))
        {
            tree.AddProject(project, expanded.Contains(project.Id), expanded);
        }

        if (query.IncludeExternalDependencies)
        {
            foreach (var dependency in model.ExternalDependencies.OrderBy(d => d.DisplayName, StringComparer.Ordinal).ThenBy(d => d.Id.ToString(), StringComparer.Ordinal))
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

        return tree;
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

    private string? Visible(ElementId id) => Nodes.ContainsKey(id.ToString()) ? id.ToString() : null;

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

    private void AddProject(CodeProject project, bool isExpanded, HashSet<ElementId> expanded)
    {
        var children = _topModules[project.Id].Select(Compress).OrderBy(m => m.QualifiedName, StringComparer.Ordinal).ToArray();
        var expand = isExpanded && children.Length > 0;
        var id = Add(new GraphNodeDto
        {
            Id = project.Id.ToString(),
            Kind = GraphNodeKinds.Project,
            Label = project.Name,
            Sublabel = project.RootDirectory.ToString(),
            Language = project.PrimaryLanguage.Value,
            Ecosystem = project.Ecosystem.ToKeyToken(),
            ChildCount = children.Length,
            IsExpandable = children.Length > 0,
            IsExpanded = expand,
            Metrics = new GraphNodeMetricsDto(0, 0, _model.Files.Count(f => f.ProjectId == project.Id)),
            Badges = project.IsPlaceholder() ? [GraphBadges.Missing] : [],
        }, parent: null);

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
        var expand = expanded.Contains(module.Id) && childCount > 0;

        var label = parentQualifiedName is not null && module.QualifiedName.StartsWith(parentQualifiedName + ".", StringComparison.Ordinal)
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
        var count = _model.Entities.Count(e => e.ModuleId == module.Id);
        return count + _childModules[module.Id].Sum(CountEntities);
    }

    private string Add(GraphNodeDto node, string? parent)
    {
        Nodes.Add(node.Id, node);
        Order.Add(node.Id);
        _parentOf[node.Id] = parent;
        return node.Id;
    }
}
