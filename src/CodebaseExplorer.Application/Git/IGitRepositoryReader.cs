using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.Git;

/// <summary>
/// Reads Git metadata of a local folder (GIT_INTEGRATION.md §2). Read-only: never writes into the repository, never
/// runs hooks, <c>core.fsmonitor</c>, filter drivers or anything else the repository configures (SECURITY.md S2).
/// </summary>
public interface IGitRepositoryReader
{
    /// <summary>
    /// Detects the Git working tree that contains <paramref name="path"/> (the folder itself or a parent; linked
    /// worktrees and <c>.git</c> files are supported).
    /// </summary>
    /// <returns>
    /// The repository; <see cref="GitErrorCodes.NotARepository"/> (<see cref="ErrorKind.NotFound"/>) if the folder
    /// is not inside a working tree (also bare repositories); <see cref="GitErrorCodes.ReadFailed"/> if a repository
    /// exists but cannot be read (e.g. corrupt, or rejected by Git's ownership check).
    /// </returns>
    Task<Result<GitRepositoryInfo>> OpenAsync(string path, CancellationToken cancellationToken);

    /// <summary>HEAD commit, branch or detached state, uncommitted changes and sanitized remotes.</summary>
    Task<Result<GitSnapshotInfo>> ReadSnapshotAsync(GitRepositoryInfo repository, CancellationToken cancellationToken);
}
