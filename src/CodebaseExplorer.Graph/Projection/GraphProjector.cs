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
/// (GRAPH_ENGINE.md §2). Supports the project level (L1): one node per project (and optionally per
/// external dependency); every relationship is attributed to the projects that own its endpoints and
/// aggregated into one edge per ordered node pair.
/// </summary>
internal sealed class GraphProjector : IGraphProjector
{
    public Result<GraphViewDto> Project(ArchitectureModel model, GraphQuery query)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(query);

        return Validate(model, query) is { } error ? error : ProjectLevel(model, query);
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

        // Same attribution as the view, so the details always match the edge the user selected.
        var group = GroupRelationships(model, query.IncludeExternalDependencies)
            .FirstOrDefault(g => string.Equals(EdgeId(g.Key.Source, g.Key.Target), edgeId, StringComparison.OrdinalIgnoreCase));
        if (group.Value is null)
        {
            return ApplicationError.NotFound(GraphErrorCodes.EdgeNotFound, "The edge is not part of the graph.");
        }

        return new GraphEdgeDetailsDto
        {
            EdgeId = EdgeId(group.Key.Source, group.Key.Target),
            SourceLabel = NodeLabel(model, group.Key.Source),
            TargetLabel = NodeLabel(model, group.Key.Target),
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

    private static GraphViewDto ProjectLevel(ArchitectureModel model, GraphQuery query)
    {
        var edges = AggregateEdges(model, query.IncludeExternalDependencies);

        var fanOut = edges.GroupBy(e => e.Source).ToDictionary(g => g.Key, g => g.Select(e => e.Target).Distinct().Count());
        var fanIn = edges.GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.Select(e => e.Source).Distinct().Count());
        var filesPerProject = model.Files
            .Where(f => f.ProjectId is not null)
            .GroupBy(f => f.ProjectId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        var topLevelModules = model.Modules
            .Where(m => m.ParentModuleId is null)
            .GroupBy(m => m.ProjectId)
            .ToDictionary(g => g.Key, g => g.Count());

        var nodes = new List<GraphNodeDto>();
        foreach (var project in model.Projects)
        {
            var id = project.Id.ToString();
            nodes.Add(new GraphNodeDto
            {
                Id = id,
                Kind = GraphNodeKinds.Project,
                Label = project.Name,
                Sublabel = project.RootDirectory.ToString(),
                Language = project.PrimaryLanguage.Value,
                Ecosystem = project.Ecosystem.ToKeyToken(),
                ChildCount = topLevelModules.GetValueOrDefault(project.Id),
                // Expansion into modules is implemented by GRAPH-006; until then nothing is expandable.
                IsExpandable = false,
                Metrics = new GraphNodeMetricsDto(fanIn.GetValueOrDefault(id), fanOut.GetValueOrDefault(id), filesPerProject.GetValueOrDefault(project.Id)),
                Badges = project.IsPlaceholder() ? [GraphBadges.Missing] : [],
            });
        }

        if (query.IncludeExternalDependencies)
        {
            foreach (var dependency in model.ExternalDependencies)
            {
                var id = dependency.Id.ToString();
                nodes.Add(new GraphNodeDto
                {
                    Id = id,
                    Kind = GraphNodeKinds.ExternalDependency,
                    Label = dependency.DisplayName,
                    Ecosystem = dependency.Ecosystem.ToKeyToken(),
                    Metrics = new GraphNodeMetricsDto(fanIn.GetValueOrDefault(id), fanOut.GetValueOrDefault(id), 0),
                });
            }
        }

        return new GraphViewDto
        {
            RepositoryId = model.RepositoryId.ToString(),
            Level = GraphLevels.Project,
            Nodes = [.. nodes
                .OrderBy(n => n.Kind == GraphNodeKinds.Project ? 0 : 1)
                .ThenBy(n => n.Label, StringComparer.Ordinal)
                .ThenBy(n => n.Id, StringComparer.Ordinal)],
            Edges = edges,
        };
    }

    private static List<GraphEdgeDto> AggregateEdges(ArchitectureModel model, bool includeExternalDependencies) =>
        [.. GroupRelationships(model, includeExternalDependencies)
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

    /// <summary>Every relationship attributed to the ordered pair of visible nodes owning its endpoints.</summary>
    private static Dictionary<(string Source, string Target), List<CodeRelationship>> GroupRelationships(
        ArchitectureModel model, bool includeExternalDependencies)
    {
        var groups = new Dictionary<(string Source, string Target), List<CodeRelationship>>();

        foreach (var relationship in model.Relationships)
        {
            var source = VisibleNode(model, relationship.Source, includeExternalDependencies);
            var target = VisibleNode(model, relationship.Target, includeExternalDependencies);
            if (source is null || target is null || source == target)
            {
                continue; // not visible at this level, or internal to one node
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

    private static string NodeLabel(ArchitectureModel model, string nodeId)
    {
        var id = ElementId.Parse(nodeId);
        return model.FindProject(id)?.Name ?? model.FindExternalDependency(id)?.DisplayName ?? nodeId;
    }

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

    /// <summary>The id of the visible node an endpoint belongs to, or <see langword="null"/> if it is not shown.</summary>
    private static string? VisibleNode(ArchitectureModel model, ElementReference reference, bool includeExternalDependencies) =>
        reference.Kind switch
        {
            ElementKind.Project => reference.Id.ToString(),
            ElementKind.File => model.FindFile(reference.Id)?.ProjectId?.ToString(),
            ElementKind.Module => model.FindModule(reference.Id)?.ProjectId.ToString(),
            ElementKind.Entity => model.FindEntity(reference.Id)?.ProjectId.ToString(),
            ElementKind.ExternalDependency => includeExternalDependencies ? reference.Id.ToString() : null,
            _ => null,
        };

    private static string Token(EvidenceLevel level) => level switch
    {
        EvidenceLevel.Heuristic => GraphEvidenceLevels.Heuristic,
        EvidenceLevel.Syntactic => GraphEvidenceLevels.Syntactic,
        EvidenceLevel.Confirmed => GraphEvidenceLevels.Confirmed,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
