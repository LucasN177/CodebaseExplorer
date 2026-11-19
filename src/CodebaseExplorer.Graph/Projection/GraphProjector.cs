using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;

namespace CodebaseExplorer.Graph.Projection;

/// <summary>
/// Pure projection of an <see cref="ArchitectureModel"/> into a <see cref="GraphViewDto"/>
/// (GRAPH_ENGINE.md §2). The root level is the project level (L1): one node per project (and optionally
/// per external dependency). Expanded projects show their namespaces (L2), expanded modules their
/// sub-namespaces and types (L3) as child nodes (mixed levels, GRAPH-006). Every relationship is
/// attributed to the deepest visible node containing each endpoint and aggregated into one edge per
/// ordered node pair; relationships inside one node or between a node and its own ancestor are dropped. The class level
/// (<see cref="GraphLevels.Entity"/>, GRAPH-011) shows every top-level type as a root; types expand into their members.
/// </summary>
internal sealed class GraphProjector : IGraphProjector
{
    public Result<GraphViewDto> Project(ArchitectureModel model, GraphQuery query, GitActivityIndex? activity = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(query);

        return Validate(model, query) is { } error ? error : ProjectView(model, query, activity);
    }

    public Result<GraphEdgeDetailsDto> DescribeEdge(ArchitectureModel model, GraphQuery query, string edgeId)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(edgeId);

        if (Validate(model, query) is { } error)
        {
            return error;
        }

        // Same visibility and attribution as the view, so the details always match the edge the user selected.
        var tree = VisibleTree.Build(model, query, query.Focus is null ? null : GraphQuery.MaxNodeBudget);
        var group = GroupRelationships(model, tree)
            .FirstOrDefault(g => string.Equals(EdgeId(g.Key.Source, g.Key.Target), edgeId, StringComparison.OrdinalIgnoreCase));
        if (group.Value is null)
        {
            return ApplicationError.NotFound(GraphErrorCodes.EdgeNotFound, "The edge is not part of the graph.");
        }

