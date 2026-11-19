namespace CodebaseExplorer.Application.Analysis;

/// <summary>
/// Safety limits and built-in excludes for analysis runs (ANALYSIS_ENGINE.md §5). Defaults follow the
/// documented values; the Settings page makes the size, file-count and time limits and additional excludes configurable (UI-009, <see cref="Settings.AnalysisSettings"/>).
/// </summary>
public sealed record AnalysisLimits
{
    /// <summary>Directory names that are never descended into (matched case-insensitively).</summary>
    public static readonly IReadOnlyList<string> DefaultExcludedDirectoryNames =
    [
        ".git", "bin", "obj", "node_modules", ".venv", "venv", "__pycache__", ".idea", ".vs", "dist", "build",
    ];

    public static AnalysisLimits Default { get; } = new();

    /// <summary>Files above this size are recorded but not parsed (default 2 MB).</summary>
    public long MaxParseFileBytes { get; init; } = 2 * 1024 * 1024;

    /// <summary>Manifests and ignore files above this size are not parsed (default 1 MB).</summary>
    public long MaxManifestBytes { get; init; } = 1024 * 1024;

    /// <summary>Scanning stops after this many files (default 500,000).</summary>
    public int MaxFiles { get; init; } = 500_000;

    /// <summary>Directories nested deeper than this below the root are skipped (default 64).</summary>
    public int MaxDirectoryDepth { get; init; } = 64;

    /// <summary>
    /// Time budget for parsing and extracting one source file (default 5 s, ANALYSIS_ENGINE.md §5). Tree-sitter
    /// cannot interrupt a running parse, so a file whose parse exceeds it is dropped afterwards; the extraction walk is
    /// cancelled when the budget runs out.
    /// </summary>
    public TimeSpan MaxParseDuration { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Parallel workers for file inspection and per-project analysis (default: processors − 1, at least 1).</summary>
    public int MaxDegreeOfParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>Directory names that are never descended into.</summary>
    public IReadOnlyList<string> ExcludedDirectoryNames { get; init; } = DefaultExcludedDirectoryNames;
}
