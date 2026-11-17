namespace CodebaseExplorer.Contracts.Graph;

/// <summary>An aggregated edge between two visible nodes (GRAPH_ENGINE.md §2).</summary>
public sealed record GraphEdgeDto
{
    /// <summary>Deterministic id <c>source|target</c>.</summary>
    public required string Id { get; init; }

    public required string Source { get; init; }

    public required string Target { get; init; }

    /// <summary>Number of underlying relationships.</summary>
    public int Count { get; init; }

    /// <summary>Number of evidence records of the underlying relationships.</summary>
    public int Occurrences { get; init; }

    /// <summary>Relationship kind token (e.g. <c>project-reference</c>) → number of relationships.</summary>
    public IReadOnlyDictionary<string, int> Kinds { get; init; } = new Dictionary<string, int>();

    /// <summary>Strongest evidence level (<see cref="GraphEvidenceLevels"/>).</summary>
    public required string MaxLevel { get; init; }

    /// <summary>Weakest evidence level (<see cref="GraphEvidenceLevels"/>).</summary>
    public required string MinLevel { get; init; }

    /// <summary>True if at least one underlying relationship is declared in a manifest (project/package/framework reference).</summary>
    public bool IsDeclared { get; init; }
}
