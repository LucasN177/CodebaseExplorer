namespace CodebaseExplorer.Contracts.Graph;

/// <summary>One evidence record: which analyzer found the relationship, by which rule, and where.</summary>
public sealed record GraphEvidenceDto
{
    /// <summary>Analyzer id, e.g. <c>dotnet.msbuild-static</c>.</summary>
    public required string Analyzer { get; init; }

    /// <summary>Rule id, e.g. <c>csproj.ProjectReference</c>.</summary>
    public required string Rule { get; init; }

    /// <summary>Evidence level (<see cref="GraphEvidenceLevels"/>).</summary>
    public required string Level { get; init; }

    /// <summary>Repository-relative file path; <see langword="null"/> for evidence without a file location.</summary>
    public string? Path { get; init; }

    public int? StartLine { get; init; }

    public int? EndLine { get; init; }
}
