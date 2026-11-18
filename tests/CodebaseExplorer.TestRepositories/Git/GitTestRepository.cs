namespace CodebaseExplorer.TestRepositories.Git;

/// <summary>
/// A temporary Git repository created by <see cref="GitRepositoryBuilder"/>. Disposing deletes the whole temporary
/// directory, including Git's read-only object files (Windows) and linked worktrees.
/// </summary>
public sealed class GitTestRepository : IDisposable
{
    private readonly TempDirectory _directory;

    internal GitTestRepository(TempDirectory directory, string workingDirectory, IReadOnlyList<string> commits)
    {
        _directory = directory;
        WorkingDirectory = workingDirectory;
        Commits = commits;
    }

    /// <summary>The temporary directory holding the repository (and any linked worktree).</summary>
    public string TempRoot => _directory.FullPath;

    /// <summary>Absolute path of the working tree.</summary>
    public string WorkingDirectory { get; }

    /// <summary>SHAs of the commits made by the builder, oldest first.</summary>
    public IReadOnlyList<string> Commits { get; }

    /// <summary>Absolute path of a linked worktree created with <see cref="GitRepositoryBuilder.Worktree"/>.</summary>
    public string WorktreePath(string name) => _directory.PathOf($"worktrees/{name}");

    /// <summary>Absolute path of a file in the working tree.</summary>
    public string PathOf(string relativePath) =>
        Path.Combine(WorkingDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Creates a bare clone of this repository next to it (for clone tests through the local transport, which only
    /// tests may use — the product rejects <c>file://</c>, GIT_INTEGRATION.md §7).
    /// </summary>
    public string CreateBareClone(string name = "remote.git")
    {
        var path = _directory.PathOf(name);
        LibGit2Sharp.Repository.Clone(WorkingDirectory, path, new LibGit2Sharp.CloneOptions { IsBare = true });
        return path;
    }

    public void Dispose() => _directory.Dispose();
}
