namespace CodebaseExplorer.Contracts.Graph;

/// <summary>
/// What part of the architecture model to show (GRAPH_ENGINE.md §3). Currently supports the project
/// level with expansion into modules and entities (GRAPH-006) and the node budget (GRAPH-008); focus/neighborhood
/// and filters are added by GRAPH-007.
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
    /// Ids of nodes shown expanded (GRAPH-006): an expanded project shows its namespaces, an expanded module its
    /// sub-namespaces and types. Ids that are unknown, not visible or not expandable are ignored.
    /// </summary>
    public IReadOnlyList<string> Expanded { get; init; } = [];

    /// <summary>Default of <see cref="NodeBudget"/> (GRAPH_ENGINE.md §3).</summary>
    public const int DefaultNodeBudget = 500;

    /// <summary>Largest accepted <see cref="NodeBudget"/>.</summary>
    public const int MaxNodeBudget = 5_000;

    /// <summary>
    /// At most this many nodes are returned (GRAPH-008, 2 … <see cref="MaxNodeBudget"/>): surplus roots collapse into an
    /// "N more" aggregate node, expansions that do not fit are refused; both are reported in <c>Truncation</c>.
    /// </summary>
    public int NodeBudget { get; init; } = DefaultNodeBudget;
}
