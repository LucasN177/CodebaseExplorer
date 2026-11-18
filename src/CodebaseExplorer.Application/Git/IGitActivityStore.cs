using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.Git;

/// <summary>
/// Persistent Git data of analysis runs (GIT-005, DATABASE_SCHEMA.md §4.12–4.13): one snapshot per run and per-file
/// change statistics mapped to the files of the run's model. Author identities are never stored (only counts).
/// </summary>
public interface IGitActivityStore
{
    /// <summary>
    /// Stores the snapshot of <paramref name="runId"/> and the statistics of the files that exist in the repository's
    /// current model (matched by repository-relative path; statistics of other paths are dropped). Call after the
    /// run's model was saved; a repository without commits (no HEAD) stores nothing.
    /// </summary>
    Task SaveAsync(
        RepositoryId repositoryId,
        AnalysisRunId runId,
        GitSnapshotInfo snapshot,
        GitFileActivityReport activity,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken);

    /// <summary>The Git data of the run that produced the repository's current model; <see langword="null"/> without Git data.</summary>
    Task<GitActivity?> LoadCurrentAsync(RepositoryId repositoryId, CancellationToken cancellationToken);
}

/// <summary>Git data of one analysis run.</summary>
/// <param name="Snapshot">Repository state when the run started.</param>
/// <param name="Files">Statistics per file id of the run's model (files without commits in the window are absent).</param>
public sealed record GitActivity(GitActivitySnapshot Snapshot, IReadOnlyDictionary<ElementId, GitFileChanges> Files);

/// <summary>A stored snapshot (table <c>GitSnapshots</c>).</summary>
/// <param name="HeadCommitSha">HEAD commit.</param>
/// <param name="Branch">Branch; <see langword="null"/> when detached.</param>
/// <param name="IsDetachedHead">HEAD was detached.</param>
/// <param name="HasUncommittedChanges">The working tree had uncommitted changes (the analysis saw them, the history did not).</param>
/// <param name="Remotes">Sanitized remotes.</param>
/// <param name="HistoryDepth">Commits scanned for the statistics.</param>
/// <param name="HistoryWindowStart">Committer time of the oldest scanned commit.</param>
/// <param name="CapturedAt">When the snapshot was taken.</param>
public sealed record GitActivitySnapshot(
    string HeadCommitSha,
    string? Branch,
    bool IsDetachedHead,
    bool HasUncommittedChanges,
    IReadOnlyList<GitRemoteInfo> Remotes,
    int HistoryDepth,
    DateTimeOffset? HistoryWindowStart,
    DateTimeOffset CapturedAt);

/// <summary>Stored statistics of one file (table <c>GitChanges</c>), see <see cref="GitFileActivity"/>.</summary>
public sealed record GitFileChanges(
    int CommitCount,
    int DistinctAuthorCount,
    long LinesAdded,
    long LinesDeleted,
    DateTimeOffset? FirstCommitAt,
    DateTimeOffset? LastCommitAt,
    string? LastCommitSha);
