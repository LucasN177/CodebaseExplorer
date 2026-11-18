namespace CodebaseExplorer.Application.Git;

/// <summary>
/// Change statistics of one file within a <see cref="GitHistoryWindow"/> (GIT-004, DATABASE_SCHEMA.md §4.13). Author
/// identities are counted only, never kept (GIT_INTEGRATION.md §6).
/// </summary>
/// <param name="Path">Path relative to the analyzed folder (<see cref="GitRepositoryInfo.RootPath"/>), <c>/</c> separators.</param>
/// <param name="CommitCount">Non-merge commits in the window that added, modified or deleted the file.</param>
/// <param name="DistinctAuthorCount">Distinct authors of these commits (by e-mail, case-insensitive; by name if the e-mail is empty).</param>
/// <param name="LinesAdded">Added lines over these commits (diff against the first parent, binary files count 0).</param>
/// <param name="LinesDeleted">Deleted lines over these commits.</param>
/// <param name="FirstCommitAt">Committer time of the oldest of these commits.</param>
/// <param name="LastCommitAt">Committer time of the newest of these commits.</param>
/// <param name="LastCommitSha">The newest of these commits.</param>
public sealed record GitFileActivity(
    string Path,
    int CommitCount,
    int DistinctAuthorCount,
    long LinesAdded,
    long LinesDeleted,
    DateTimeOffset FirstCommitAt,
    DateTimeOffset LastCommitAt,
    string LastCommitSha);

/// <summary>Result of a history walk: per-file statistics and what was scanned.</summary>
/// <param name="Files">Files under the analyzed folder with at least one commit in the window, ordered by path (ordinal).</param>
/// <param name="CommitsScanned">Non-merge commits whose changes were counted (the snapshot's <c>HistoryDepth</c>).</param>
/// <param name="OldestCommitAt">Committer time of the oldest counted commit; <see langword="null"/> if none was counted.</param>
/// <param name="ReachedCommitLimit">The walk stopped at <see cref="GitHistoryWindow.MaxCommits"/>; older history exists.</param>
public sealed record GitFileActivityReport(
    IReadOnlyList<GitFileActivity> Files,
    int CommitsScanned,
    DateTimeOffset? OldestCommitAt,
    bool ReachedCommitLimit);

/// <summary>Progress of a Git operation (history walk, clone, checkout).</summary>
/// <param name="Stage">What is running.</param>
/// <param name="Completed">Units done (commits, objects).</param>
/// <param name="Total">Units in total; 0 if unknown.</param>
public readonly record struct GitProgress(GitProgressStage Stage, long Completed, long Total);

/// <summary>Stages reported through <see cref="GitProgress"/>.</summary>
public enum GitProgressStage
{
    /// <summary>Walking the history for activity statistics.</summary>
    ReadingHistory,

    /// <summary>Receiving objects from the remote (clone, update); units are objects.</summary>
    Receiving,

    /// <summary>Writing the working tree (clone, update); units are files.</summary>
    CheckingOut,
}
