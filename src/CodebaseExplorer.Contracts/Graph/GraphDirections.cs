namespace CodebaseExplorer.Contracts.Graph;

/// <summary>Which edges a neighborhood view follows from its focus (GRAPH-007).</summary>
public static class GraphDirections
{
    public const string Incoming = "incoming";
    public const string Outgoing = "outgoing";
    public const string Both = "both";
}
