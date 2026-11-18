using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Git;
using CodebaseExplorer.TestRepositories;
using CodebaseExplorer.TestRepositories.Git;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace CodebaseExplorer.IntegrationTests.Git;

/// <summary>
/// GIT-003: the LibGit2Sharp cloner against local remotes. The local transport is used only by tests; the product
/// rejects every non-HTTPS URL before it reaches the cloner (<see cref="GitRemoteUrls.ParseCloneUrl"/>).
/// </summary>
public sealed class GitRemoteClonerTests : IDisposable
{
    private readonly TempDirectory _data = new();
    private readonly IGitRemoteCloner _cloner;

    public GitRemoteClonerTests()
    {
        var paths = Substitute.For<IApplicationPaths>();
        paths.ClonesDirectory.Returns(_data.PathOf("clones"));
        _cloner = new ServiceCollection().AddSingleton(paths).AddGitModule().BuildServiceProvider().GetRequiredService<IGitRemoteCloner>();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Target => _data.PathOf("clones/example.com/owner/repo");

    public void Dispose() => _data.Dispose();

    [Fact]
    public async Task Clone_DefaultBranch_ChecksOutFullHistory()
    {
        using var source = Source().Build();

        var result = (await _cloner.CloneAsync(Request(source), null, Ct)).Value;

        Assert.Equal(Target, result.Path);
        Assert.Equal(GitRepositoryBuilder.DefaultBranch, result.Branch);
        Assert.Equal(source.Commits[^1], result.HeadCommitSha);
        Assert.False(result.HasSubmodules);
        Assert.True(File.Exists(Path.Combine(Target, "src", "a.txt")));
        using var clone = new Repository(Target);
        Assert.Equal(source.Commits.Count, clone.Commits.Count());
        Assert.False(clone.Info.IsShallow);
        Assert.False(Directory.Exists(Target + ".partial"));
    }

    [Fact]
    public async Task Clone_Branch_ChecksOutThatBranch()
    {
        using var source = Source().Branch("release", checkout: true).Write("release.txt", "r\n").Commit("release")
            .Checkout(GitRepositoryBuilder.DefaultBranch).Build();

        var result = (await _cloner.CloneAsync(Request(source, branch: "release"), null, Ct)).Value;

        Assert.Equal("release", result.Branch);
        Assert.True(File.Exists(Path.Combine(Target, "release.txt")));
    }

    [Fact]
    public async Task Clone_UnknownBranch_FailsAndLeavesNothing()
    {
        using var source = Source().Build();

        var result = await _cloner.CloneAsync(Request(source, branch: "missing"), null, Ct);

        Assert.Contains(result.Error.Code, new[] { GitErrorCodes.BranchNotFound, GitErrorCodes.CloneFailed });
        AssertNothingLeft();
    }

    [Fact]
    public async Task Clone_UnreachableRemote_FailsAndLeavesNothing()
    {
        var result = await _cloner.CloneAsync(new GitCloneRequest(new Uri(_data.PathOf("missing-remote")), Target, null, null), null, Ct);

        Assert.Equal(GitErrorCodes.CloneFailed, result.Error.Code);
        AssertNothingLeft();
    }

    [Fact]
    public async Task Clone_Cancelled_LeavesNoHalfClonedDirectory()
    {
        using var source = Source().Build();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new CallbackProgress(_ => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _cloner.CloneAsync(Request(source), progress, cts.Token));

        AssertNothingLeft();
    }

    [Fact]
    public async Task Clone_StalePartialDirectory_IsReplaced()
    {
        using var source = Source().Build();
        Directory.CreateDirectory(Target + ".partial");
        File.WriteAllText(Path.Combine(Target + ".partial", "junk.txt"), "x");

        var result = await _cloner.CloneAsync(Request(source), null, Ct);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(Path.Combine(Target, "junk.txt")));
        Assert.False(Directory.Exists(Target + ".partial"));
    }

    [Fact]
    public async Task Clone_ExistingTarget_IsAConflict()
    {
        using var source = Source().Build();
        Directory.CreateDirectory(Target);

        var result = await _cloner.CloneAsync(Request(source), null, Ct);

        Assert.Equal(GitErrorCodes.CloneExists, result.Error.Code);
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("clones")]
    [InlineData("clones/../outside")]
    [InlineData("clones/x.partial")]
    public async Task Clone_TargetOutsideTheClonesDirectory_IsRejected(string relative)
    {
        using var source = Source().Build();

        var result = await _cloner.CloneAsync(new GitCloneRequest(new Uri(source.WorkingDirectory), _data.PathOf(relative), null, null), null, Ct);

        Assert.Equal(GitErrorCodes.ClonePathInvalid, result.Error.Code);
        Assert.Equal(GitErrorCodes.ClonePathInvalid, (await _cloner.DeleteCloneAsync(_data.PathOf(relative), Ct)).Error.Code);
    }

