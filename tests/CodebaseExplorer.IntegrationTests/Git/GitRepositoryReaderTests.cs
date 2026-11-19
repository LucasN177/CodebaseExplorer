using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Git;
using CodebaseExplorer.IntegrationTests.Security;
using CodebaseExplorer.TestRepositories;
using CodebaseExplorer.TestRepositories.Git;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Git;

/// <summary>GIT-001: detection and snapshot of local repositories through the LibGit2Sharp adapter.</summary>
public sealed class GitRepositoryReaderTests
{
    private readonly IGitRepositoryReader _reader = new ServiceCollection().AddGitModule().BuildServiceProvider()
        .GetRequiredService<IGitRepositoryReader>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Open_WorkingTreeRoot_ReturnsRepositoryWithoutPrefix()
    {
        using var built = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one").Build();

        var info = (await _reader.OpenAsync(built.WorkingDirectory, Ct)).Value;

        Assert.Equal(built.WorkingDirectory, info.WorkingDirectory);
        Assert.Equal(built.WorkingDirectory, info.RootPath);
        Assert.Equal(string.Empty, info.RootPrefix);
        Assert.False(info.IsLinkedWorktree);
        Assert.False(info.IsShallow);
    }

    [Fact]
    public async Task Open_SubfolderOfWorkingTree_ReturnsPrefix()
    {
        using var built = GitRepositoryBuilder.Create().Write("src/app/a.txt", "a\n").Commit("one").Build();

        var info = (await _reader.OpenAsync(built.PathOf("src/app") + Path.DirectorySeparatorChar, Ct)).Value;

        Assert.Equal(built.WorkingDirectory, info.WorkingDirectory);
        Assert.Equal("src/app", info.RootPrefix);
    }

    [Fact]
    public async Task Open_LinkedWorktree_IsSupported()
    {
        using var built = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one").Worktree("side").Build();

        var info = (await _reader.OpenAsync(built.WorktreePath("side"), Ct)).Value;
        var snapshot = (await _reader.ReadSnapshotAsync(info, Ct)).Value;

        Assert.True(info.IsLinkedWorktree);
        Assert.Equal(built.WorktreePath("side"), info.WorkingDirectory);
        Assert.Equal("side", snapshot.Branch);
        Assert.Equal(built.Commits[0], snapshot.HeadCommitSha);
    }

    [Fact]
    public async Task Open_ThroughDirectoryLink_KeepsTheCallersSpelling()
    {
        // macOS: the temp folder /var/... is a link to /private/var/..., and libgit2 reports the resolved work tree.
        using var built = GitRepositoryBuilder.Create().Write("src/a.txt", "a\n").Commit("one").Build();
        using var links = new TempDirectory();
        if (!links.TryCreateSymbolicLink("alias", built.WorkingDirectory, isDirectory: true)
            && !links.TryCreateJunction("alias", built.WorkingDirectory))
        {
            Assert.Skip("Directory links cannot be created on this machine.");
        }

        var alias = links.PathOf("alias");
        var info = (await _reader.OpenAsync(Path.Combine(alias, "src"), Ct)).Value;
        var snapshot = (await _reader.ReadSnapshotAsync(info, Ct)).Value;

        Assert.Equal(Path.Combine(alias, "src"), info.RootPath);
        Assert.Equal(alias, info.WorkingDirectory);
        Assert.Equal("src", info.RootPrefix);
        Assert.Equal(built.Commits[0], snapshot.HeadCommitSha);
    }

