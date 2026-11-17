using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.Scanning;

/// <summary>Output of the scan stage: files sorted by path (ordinal), statistics and diagnostics.</summary>
internal sealed record ScanResult(
    IReadOnlyList<ScannedFile> Files,
    ScanStatistics Statistics,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics);
