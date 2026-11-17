namespace CodebaseExplorer.Contracts.Graph;

/// <summary>
/// What part of the architecture model to show (GRAPH_ENGINE.md §3). Currently supports the project
/// level; expansion, focus/neighborhood, filters and the node budget are added by GRAPH-006 … GRAPH-008.
/// </summary>
public sealed record GraphQuery
{
    /// <summary>Repository id (32 hex characters).</summary>
    public required string RepositoryId { get; init; }

    /// <summary>One of <see cref="GraphLevels"/>.</summary>
    public string Level { get; init; } = GraphLevels.Project;

    /// <summary>Show external packages as nodes (default false).</summary>
    public bool IncludeExternalDependencies { get; init; }
}
