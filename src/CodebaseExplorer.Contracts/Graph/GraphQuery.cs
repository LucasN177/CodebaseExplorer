namespace CodebaseExplorer.Contracts.Graph;

/// <summary>
/// What part of the architecture model to show (GRAPH_ENGINE.md §3). Currently supports the project
/// level with expansion into modules and entities (GRAPH-006); focus/neighborhood, filters and the node budget
/// are added by GRAPH-007 … GRAPH-008.
/// </summary>
public sealed record GraphQuery
{
    /// <summary>Repository id (32 hex characters).</summary>
    public required string RepositoryId { get; init; }

    /// <summary>One of <see cref="GraphLevels"/>.</summary>
    public string Level { get; init; } = GraphLevels.Project;

    /// <summary>Show external packages as nodes (default false).</summary>
    public bool IncludeExternalDependencies { get; init; }

    /// <summary>
    /// Fill <see cref="GraphNodeMetricsDto.ChangeCount"/> from the stored Git activity (GIT-005 activity overlay; default
    /// false). Without Git data the counts stay <see langword="null"/>.
    /// </summary>
    public bool IncludeActivity { get; init; }

    /// <summary>
    /// Ids of nodes shown expanded (GRAPH-006): an expanded project shows its namespaces, an expanded module its
    /// sub-namespaces and types. Ids that are unknown, not visible or not expandable are ignored.
    /// </summary>
    public IReadOnlyList<string> Expanded { get; init; } = [];
}
