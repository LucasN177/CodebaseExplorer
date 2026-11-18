using LibGit2Sharp;

namespace CodebaseExplorer.TestRepositories.Git;

/// <summary>
/// Creates temporary Git repositories with scripted history for Git tests (GIT-002, GIT_INTEGRATION.md §7), so that
/// no nested <c>.git</c> folder is ever committed. Changes are collected with <see cref="Write"/>, <see cref="Delete"/>
/// and <see cref="Rename"/> and recorded by <see cref="Commit"/>; authors and times are explicit, so histories are
/// deterministic.
/// </summary>
/// <example>
/// <code>
/// using var repository = GitRepositoryBuilder.Create()
///     .Write("src/a.cs", "class A {}").Commit("add A", GitTestAuthor.Ada, at: day1)
///     .Rename("src/a.cs", "src/b.cs").Commit("rename", GitTestAuthor.Grace, at: day2)
///     .Branch("feature", checkout: true)
///     .Build();
/// </code>
/// </example>
public sealed class GitRepositoryBuilder
{
    /// <summary>Time of the first commit when no explicit time is given; later commits follow at one-hour steps.</summary>
    public static readonly DateTimeOffset DefaultStart = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The branch every built repository starts on.</summary>
    public const string DefaultBranch = "main";

    private readonly TempDirectory _directory;
    private readonly List<string> _commits = [];
    private DateTimeOffset _nextTime = DefaultStart;

    private GitRepositoryBuilder(TempDirectory directory, string workingDirectory, bool initialize)
    {
        _directory = directory;
        WorkingDirectory = workingDirectory;
        if (initialize)
        {
            Repository.Init(workingDirectory);

            // Independent of the machine's init.defaultBranch: every built repository starts on "main".
            File.WriteAllText(Path.Combine(workingDirectory, ".git", "HEAD"), $"ref: refs/heads/{DefaultBranch}\n");
        }
    }

    /// <summary>The working tree of the repository being built.</summary>
    public string WorkingDirectory { get; }

    /// <summary>Starts a new repository in a fresh temporary directory (working tree = <c>&lt;temp&gt;/repo</c>).</summary>
    public static GitRepositoryBuilder Create()
    {
        var directory = new TempDirectory();
        return new GitRepositoryBuilder(directory, directory.CreateDirectory("repo"), initialize: true);
    }

