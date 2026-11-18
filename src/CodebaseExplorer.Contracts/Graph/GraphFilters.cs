namespace CodebaseExplorer.Contracts.Graph;

/// <summary>
/// Filters of a graph view (GRAPH_ENGINE.md §3, GRAPH-007). Empty lists mean "all". Hidden elements disappear with
/// their relationships; relationship filters only change which relationships are aggregated into edges.
/// </summary>
public sealed record GraphFilters
{
    public static GraphFilters Default { get; } = new();

    /// <summary>Language tokens (e.g. <c>csharp</c>, <c>typescript</c>); projects by primary language, code elements by their language.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Ecosystem tokens (<c>dotnet</c>, <c>node</c>, <c>python</c>, <c>other</c>); external dependencies by their package ecosystem.</summary>
    public IReadOnlyList<string> Ecosystems { get; init; } = [];

    /// <summary>Relationship kind tokens (e.g. <c>project-reference</c>, <c>imports</c>, <c>calls</c>).</summary>
    public IReadOnlyList<string> RelationshipKinds { get; init; } = [];

    /// <summary>Weakest evidence level shown (one of <see cref="GraphEvidenceLevels"/>); <see langword="null"/> shows all.</summary>
    public string? MinLevel { get; init; }

    /// <summary>
    /// Show test code (default false): test projects (all their code files are tests), code elements declared only in
    /// test files, and relationships found only in test files.
    /// </summary>
    public bool IncludeTests { get; init; }

    /// <summary>Show generated code (default false): code elements declared only in generated files and relationships found only there.</summary>
    public bool IncludeGenerated { get; init; }
}
