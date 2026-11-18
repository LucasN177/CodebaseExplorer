using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using LibGit2Sharp;

namespace CodebaseExplorer.Git.LibGit2;

/// <summary>
/// <see cref="IGitRepositoryReader"/> on LibGit2Sharp (GIT_INTEGRATION.md §3). libgit2 runs no hooks, fsmonitor,
/// filter drivers or credential helpers (verified in GIT-006, §3.1); this reader additionally never writes: no index
/// refresh is persisted, nothing is fetched. LibGit2Sharp is synchronous, so the work runs on the thread pool.
/// </summary>
internal sealed class LibGit2RepositoryReader : IGitRepositoryReader
{
    public Task<Result<GitRepositoryInfo>> OpenAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => Open(Path.GetFullPath(path)), cancellationToken);
    }

    public Task<Result<GitSnapshotInfo>> ReadSnapshotAsync(GitRepositoryInfo repository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return Task.Run(() => ReadSnapshot(repository, cancellationToken), cancellationToken);
    }

    private static Result<GitRepositoryInfo> Open(string rootPath)
    {
        string? gitDirectory;
        try
        {
            gitDirectory = Repository.Discover(rootPath);
        }
        catch (LibGit2SharpException exception)
        {
            return ReadFailed(rootPath, exception);
        }

        if (gitDirectory is null)
        {
            return NotARepository(rootPath);
        }

        try
        {
            using var repository = new Repository(gitDirectory);
            if (repository.Info.IsBare || repository.Info.WorkingDirectory is not { } workingDirectory)
            {
                return NotARepository(rootPath);
            }

            var workTree = TrimSeparators(Path.GetFullPath(workingDirectory));
            var root = TrimSeparators(rootPath);
            var prefix = Path.GetRelativePath(workTree, root);
            if (prefix == ".")
            {
                prefix = string.Empty;
            }
            else if (Path.IsPathRooted(prefix) || prefix == ".." || prefix.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                // The discovered work tree does not contain the folder (e.g. core.worktree points elsewhere).
                return NotARepository(rootPath);
            }

            return new GitRepositoryInfo(
                root,
                workTree,
                prefix.Replace(Path.DirectorySeparatorChar, '/'),
                IsLinkedWorktree: File.Exists(Path.Combine(workTree, ".git")),
                IsShallow: repository.Info.IsShallow);
        }
        catch (LibGit2SharpException exception)
        {
            return ReadFailed(rootPath, exception);
        }
    }

    private static Result<GitSnapshotInfo> ReadSnapshot(GitRepositoryInfo info, CancellationToken cancellationToken)
    {
        try
        {
            using var repository = new Repository(info.WorkingDirectory);
            var head = repository.Head;
            var detached = repository.Info.IsHeadDetached;
            cancellationToken.ThrowIfCancellationRequested();

            var status = repository.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = false,
                IncludeIgnored = false,
                ExcludeSubmodules = true,
                DisablePathSpecMatch = true,
                DetectRenamesInIndex = false,
                DetectRenamesInWorkDir = false,
            });
            cancellationToken.ThrowIfCancellationRequested();

            var remotes = repository.Network.Remotes
                .Select(r => new GitRemoteInfo(r.Name, GitRemoteUrls.Sanitize(r.Url ?? string.Empty)))
                .OrderBy(r => r.Name, StringComparer.Ordinal)
                .ToList();

            return new GitSnapshotInfo(
                head.Tip?.Sha,
                detached ? null : head.FriendlyName,
                detached,
                status.IsDirty,
                remotes);
        }
        catch (LibGit2SharpException exception)
        {
            return ReadFailed(info.RootPath, exception);
        }
    }

    private static ApplicationError NotARepository(string path) =>
        ApplicationError.NotFound(GitErrorCodes.NotARepository, "The folder is not inside a Git working tree.",
            new Dictionary<string, string> { ["path"] = path });

    private static ApplicationError ReadFailed(string path, LibGit2SharpException exception) =>
        ApplicationError.Failure(GitErrorCodes.ReadFailed, $"The Git repository could not be read: {exception.Message}",
            new Dictionary<string, string> { ["path"] = path });

    private static string TrimSeparators(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        return path.Length > root.Length ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
    }
}
