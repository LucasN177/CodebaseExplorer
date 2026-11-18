using CodebaseExplorer.TestRepositories;
using CodebaseExplorer.TestRepositories.Git;
using LibGit2Sharp;

namespace CodebaseExplorer.IntegrationTests.Git;

/// <summary>GIT-002: the test helper builds the histories it promises and cleans up after itself.</summary>
public sealed class GitRepositoryBuilderTests
{
    [Fact]
    public void Build_RecordsCommitsWithAuthorsTimesAndChanges()
    {
        var day1 = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.FromHours(1));
        using var built = GitRepositoryBuilder.Create()
            .WriteLines("src/a.txt", 3).Commit("add a", GitTestAuthor.Ada, day1)
            .Write("src/a.txt", "changed\n").Write("b.txt", "b\n").Commit("change a, add b", GitTestAuthor.Grace)
            .Rename("b.txt", "docs/b.txt").Delete("src/a.txt").Commit("move b, delete a", GitTestAuthor.Linus)
            .Build();

        using var repository = new Repository(built.WorkingDirectory);
        var commits = repository.Commits.QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Reverse | CommitSortStrategies.Topological }).ToList();

        Assert.Equal(built.Commits, commits.Select(c => c.Sha));
        Assert.Equal(["Ada Lovelace", "Grace Hopper", "Linus Torvalds"], commits.Select(c => c.Author.Name));
        Assert.Equal(day1, commits[0].Author.When);
        Assert.Equal(day1.AddHours(1), commits[1].Author.When);
        Assert.Equal(["docs/b.txt"], repository.Head.Tip.Tree.SelectMany(Flatten).Select(e => e.Path.Replace('\\', '/')));
        Assert.False(repository.RetrieveStatus().IsDirty);
    }

    [Fact]
    public void Build_CreatesBranchesMergesDetachedHeadsRemotesAndWorktrees()
    {
        using var built = GitRepositoryBuilder.Create()
            .Write("a.txt", "a\n").Commit("base")
            .Branch("feature", checkout: true).Write("f.txt", "f\n").Commit("feature work")
            .Checkout("main").Write("m.txt", "m\n").Commit("main work")
            .Merge("feature")
            .Remote("origin", "https://github.com/example/demo.git")
            .Worktree("side")
            .Build();

        using (var repository = new Repository(built.WorkingDirectory))
        {
            Assert.Equal("main", repository.Head.FriendlyName);
            Assert.Equal(2, repository.Head.Tip.Parents.Count());
            Assert.Equal(built.Commits[^1], repository.Head.Tip.Sha);
            Assert.Equal("https://github.com/example/demo.git", repository.Network.Remotes["origin"].Url);
        }

        Assert.True(File.Exists(Path.Combine(built.WorktreePath("side"), ".git")), "A linked worktree has a .git file.");
        using (var worktree = new Repository(built.WorktreePath("side")))
        {
            Assert.Equal("side", worktree.Head.FriendlyName);
        }
    }

    [Fact]
    public void DetachHead_LeavesNoBranchCheckedOut()
    {
        using var built = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one").Write("a.txt", "b\n").Commit("two").Build();
        using var detached = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one").DetachHead().Build();

        using var repository = new Repository(detached.WorkingDirectory);
        Assert.True(repository.Info.IsHeadDetached);
    }

    [Fact]
    public void CreateBareClone_ProducesARemoteForCloneTests()
    {
        using var built = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one").Build();

        var bare = built.CreateBareClone();

        using var repository = new Repository(bare);
        Assert.True(repository.Info.IsBare);
        Assert.Equal(built.Commits[0], repository.Head.Tip.Sha);
    }

    [Fact]
    public void Dispose_DeletesTheRepositoryIncludingReadOnlyObjectFiles()
    {
        var built = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one").Worktree("side").Build();
        var root = built.TempRoot;
        Assert.Contains(
            Directory.EnumerateFiles(Path.Combine(built.WorkingDirectory, ".git", "objects"), "*", SearchOption.AllDirectories),
            f => File.GetAttributes(f).HasFlag(FileAttributes.ReadOnly) || !OperatingSystem.IsWindows());

        built.Dispose();

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Fixtures_ContainNoNestedGitRepository()
    {
        var nested = Directory.EnumerateFileSystemEntries(TestRepositoryCatalog.FixturesRoot, ".git", SearchOption.AllDirectories).ToList();

        Assert.Empty(nested);
    }

    private static IEnumerable<TreeEntry> Flatten(TreeEntry entry) =>
        entry.TargetType == TreeEntryTargetType.Tree ? ((Tree)entry.Target).SelectMany(Flatten) : [entry];
}