        return new GraphEdgeDetailsDto
        {
            EdgeId = EdgeId(group.Key.Source, group.Key.Target),
            SourceLabel = tree.Nodes[group.Key.Source].Label,
            TargetLabel = tree.Nodes[group.Key.Target].Label,
            Relationships = [.. group.Value
                .Select(relationship => new GraphRelationshipDto
                {
                    Kind = relationship.Kind.ToKeyToken(),
                    Level = Token(relationship.Level),
                    SourceLabel = ElementLabel(model, relationship.Source),
                    TargetLabel = ElementLabel(model, relationship.Target),
                    Evidence = [.. relationship.Evidence
                        .Select(evidence => new GraphEvidenceDto
                        {
                            Analyzer = evidence.Analyzer.Value,
                            Rule = evidence.Rule,
                            Level = Token(evidence.Level),
                            Path = evidence.Location is { } location ? model.FindFile(location.FileId)?.Path.Value : null,
                            StartLine = evidence.Location?.StartLine,
                            EndLine = evidence.Location?.EndLine,
                        })
                        .OrderBy(e => e.Path, StringComparer.Ordinal)
                        .ThenBy(e => e.StartLine)
                        .ThenBy(e => e.Rule, StringComparer.Ordinal)],
                })
                .OrderBy(r => r.Kind, StringComparer.Ordinal)
                .ThenBy(r => r.SourceLabel, StringComparer.Ordinal)
                .ThenBy(r => r.TargetLabel, StringComparer.Ordinal)],
        };
    }

    private static ApplicationError? Validate(ArchitectureModel model, GraphQuery query)
    {
        if (!string.Equals(query.RepositoryId, model.RepositoryId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationError.Validation(GraphErrorCodes.RepositoryMismatch, "The query is for another repository than the model.");
        }

        if (query.Level is not (GraphLevels.Project or GraphLevels.Repository or GraphLevels.Entity))
        {
            return ApplicationError.Validation(GraphErrorCodes.LevelNotSupported,
                $"The graph level '{query.Level}' is not supported; supported: '{GraphLevels.Project}', '{GraphLevels.Repository}', '{GraphLevels.Entity}'.");
        }

        if (query.NodeBudget < 2 || query.NodeBudget > GraphQuery.MaxNodeBudget)
        {
            return ApplicationError.Validation(GraphErrorCodes.NodeBudgetInvalid,
                $"The node budget must be between 2 and {GraphQuery.MaxNodeBudget}.");
        }

        if (query.Depth is < 1 or > 3 || query.Direction is not (GraphDirections.Incoming or GraphDirections.Outgoing or GraphDirections.Both))
        {
            return ApplicationError.Validation(GraphErrorCodes.NeighborhoodInvalid,
                "The neighborhood needs a depth of 1 to 3 and the direction incoming, outgoing or both.");
        }

        return null;
    }

    private static Result<GraphViewDto> ProjectView(ArchitectureModel model, GraphQuery query, GitActivityIndex? activity)
    {
        // In neighborhood mode the whole graph (up to the maximum budget) is built first, then reduced to the neighborhood,
        // and only then is the requested budget applied — so a focus can reach elements an overview would aggregate.
        var tree = VisibleTree.Build(model, query, query.Focus is null ? null : GraphQuery.MaxNodeBudget);
        var edges = AggregateEdges(GroupRelationships(model, tree));
        IReadOnlyList<string> order = tree.Order;
        var truncation = TruncationOf(tree);
        List<BreadcrumbDto> breadcrumbs = [new(model.RepositoryId.ToString(), "Repository", GraphLevels.Repository)];

        if (query.Focus is { } requested)
        {
            if (FocusNode(model, tree, requested) is not { } focus)
            {
                return ApplicationError.NotFound(GraphErrorCodes.FocusNotFound, "The focused element is not part of the graph (it may be hidden by a filter).");
            }

            (order, edges, var omitted) = Neighborhood(tree, edges, focus, query);
            truncation = omitted > 0
                ? new TruncationDto(omitted, TruncationReasons.NodeBudget,
                [
                    $"{omitted} elements of the neighborhood are not shown (limit {query.NodeBudget} nodes).",
                    "Reduce the depth or use filters.",
                ])
                : null;
            breadcrumbs.AddRange(PathTo(tree, focus));
        }

        var fanOut = edges.GroupBy(e => e.Source).ToDictionary(g => g.Key, g => g.Select(e => e.Target).Distinct().Count());
        var fanIn = edges.GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.Select(e => e.Source).Distinct().Count());

        return new GraphViewDto
        {
            RepositoryId = model.RepositoryId.ToString(),
            Level = tree.IsFolderView ? GraphLevels.Folder : query.Level,
            // Parents before children (the web view adds nodes in order); roots sorted as before GRAPH-006.
            Nodes = [.. order.Select(id => tree.Nodes[id] with
            {
                Metrics = tree.Nodes[id].Metrics with
                {
                    FanIn = fanIn.GetValueOrDefault(id),
                    FanOut = fanOut.GetValueOrDefault(id),
                    ChangeCount = ChangeCountOf(activity, id),
                },
            })],
            Edges = edges,
            Truncation = truncation,
            Breadcrumbs = breadcrumbs,
        };
    }

    /// <summary>
    /// File changes of a node (GIT-005); <see langword="null"/> without activity and for nodes that are no model element
    /// (aggregate, group and folder nodes have non-element ids).
    /// </summary>
    private static int? ChangeCountOf(GitActivityIndex? activity, string nodeId) =>
        activity is not null && nodeId is { Length: 32 } && Guid.TryParseExact(nodeId, "N", out var guid) && guid != Guid.Empty
            ? activity.For(ElementId.Parse(nodeId))?.FileChanges
            : null;

    /// <summary>The visible node for a focus id: a node of the view, or the deepest visible node of a model element.</summary>
    private static string? FocusNode(ArchitectureModel model, VisibleTree tree, string focus)
    {
        if (tree.Nodes.ContainsKey(focus))
        {
            return focus;
        }

        if (focus is not { Length: 32 } || !Guid.TryParseExact(focus, "N", out var guid) || guid == Guid.Empty)
        {
            return null;
        }

        var id = ElementId.Parse(focus);
        ElementKind? kind = model.FindProject(id) is not null ? ElementKind.Project
            : model.FindModule(id) is not null ? ElementKind.Module
            : model.FindEntity(id) is not null ? ElementKind.Entity
            : model.FindExternalDependency(id) is not null ? ElementKind.ExternalDependency
            : model.FindFile(id) is not null ? ElementKind.File
            : null;
        return kind is { } elementKind ? tree.VisibleNodeOf(new ElementReference(elementKind, id)) : null;
    }

    /// <summary>
    /// The focus (with its content), every node within <c>Depth</c> hops along <c>Direction</c>, and their compound
    /// parents; nearest first until the node budget is reached (GRAPH_ENGINE.md §3, WF-05).
    /// </summary>
    private static (IReadOnlyList<string> Order, List<GraphEdgeDto> Edges, int Omitted) Neighborhood(
        VisibleTree tree, List<GraphEdgeDto> edges, string focus, GraphQuery query)
    {
        var distance = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in tree.Order.Where(id => id == focus || tree.IsAncestor(focus, id)))
        {
            distance[id] = 0;
        }

        var outgoing = query.Direction != GraphDirections.Incoming ? edges.ToLookup(e => e.Source, e => e.Target) : null;
        var incoming = query.Direction != GraphDirections.Outgoing ? edges.ToLookup(e => e.Target, e => e.Source) : null;
        var frontier = distance.Keys.ToList();
        for (var hop = 1; hop <= query.Depth && frontier.Count > 0; hop++)
        {
            var next = new List<string>();
            foreach (var node in frontier)
            {
                foreach (var neighbor in (outgoing?[node] ?? []).Concat(incoming?[node] ?? []))
                {
                    if (distance.TryAdd(neighbor, hop))
                    {
                        next.Add(neighbor);
                    }
                }
            }

            frontier = next;
        }

        var position = tree.Order.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);
        var kept = new HashSet<string>(StringComparer.Ordinal);
        var omitted = 0;
        foreach (var node in distance.OrderBy(d => d.Value).ThenBy(d => position[d.Key]).Select(d => d.Key))
        {
            var withAncestors = AncestorsOf(tree, node).Append(node).Where(id => !kept.Contains(id)).ToArray();
            if (kept.Count + withAncestors.Length > query.NodeBudget)
            {
                omitted++;
                continue;
            }

            kept.UnionWith(withAncestors);
        }

        return ([.. tree.Order.Where(kept.Contains)], [.. edges.Where(e => kept.Contains(e.Source) && kept.Contains(e.Target))], omitted);
    }

    /// <summary>Compound parents of a node, outermost first.</summary>
    private static IEnumerable<string> AncestorsOf(VisibleTree tree, string node)
    {
        var chain = new List<string>();
        for (var parent = tree.Nodes[node].ParentId; parent is not null; parent = tree.Nodes[parent].ParentId)
        {
            chain.Insert(0, parent);
        }

        return chain;
    }

    /// <summary>Breadcrumbs from the outermost container down to the focused node.</summary>
    private static IEnumerable<BreadcrumbDto> PathTo(VisibleTree tree, string focus) =>
        AncestorsOf(tree, focus).Append(focus).Select(id => tree.Nodes[id]).Select(node => new BreadcrumbDto(node.Id, node.Label, node.Kind switch
        {
            GraphNodeKinds.Module => GraphLevels.Module,
            GraphNodeKinds.Entity => GraphLevels.Entity,
            GraphNodeKinds.Group => GraphLevels.Repository,
            GraphNodeKinds.Folder => GraphLevels.Folder,
            _ => GraphLevels.Project,
        }));

    /// <summary>What the node budget left out and how to see it (GRAPH_ENGINE.md §4).</summary>
    private static TruncationDto? TruncationOf(VisibleTree tree)
    {
        if (tree.OmittedRootCount > 0)
        {
            return new TruncationDto(tree.OmittedRootCount + tree.RefusedChildCount, TruncationReasons.NodeBudget,
            [
                $"{tree.OmittedRootCount} elements with the fewest dependencies are collapsed into the \"more\" node.",
                "Use filters (language, ecosystem, relationship kind) to reduce the graph.",
                "Open the neighborhood of an element to see it with its dependencies only.",
            ]);
        }

        return tree.RefusedChildCount > 0
            ? new TruncationDto(tree.RefusedChildCount, TruncationReasons.ExpansionRefused,
            [
                $"{tree.RefusedExpansions.Count} expansion(s) would exceed the node budget and were not applied.",
                "Collapse other nodes, use filters or open the neighborhood of an element.",
            ])
            : null;
    }

    private static List<GraphEdgeDto> AggregateEdges(Dictionary<(string Source, string Target), List<CodeRelationship>> groups) =>
        [.. groups
            .Select(group =>
            {
                var levels = group.Value.SelectMany(r => r.Evidence).Select(e => e.Level).ToArray();
                return new GraphEdgeDto
                {
                    Id = EdgeId(group.Key.Source, group.Key.Target),
                    Source = group.Key.Source,
                    Target = group.Key.Target,
                    Count = group.Value.Count,
                    Occurrences = group.Value.Sum(r => r.Occurrences),
                    Kinds = group.Value
                        .GroupBy(r => r.Kind.ToKeyToken())
                        .OrderBy(g => g.Key, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    MaxLevel = Token(levels.Max()),
                    MinLevel = Token(levels.Min()),
                    IsDeclared = group.Value.Any(r => r.Kind is RelationshipKind.ProjectReference
                        or RelationshipKind.PackageReference or RelationshipKind.FrameworkReference),
                };
            })
            .OrderBy(e => e.Id, StringComparer.Ordinal)];

    /// <summary>Every relationship attributed to the ordered pair of the deepest visible nodes containing its endpoints.</summary>
    private static Dictionary<(string Source, string Target), List<CodeRelationship>> GroupRelationships(ArchitectureModel model, VisibleTree tree)
    {
        var groups = new Dictionary<(string Source, string Target), List<CodeRelationship>>();

        foreach (var relationship in model.Relationships)
        {
            if (!tree.Filter.Shows(relationship))
            {
                continue;
            }

            var source = tree.VisibleNodeOf(relationship.Source);
            var target = tree.VisibleNodeOf(relationship.Target);
            if (source is null || target is null || source == target || tree.IsAncestor(source, target) || tree.IsAncestor(target, source))
            {
                continue; // not visible, internal to one node, or between a compound node and its own content
            }

            var key = (source, target);
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }

            list.Add(relationship);
        }

        return groups;
    }

    /// <summary>Deterministic aggregate edge id (GRAPH_ENGINE.md §5).</summary>
    private static string EdgeId(string source, string target) => $"{source}|{target}";

    /// <summary>A readable name for a relationship endpoint (project, file path, module, entity, package).</summary>
    private static string ElementLabel(ArchitectureModel model, ElementReference reference) =>
        reference.Kind switch
        {
            ElementKind.Project => model.FindProject(reference.Id)?.Name,
            ElementKind.File => model.FindFile(reference.Id)?.Path.Value,
            ElementKind.Module => model.FindModule(reference.Id)?.QualifiedName,
            ElementKind.Entity => model.FindEntity(reference.Id)?.QualifiedName,
            ElementKind.ExternalDependency => model.FindExternalDependency(reference.Id)?.DisplayName,
            _ => null,
        } ?? reference.Id.ToString();

    private static string Token(EvidenceLevel level) => level switch
    {
        EvidenceLevel.Heuristic => GraphEvidenceLevels.Heuristic,
        EvidenceLevel.Syntactic => GraphEvidenceLevels.Syntactic,
        EvidenceLevel.Confirmed => GraphEvidenceLevels.Confirmed,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
