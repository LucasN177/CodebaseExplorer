using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>AnalysisDiagnostics</c> (§4.3).</summary>
internal sealed class AnalysisDiagnosticRecord
{
    public long Id { get; set; }

    public Guid AnalysisRunId { get; set; }

    /// <summary>Position within the run, so diagnostics are read back in the order they were reported.</summary>
    public int Ordinal { get; set; }

    public DiagnosticSeverity Severity { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? AnalyzerId { get; set; }

    /// <summary>No foreign key: the file may not exist in the current model.</summary>
    public Guid? FileId { get; set; }

    public int? StartLine { get; set; }

    public int? StartColumn { get; set; }

    public int? EndLine { get; set; }

    public int? EndColumn { get; set; }
}
