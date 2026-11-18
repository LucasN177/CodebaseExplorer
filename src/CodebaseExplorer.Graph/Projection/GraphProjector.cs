using CodebaseExplorer.Application.Common;
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
/// ordered node pair; relationships inside one node or between a node and its own ancestor are dropped.
/// </summary>
internal sealed class GraphProjector : IGraphProjector
{
    public Result<GraphViewDto> Project(ArchitectureModel model, GraphQuery query)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(query);

        return Validate(model, query) is { } error ? error : ProjectView(model, query);
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
        var tree = VisibleTree.Build(model, query);
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

        if (query.Level != GraphLevels.Project)
        {
            return ApplicationError.Validation(GraphErrorCodes.LevelNotSupported,
                $"The graph level '{query.Level}' is not supported yet; supported: '{GraphLevels.Project}'.");
        }

        return null;
    }

    private static GraphViewDto ProjectView(ArchitectureModel model, GraphQuery query)
    {
        var tree = VisibleTree.Build(model, query);
        var edges = AggregateEdges(GroupRelationships(model, tree));

        var fanOut = edges.GroupBy(e => e.Source).ToDictionary(g => g.Key, g => g.Select(e => e.Target).Distinct().Count());
        var fanIn = edges.GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.Select(e => e.Source).Distinct().Count());

        return new GraphViewDto
        {
            RepositoryId = model.RepositoryId.ToString(),
            Level = GraphLevels.Project,
            // Parents before children (the web view adds nodes in order); roots sorted as before GRAPH-006.
            Nodes = [.. tree.Order.Select(id => tree.Nodes[id] with
            {
                Metrics = tree.Nodes[id].Metrics with { FanIn = fanIn.GetValueOrDefault(id), FanOut = fanOut.GetValueOrDefault(id) },
            })],
            Edges = edges,
        };
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
