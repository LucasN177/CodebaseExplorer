namespace CodebaseExplorer.Contracts.Graph;

/// <summary>A graph node. Contains names, paths and metrics only — never source code.</summary>
public sealed record GraphNodeDto
{
    /// <summary>Element id (32 hex characters).</summary>
    public required string Id { get; init; }

    /// <summary>Compound parent node, if any.</summary>
    public string? ParentId { get; init; }

    /// <summary>One of <see cref="GraphNodeKinds"/>.</summary>
    public required string Kind { get; init; }

    public required string Label { get; init; }

    /// <summary>Secondary text, e.g. the repository-relative root directory.</summary>
    public string? Sublabel { get; init; }

    /// <summary>Language token, e.g. <c>csharp</c>.</summary>
    public string? Language { get; init; }

    /// <summary>Ecosystem token, e.g. <c>dotnet</c> or <c>nuget</c>.</summary>
    public string? Ecosystem { get; init; }

    public bool IsExpandable { get; init; }

    public bool IsExpanded { get; init; }

    /// <summary>Number of child elements one level down (e.g. modules of a project).</summary>
    public int ChildCount { get; init; }

    public required GraphNodeMetricsDto Metrics { get; init; }

    /// <summary>Values of <see cref="GraphBadges"/>.</summary>
    public IReadOnlyList<string> Badges { get; init; } = [];
}
