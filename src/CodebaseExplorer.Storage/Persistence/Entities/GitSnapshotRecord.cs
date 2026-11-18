namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>GitSnapshots</c> (§4.12).</summary>
internal sealed class GitSnapshotRecord
{
    public Guid Id { get; set; }

    public Guid RepositoryId { get; set; }

    public Guid AnalysisRunId { get; set; }

    public string HeadCommitSha { get; set; } = string.Empty;

    public string? Branch { get; set; }

    public bool IsDetachedHead { get; set; }

    public bool HasUncommittedChanges { get; set; }

    public string RemotesJson { get; set; } = "[]";

    public int HistoryDepth { get; set; }

    public DateTimeOffset? HistoryWindowStart { get; set; }

    public DateTimeOffset CapturedAt { get; set; }
}
