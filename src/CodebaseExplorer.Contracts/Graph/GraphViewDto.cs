namespace CodebaseExplorer.Contracts.Graph;

/// <summary>A complete graph view sent to the web view with <c>graph/load</c> (GRAPH_ENGINE.md §5).</summary>
public sealed record GraphViewDto
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string RepositoryId { get; init; }

    /// <summary>One of <see cref="GraphLevels"/>.</summary>
    public required string Level { get; init; }

    public IReadOnlyList<GraphNodeDto> Nodes { get; init; } = [];

    public IReadOnlyList<GraphEdgeDto> Edges { get; init; } = [];

    /// <summary>Set when nodes were omitted (node budget, GRAPH-008); <see langword="null"/> otherwise.</summary>
    public TruncationDto? Truncation { get; init; }

    public IReadOnlyList<BreadcrumbDto> Breadcrumbs { get; init; } = [];
}