    /// <summary>Writes (creates or overwrites) a UTF-8 file in the working tree; recorded by the next <see cref="Commit"/>.</summary>
    public GitRepositoryBuilder Write(string relativePath, string content)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return this;
    }

    /// <summary>Writes <paramref name="lineCount"/> numbered lines (handy for line statistics).</summary>
    public GitRepositoryBuilder WriteLines(string relativePath, int lineCount, string prefix = "line") =>
        Write(relativePath, string.Concat(Enumerable.Range(1, lineCount).Select(i => $"{prefix} {i}\n")));

    /// <summary>Deletes a file from the working tree; recorded by the next <see cref="Commit"/>.</summary>
    public GitRepositoryBuilder Delete(string relativePath)
    {
        File.Delete(PathOf(relativePath));
        return this;
    }

    /// <summary>Moves a file; Git records a rename as delete + add (rename detection is a diff option, not history).</summary>
    public GitRepositoryBuilder Rename(string fromRelativePath, string toRelativePath)
    {
        var target = PathOf(toRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(PathOf(fromRelativePath), target);
        return this;
    }

    /// <summary>Stages every change of the working tree and commits it.</summary>
    /// <param name="message">Commit message.</param>
    /// <param name="author">Author and committer; <see cref="GitTestAuthor.Ada"/> if omitted.</param>
    /// <param name="at">Commit time; one hour after the previous commit if omitted.</param>
    public GitRepositoryBuilder Commit(string message, GitTestAuthor? author = null, DateTimeOffset? at = null)
    {
        var when = at ?? _nextTime;
        _nextTime = when.AddHours(1);
        var signature = (author ?? GitTestAuthor.Ada).ToSignature(when);

        using var repository = new Repository(WorkingDirectory);
        Commands.Stage(repository, "*");
        var commit = repository.Commit(message, signature, signature, new CommitOptions { AllowEmptyCommit = true });
        _commits.Add(commit.Sha);
        return this;
    }

    /// <summary>Creates a branch at HEAD and optionally checks it out.</summary>
    public GitRepositoryBuilder Branch(string name, bool checkout = false)
    {
        using var repository = new Repository(WorkingDirectory);
        var branch = repository.CreateBranch(name);
        if (checkout)
        {
            Commands.Checkout(repository, branch);
        }

        return this;
    }

    /// <summary>Checks out an existing branch.</summary>
    public GitRepositoryBuilder Checkout(string branchName)
    {
        using var repository = new Repository(WorkingDirectory);
        Commands.Checkout(repository, repository.Branches[branchName]);
        return this;
    }

    /// <summary>Detaches HEAD at the given commit (default: the current HEAD commit).</summary>
    public GitRepositoryBuilder DetachHead(string? commitSha = null)
    {
        using var repository = new Repository(WorkingDirectory);
        Commands.Checkout(repository, repository.Lookup<LibGit2Sharp.Commit>(commitSha ?? repository.Head.Tip.Sha));
        return this;
    }

    /// <summary>Merges <paramref name="branchName"/> into the current branch with a merge commit (no fast-forward).</summary>
    public GitRepositoryBuilder Merge(string branchName, GitTestAuthor? author = null, DateTimeOffset? at = null)
    {
        var when = at ?? _nextTime;
        _nextTime = when.AddHours(1);
        using var repository = new Repository(WorkingDirectory);
        var result = repository.Merge(repository.Branches[branchName], (author ?? GitTestAuthor.Ada).ToSignature(when),
            new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastForward });
        if (result.Commit is null)
        {
            throw new InvalidOperationException($"Merging '{branchName}' produced no commit ({result.Status}).");
        }

        _commits.Add(result.Commit.Sha);
        return this;
    }

    /// <summary>Adds a remote with the given URL (stored verbatim, e.g. to test sanitization).</summary>
    public GitRepositoryBuilder Remote(string name, string url)
    {
        using var repository = new Repository(WorkingDirectory);
        repository.Network.Remotes.Add(name, url);
        return this;
    }

    /// <summary>Appends raw text to <c>.git/config</c> (e.g. a hostile <c>core.fsmonitor</c>).</summary>
    public GitRepositoryBuilder AppendConfig(string configText)
    {
        File.AppendAllText(Path.Combine(GitDirectory, "config"), "\n" + configText + "\n");
        return this;
    }

    /// <summary>Writes an executable-looking hook script into <c>.git/hooks</c>.</summary>
    public GitRepositoryBuilder Hook(string name, string script)
    {
        var hooks = Directory.CreateDirectory(Path.Combine(GitDirectory, "hooks")).FullName;
        File.WriteAllText(Path.Combine(hooks, name), script);
        return this;
    }

    /// <summary>
    /// Adds a linked worktree for a new branch at HEAD, in <c>&lt;temp&gt;/worktrees/&lt;name&gt;</c> (its <c>.git</c> is a file).
    /// </summary>
    public GitRepositoryBuilder Worktree(string name)
    {
        var path = _directory.PathOf($"worktrees/{name}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var repository = new Repository(WorkingDirectory);
        repository.Worktrees.Add(name, path, isLocked: false);
        return this;
    }

    /// <summary>Finishes building; the returned repository owns (and deletes) the temporary directory.</summary>
    public GitTestRepository Build() => new(_directory, WorkingDirectory, [.. _commits]);

    private string GitDirectory => Path.Combine(WorkingDirectory, ".git");

    private string PathOf(string relativePath) =>
        Path.Combine(WorkingDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
