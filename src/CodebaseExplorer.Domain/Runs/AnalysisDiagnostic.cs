using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Runs;

/// <summary>A warning, error or note reported during an analysis run.</summary>
public sealed record AnalysisDiagnostic
{
    public AnalysisDiagnostic(
        DiagnosticSeverity severity,
        string code,
        string message,
        AnalyzerId? analyzer = null,
        SourceLocation? location = null)
    {
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity), severity, null);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (analyzer is { Value: null })
        {
            throw new ArgumentException("An analyzer id must be initialized.", nameof(analyzer));
        }

        Severity = severity;
        Code = code.Trim();
        Message = message.Trim();
        Analyzer = analyzer;
        Location = location;
    }

    public DiagnosticSeverity Severity { get; }

    /// <summary>Stable machine-readable code, e.g. <c>CE-SCAN-001</c>.</summary>
    public string Code { get; }

    public string Message { get; }

    public AnalyzerId? Analyzer { get; }

    public SourceLocation? Location { get; }
}
