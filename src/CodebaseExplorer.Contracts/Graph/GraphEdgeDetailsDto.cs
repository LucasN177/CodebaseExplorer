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

/// <summary>One evidence record: which analyzer found the relationship, by which rule, and where.</summary>
public sealed record GraphEvidenceDto
{
    /// <summary>Analyzer id, e.g. <c>dotnet.msbuild-static</c>.</summary>
    public required string Analyzer { get; init; }

    /// <summary>Rule id, e.g. <c>csproj.ProjectReference</c>.</summary>
    public required string Rule { get; init; }

    /// <summary>Evidence level (<see cref="GraphEvidenceLevels"/>).</summary>
    public required string Level { get; init; }

    /// <summary>Repository-relative file path; <see langword="null"/> for evidence without a file location.</summary>
    public string? Path { get; init; }

    public int? StartLine { get; init; }

    public int? EndLine { get; init; }
}
