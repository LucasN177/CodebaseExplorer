using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Application.Analysis;

/// <summary>
/// Output of a successful analysis: a validated model plus everything the caller needs to complete
/// the <see cref="AnalysisRun"/> (diagnostics, statistics, analyzer versions).
/// </summary>
/// <param name="Model">The validated architecture model.</param>
/// <param name="Diagnostics">All diagnostics; an <c>Error</c> among them makes the run partially succeeded.</param>
/// <param name="Statistics">Counts and stage durations.</param>
/// <param name="AnalyzerVersions">Analyzer id → version of every analyzer that ran.</param>
public sealed record RepositoryAnalysis(
    ArchitectureModel Model,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics,
    AnalysisStatistics Statistics,
    IReadOnlyDictionary<string, string> AnalyzerVersions);
