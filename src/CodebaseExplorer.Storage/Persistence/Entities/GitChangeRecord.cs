namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>GitChanges</c> (§4.13).</summary>
internal sealed class GitChangeRecord
{
    public Guid GitSnapshotId { get; set; }

    public Guid FileId { get; set; }

    public int CommitCount { get; set; }

    public int DistinctAuthorCount { get; set; }

    public long? LinesAdded { get; set; }

    public long? LinesDeleted { get; set; }

    public DateTimeOffset? FirstCommitAt { get; set; }

    public DateTimeOffset? LastCommitAt { get; set; }

    public string? LastCommitSha { get; set; }
}
