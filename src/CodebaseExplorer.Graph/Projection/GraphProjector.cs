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

        if (!string.Equals(query.RepositoryId, model.RepositoryId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationError.Validation(GraphErrorCodes.RepositoryMismatch, "The query is for another repository than the model.");
        }

        if (query.Level != GraphLevels.Project)
        {
            return ApplicationError.Validation(GraphErrorCodes.LevelNotSupported,
                $"The graph level '{query.Level}' is not supported yet; supported: '{GraphLevels.Project}'.");
        }

        return ProjectLevel(model, query);
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

    private static List<GraphEdgeDto> AggregateEdges(ArchitectureModel model, bool includeExternalDependencies)
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

        return [.. groups
            .Select(group =>
            {
                var levels = group.Value.SelectMany(r => r.Evidence).Select(e => e.Level).ToArray();
                return new GraphEdgeDto
                {
                    Id = $"{group.Key.Source}|{group.Key.Target}",
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
    }

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
