namespace CodebaseExplorer.Contracts.Graph;

/// <summary>Values of <see cref="TruncationDto.Reason"/> (GRAPH-008).</summary>
public static class TruncationReasons
{
    /// <summary>More roots than the node budget: the ones with the lowest degree are collapsed into an aggregate node.</summary>
    public const string NodeBudget = "node-budget";

    /// <summary>An expansion was refused because its children would exceed the node budget.</summary>
    public const string ExpansionRefused = "expansion-refused";
}
