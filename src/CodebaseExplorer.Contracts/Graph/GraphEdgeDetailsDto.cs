namespace CodebaseExplorer.Contracts.Graph;

/// <summary>
/// The stored relationships behind one aggregated graph edge, with their evidence (GRAPH-010,
/// GRAPH_ENGINE.md §5). Names, paths and line numbers only — never source code.
/// </summary>
public sealed record GraphEdgeDetailsDto
{
    /// <summary>The aggregated edge id (<c>source|target</c>).</summary>
    public required string EdgeId { get; init; }

    public required string SourceLabel { get; init; }

    public required string TargetLabel { get; init; }

    /// <summary>Ordered by kind, then source and target label.</summary>
    public IReadOnlyList<GraphRelationshipDto> Relationships { get; init; } = [];
}
