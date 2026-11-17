namespace CodebaseExplorer.Contracts.Graph;

/// <summary>Evidence levels as used on the wire (ordered: heuristic &lt; syntactic &lt; confirmed).</summary>
public static class GraphEvidenceLevels
{
    public const string Heuristic = "heuristic";
    public const string Syntactic = "syntactic";
    public const string Confirmed = "confirmed";
}
