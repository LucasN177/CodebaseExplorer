namespace CodebaseExplorer.Contracts.Graph;

/// <summary>Node kinds as used on the wire.</summary>
public static class GraphNodeKinds
{
    public const string Project = "project";
    public const string ExternalDependency = "external-dependency";
    public const string Module = "module";
    public const string Entity = "entity";

    /// <summary>"N more": roots left out by the node budget (GRAPH-008); not expandable, carries their edges.</summary>
    public const string Aggregate = "aggregate";
}
