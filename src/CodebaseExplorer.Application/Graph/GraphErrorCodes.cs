namespace CodebaseExplorer.Application.Graph;

/// <summary>Stable <see cref="Common.ApplicationError"/> codes of graph projection.</summary>
public static class GraphErrorCodes
{
    /// <summary>The query names another repository than the model.</summary>
    public const string RepositoryMismatch = "graph.repository-mismatch";

    /// <summary>The requested level is not supported (yet).</summary>
    public const string LevelNotSupported = "graph.level-not-supported";

    /// <summary>The edge id does not denote an edge of the projected view.</summary>
    public const string EdgeNotFound = "graph.edge-not-found";

    /// <summary>The node budget is outside 2 … <c>GraphQuery.MaxNodeBudget</c>.</summary>
    public const string NodeBudgetInvalid = "graph.node-budget-invalid";

    /// <summary>Neighborhood depth outside 1 … 3 or an unknown direction.</summary>
    public const string NeighborhoodInvalid = "graph.neighborhood-invalid";

    /// <summary>The focused element is not part of the (filtered) graph.</summary>
    public const string FocusNotFound = "graph.focus-not-found";
}
