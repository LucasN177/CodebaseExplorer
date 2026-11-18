namespace CodebaseExplorer.Desktop.Services;

/// <summary>Layouts of the graph web view (<c>view/layout</c>, GRAPH_ENGINE.md §8).</summary>
public enum GraphLayout
{
    /// <summary>Force-directed (fcose); the default because it stays fast on large graphs.</summary>
    Force,

    /// <summary>Layered top-down (dagre); dependencies below their dependents.</summary>
    Hierarchical,

    /// <summary>Rings by degree; the most connected nodes in the center.</summary>
    Concentric,
}

/// <summary>Tokens of <see cref="GraphLayout"/> — used on the wire and as the stored setting value (DATABASE_SCHEMA.md §4.17).</summary>
public static class GraphLayouts
{
    /// <summary>Layout used when nothing (or something unknown) is stored.</summary>
    public const GraphLayout Default = GraphLayout.Force;

    public static string ToToken(this GraphLayout layout) => layout switch
    {
        GraphLayout.Hierarchical => "hierarchical",
        GraphLayout.Concentric => "concentric",
        _ => "force",
    };

    /// <summary>Parses a token; <see langword="null"/>, empty or unknown values yield <see cref="Default"/>.</summary>
    public static GraphLayout Parse(string? token) => token switch
    {
        "hierarchical" => GraphLayout.Hierarchical,
        "concentric" => GraphLayout.Concentric,
        "force" => GraphLayout.Force,
        _ => Default,
    };

    /// <summary>Name shown in the layout switcher.</summary>
    public static string DisplayName(this GraphLayout layout) => layout switch
    {
        GraphLayout.Hierarchical => "Hierarchical",
        GraphLayout.Concentric => "Concentric",
        _ => "Force-directed",
    };
}
