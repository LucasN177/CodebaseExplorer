using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>AnalysisRuns</c> (§4.2).</summary>
internal sealed class AnalysisRunRecord
{
    public Guid Id { get; set; }

    public Guid RepositoryId { get; set; }

    public AnalysisRunStatus Status { get; set; }

    public AnalysisTrigger Trigger { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? CommitSha { get; set; }

    public string? Branch { get; set; }

    public string AnalyzerVersionsJson { get; set; } = "{}";

    public string StatisticsJson { get; set; } = "{}";

    public List<AnalysisDiagnosticRecord> Diagnostics { get; set; } = [];
}
