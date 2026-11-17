namespace CodebaseExplorer.Contracts.Graph;

/// <summary>Graph levels (GRAPH_ENGINE.md §2) as used on the wire.</summary>
public static class GraphLevels
{
    public const string Repository = "repository";
    public const string Project = "project";
    public const string Module = "module";
    public const string Entity = "entity";
}
