using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.FileSystem;

/// <summary>Turns file-reader errors into analysis diagnostics (ANALYSIS_ENGINE.md §5).</summary>
internal static class FileAccessDiagnostics
{
    /// <summary>
    /// Severity: too large → <see cref="DiagnosticSeverity.Info"/> (the file is recorded but not parsed);
    /// link not followed, outside root, not found, unreadable → <see cref="DiagnosticSeverity.Warning"/>.
    /// </summary>
    public static AnalysisDiagnostic From(ApplicationError error, AnalyzerId? analyzer = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        var severity = error.Code == RepositoryErrorCodes.FileTooLarge ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning;
        var message = error.Details.TryGetValue("path", out var path) ? $"{path}: {error.Message}" : error.Message;

        return new AnalysisDiagnostic(severity, error.Code, message, analyzer);
    }
}
