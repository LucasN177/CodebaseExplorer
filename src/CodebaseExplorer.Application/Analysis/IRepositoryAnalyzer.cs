using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.Analysis;

/// <summary>
/// Runs the analysis pipeline over a repository (ANALYSIS_ENGINE.md §2). Never executes repository code.
/// </summary>
public interface IRepositoryAnalyzer
{
    /// <summary>
    /// Analyzes the repository. Analyzer failures are isolated and reported as <c>Error</c>
    /// diagnostics in a successful result; a model that fails validation (e.g. an id collision)
    /// yields <see cref="AnalysisErrorCodes.ModelInvalid"/>. Cancellation throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<Result<RepositoryAnalysis>> AnalyzeAsync(
        AnalysisRequest request,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken);
}
