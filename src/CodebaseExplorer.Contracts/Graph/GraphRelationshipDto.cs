namespace CodebaseExplorer.Contracts.Graph;

/// <summary>One stored relationship of an aggregated edge.</summary>
public sealed record GraphRelationshipDto
{
    /// <summary>Relationship kind token, e.g. <c>project-reference</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Evidence level of the relationship (<see cref="GraphEvidenceLevels"/>).</summary>
    public required string Level { get; init; }

    /// <summary>The relationship's own source element (a project, file, module, entity …).</summary>
    public required string SourceLabel { get; init; }

    public required string TargetLabel { get; init; }

    /// <summary>At least one record (every relationship has evidence; DOMAIN_MODEL.md §4.8).</summary>
    public IReadOnlyList<GraphEvidenceDto> Evidence { get; init; } = [];
}
