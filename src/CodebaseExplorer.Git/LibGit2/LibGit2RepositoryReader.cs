using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Git.History;
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

    public Task<Result<GitFileActivityReport>> ReadFileActivityAsync(
        GitRepositoryInfo repository, GitHistoryWindow window, IProgress<GitProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(window);
        return Task.Run<Result<GitFileActivityReport>>(() =>
        {
            try
            {
                return FileActivityWalker.Walk(repository, window, progress, cancellationToken);
            }
            catch (LibGit2SharpException exception)
            {
                return ReadFailed(repository.RootPath, exception);
            }
        }, cancellationToken);
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
            var prefix = RelativeInside(workTree, root);
            if (prefix is null)
            {
                // libgit2 reports the work tree with symbolic links resolved (macOS: /var → /private/var), so compare
                // the resolved paths and express the work tree in the caller's spelling where it is an ancestor.
                var resolvedWorkTree = ResolveLinks(workTree);
                prefix = RelativeInside(resolvedWorkTree, ResolveLinks(root));
                if (prefix is null)
                {
                    // The discovered work tree does not contain the folder (e.g. core.worktree points elsewhere).
                    return NotARepository(rootPath);
                }

                var ancestor = Ancestor(root, prefix);
                workTree = ancestor is not null && PathsEqual(ResolveLinks(ancestor), resolvedWorkTree) ? ancestor : resolvedWorkTree;
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

    /// <summary><paramref name="path"/> relative to <paramref name="directory"/> ("" for itself), or null if outside it.</summary>
    private static string? RelativeInside(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        if (relative == ".")
        {
            return string.Empty;
        }

        return Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? null
            : relative;
    }

    /// <summary>The ancestor of <paramref name="path"/> that <paramref name="relative"/> leads down from, if the segments match.</summary>
    private static string? Ancestor(string path, string relative)
    {
        var current = path;
        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (!PathsEqual(Path.GetFileName(current), segments[i]) || Path.GetDirectoryName(current) is not { } parent)
            {
                return null;
            }

            current = parent;
        }

        return TrimSeparators(current);
    }

    /// <summary>The path with every symbolic link (or junction) along it resolved; unresolvable parts stay as they are.</summary>
    private static string ResolveLinks(string path, int depth = 0)
    {
        const int MaxDepth = 32;
        var root = Path.GetPathRoot(path) ?? string.Empty;
        var current = root;
        try
        {
            foreach (var segment in path[root.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                var directory = new DirectoryInfo(current);
                if (depth < MaxDepth && directory.LinkTarget is not null && directory.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    current = ResolveLinks(Path.GetFullPath(target.FullName), depth + 1);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return TrimSeparators(path);
        }

        return TrimSeparators(current);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static string TrimSeparators(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        return path.Length > root.Length ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
    }
}
