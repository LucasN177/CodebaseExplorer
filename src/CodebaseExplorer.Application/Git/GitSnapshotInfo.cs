namespace CodebaseExplorer.Application.Git;

/// <summary>Git state of a working tree at one point in time (<see cref="IGitRepositoryReader.ReadSnapshotAsync"/>).</summary>
/// <param name="HeadCommitSha">Full lowercase SHA of the HEAD commit; <see langword="null"/> for a repository without commits (unborn branch).</param>
/// <param name="Branch">Short name of the checked-out branch; <see langword="null"/> when HEAD is detached.</param>
/// <param name="IsDetachedHead">HEAD points to a commit, not to a branch.</param>
/// <param name="HasUncommittedChanges">Tracked files are modified, staged or deleted, or untracked files exist (ignored files do not count).</param>
/// <param name="Remotes">Remotes ordered by name, URLs sanitized with <see cref="GitRemoteUrls.Sanitize"/>.</param>
public sealed record GitSnapshotInfo(
    string? HeadCommitSha,
    string? Branch,
    bool IsDetachedHead,
    bool HasUncommittedChanges,
    IReadOnlyList<GitRemoteInfo> Remotes);

/// <summary>A configured remote; <paramref name="Url"/> never contains credentials.</summary>
public sealed record GitRemoteInfo(string Name, string Url);