    [Fact]
    public async Task Open_FolderWithoutRepository_ReturnsNotFound()
    {
        using var folder = new TempDirectory();

        var result = await _reader.OpenAsync(folder.FullPath, Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal(GitErrorCodes.NotARepository, result.Error.Code);
    }

    [Fact]
    public async Task Open_BareRepository_ReturnsNotFound()
    {
        using var built = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one").Build();

        var result = await _reader.OpenAsync(built.CreateBareClone(), Ct);

        Assert.Equal(GitErrorCodes.NotARepository, result.Error.Code);
    }

    [Fact]
    public async Task Open_CorruptConfiguration_ReturnsReadFailed()
    {
        using var built = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one").AppendConfig("[core\n\tbroken = = =").Build();

        var result = await _reader.OpenAsync(built.WorkingDirectory, Ct);

        Assert.Equal(ErrorKind.Failure, result.Error.Kind);
        Assert.Equal(GitErrorCodes.ReadFailed, result.Error.Code);
    }

    [Fact]
    public async Task ReadSnapshot_CleanBranch_ReturnsHeadAndBranch()
    {
        using var built = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one").Write("a.txt", "b\n").Commit("two").Build();

        var snapshot = await ReadSnapshotAsync(built.WorkingDirectory);

        Assert.Equal(built.Commits[1], snapshot.HeadCommitSha);
        Assert.Equal(GitRepositoryBuilder.DefaultBranch, snapshot.Branch);
        Assert.False(snapshot.IsDetachedHead);
        Assert.False(snapshot.HasUncommittedChanges);
        Assert.Empty(snapshot.Remotes);
    }

    [Fact]
    public async Task ReadSnapshot_DetachedHead_HasNoBranch()
    {
        using var detached = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one").DetachHead().Build();

        var snapshot = await ReadSnapshotAsync(detached.WorkingDirectory);

        Assert.True(snapshot.IsDetachedHead);
        Assert.Null(snapshot.Branch);
        Assert.Equal(detached.Commits[0], snapshot.HeadCommitSha);
    }

    [Fact]
    public async Task ReadSnapshot_RepositoryWithoutCommits_HasNoHead()
    {
        using var built = GitRepositoryBuilder.Create().Build();

        var snapshot = await ReadSnapshotAsync(built.WorkingDirectory);

        Assert.Null(snapshot.HeadCommitSha);
        Assert.Equal(GitRepositoryBuilder.DefaultBranch, snapshot.Branch);
        Assert.False(snapshot.IsDetachedHead);
    }

    [Theory]
    [InlineData("modified")]
    [InlineData("untracked")]
    [InlineData("deleted")]
    public async Task ReadSnapshot_UncommittedChanges_AreDetected(string change)
    {
        using var built = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one").Build();
        switch (change)
        {
            case "modified": File.WriteAllText(built.PathOf("a.txt"), "changed\n"); break;
            case "untracked": File.WriteAllText(built.PathOf("new.txt"), "new\n"); break;
            default: File.Delete(built.PathOf("a.txt")); break;
        }

        var snapshot = await ReadSnapshotAsync(built.WorkingDirectory);

        Assert.True(snapshot.HasUncommittedChanges);
    }

    [Fact]
    public async Task ReadSnapshot_IgnoredFiles_DoNotCount()
    {
        using var built = GitRepositoryBuilder.Create().Write(".gitignore", "bin/\n").Commit("one").Build();
        Directory.CreateDirectory(built.PathOf("bin"));
        File.WriteAllText(built.PathOf("bin/out.dll"), "x");

        var snapshot = await ReadSnapshotAsync(built.WorkingDirectory);

        Assert.False(snapshot.HasUncommittedChanges);
    }

    [Fact]
    public async Task ReadSnapshot_Remotes_AreSanitizedAndOrdered()
    {
        using var built = GitRepositoryBuilder.Create().Write("a.txt", "a\n").Commit("one")
            .Remote("upstream", "git@github.com:owner/repo.git")
            .Remote("origin", "https://user:ghp_secret@github.com/me/repo.git")
            .Build();

        var snapshot = await ReadSnapshotAsync(built.WorkingDirectory);

        Assert.Equal(
            [new GitRemoteInfo("origin", "https://github.com/me/repo.git"), new GitRemoteInfo("upstream", "github.com:owner/repo.git")],
            snapshot.Remotes);
    }

    [Fact]
    public async Task HostileConfigurationAndHooks_ExecuteNothingAndChangeNothing()
    {
        var marker = ZeroExecutionHarness.MarkerPrefix;
        using var built = GitRepositoryBuilder.Create()
            .Write("a.txt", "a\n").Write(".gitattributes", "*.txt filter=evil diff=evil\n").Commit("one")
            .AppendConfig($"""
                [core]
                	fsmonitor = touch {marker}fsmonitor
                	sshCommand = touch {marker}ssh
                	pager = touch {marker}pager
                [filter "evil"]
                	clean = touch {marker}clean
                	smudge = touch {marker}smudge
                	process = touch {marker}process
                [diff "evil"]
                	textconv = touch {marker}textconv
                [credential]
                	helper = !touch {marker}credential
                [remote "origin"]
                	url = https://github.com/owner/repo.git
                	uploadpack = touch {marker}uploadpack
                """)
            .Hook("fsmonitor-watchman", $"#!/bin/sh\ntouch {marker}fsmonitor-watchman\n")
            .Hook("post-index-change", $"#!/bin/sh\ntouch {marker}post-index-change\n")
            .Hook("reference-transaction", $"#!/bin/sh\ntouch {marker}reference-transaction\n")
            .Build();
        File.WriteAllText(built.PathOf("a.txt"), "dirty\n");
        var before = ZeroExecutionHarness.Snapshot(built.TempRoot);

        var info = (await _reader.OpenAsync(built.WorkingDirectory, Ct)).Value;
        var snapshot = (await _reader.ReadSnapshotAsync(info, Ct)).Value;

        Assert.True(snapshot.HasUncommittedChanges);
        Assert.Empty(ZeroExecutionHarness.Markers(built.TempRoot));
        Assert.Empty(ZeroExecutionHarness.Changes(built.TempRoot, before));
    }

    [Fact]
    public void GitModule_ReferencesNoProcessApi()
    {
        Assert.Empty(ZeroExecutionHarness.ProcessApiUsages(typeof(DependencyInjection).Assembly));
    }

    private async Task<GitSnapshotInfo> ReadSnapshotAsync(string path)
    {
        var info = (await _reader.OpenAsync(path, Ct)).Value;
        return (await _reader.ReadSnapshotAsync(info, Ct)).Value;
    }
}