    [Fact]
    public async Task Clone_Submodules_AreReportedButNotCloned()
    {
        using var source = Source()
            .Write(".gitmodules", "[submodule \"lib\"]\n\tpath = lib\n\turl = https://github.com/example/lib.git\n")
            .Commit("declare submodule").Build();

        var result = (await _cloner.CloneAsync(Request(source), null, Ct)).Value;

        Assert.True(result.HasSubmodules);
        Assert.False(Directory.Exists(Path.Combine(Target, "lib")));
    }

    [Fact]
    public async Task Clone_WithToken_NeverWritesTheTokenAndReportsProgress()
    {
        using var source = Source().Build();
        var reports = new List<GitProgress>();

        var result = await _cloner.CloneAsync(Request(source, token: new GitAccessToken("x-access-token", "ghp_supersecret")), new CallbackProgress(reports.Add), Ct);

        Assert.True(result.IsSuccess);
        Assert.Contains(reports, r => r.Stage == GitProgressStage.CheckingOut);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Target, ".git"), "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("ghp_supersecret", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Update_FetchesAndChecksOutTheNewTip()
    {
        using var source = Source().Branch("feature").Build();
        await _cloner.CloneAsync(Request(source), null, Ct);
        var newCommit = CommitInSource(source, "src/a.txt", "updated\n");
        File.WriteAllText(Path.Combine(Target, "src", "a.txt"), "local edit\n");

        var result = (await _cloner.UpdateAsync(Target, null, null, null, Ct)).Value;

        Assert.Equal(newCommit, result.HeadCommitSha);
        Assert.Equal("updated\n", File.ReadAllText(Path.Combine(Target, "src", "a.txt")).ReplaceLineEndings("\n"));

        var feature = (await _cloner.UpdateAsync(Target, "feature", null, null, Ct)).Value;
        Assert.Equal("feature", feature.Branch);
        Assert.Equal(source.Commits[^1], feature.HeadCommitSha);

        Assert.Equal(GitErrorCodes.BranchNotFound, (await _cloner.UpdateAsync(Target, "missing", null, null, Ct)).Error.Code);
    }

    [Fact]
    public async Task Update_MissingClone_IsNotFound()
    {
        var result = await _cloner.UpdateAsync(Target, null, null, null, Ct);

        Assert.Equal(GitErrorCodes.NotARepository, result.Error.Code);
    }

    [Fact]
    public async Task DeleteClone_RemovesEverythingIncludingReadOnlyObjects()
    {
        using var source = Source().Build();
        await _cloner.CloneAsync(Request(source), null, Ct);

        var result = await _cloner.DeleteCloneAsync(Target, Ct);

        Assert.True(result.IsSuccess);
        AssertNothingLeft();
        Assert.True((await _cloner.DeleteCloneAsync(Target, Ct)).IsSuccess);
    }

    private static GitRepositoryBuilder Source() =>
        GitRepositoryBuilder.Create().WriteLines("src/a.txt", 2).Commit("one").WriteLines("src/a.txt", 3).Commit("two");

    private GitCloneRequest Request(GitTestRepository source, string? branch = null, GitAccessToken? token = null) =>
        new(new Uri(source.WorkingDirectory), Target, branch, token);

    private static string CommitInSource(GitTestRepository source, string path, string content)
    {
        File.WriteAllText(source.PathOf(path), content);
        using var repository = new Repository(source.WorkingDirectory);
        Commands.Stage(repository, "*");
        var signature = new Signature("Ada", "ada@example.com", GitRepositoryBuilder.DefaultStart.AddDays(1));
        return repository.Commit("update", signature, signature).Sha;
    }

    private void AssertNothingLeft()
    {
        Assert.False(Directory.Exists(Target), "The target must not exist.");
        Assert.False(Directory.Exists(Target + ".partial"), "No partial clone may remain.");
    }

    private sealed class CallbackProgress(Action<GitProgress> callback) : IProgress<GitProgress>
    {
        public void Report(GitProgress value) => callback(value);
    }
}
