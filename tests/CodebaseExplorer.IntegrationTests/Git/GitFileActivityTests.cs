using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Git;
using CodebaseExplorer.TestRepositories.Git;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Git;

/// <summary>GIT-004: per-file activity statistics on builder-created histories.</summary>
public sealed class GitFileActivityTests
{
    private static readonly DateTimeOffset T0 = GitRepositoryBuilder.DefaultStart;

    private static readonly GitHistoryWindow Unbounded = new(GitHistoryWindow.DefaultMaxCommits, since: null);

    private readonly IGitRepositoryReader _reader = new ServiceCollection().AddGitModule().BuildServiceProvider()
        .GetRequiredService<IGitRepositoryReader>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Walk_CountsCommitsAuthorsLinesAndTimesPerFile()
    {
        using var built = GitRepositoryBuilder.Create()
            .WriteLines("src/a.txt", 3).WriteLines("b.txt", 2).Commit("add", GitTestAuthor.Ada)
            .Write("src/a.txt", "line 1\nline 2\nchanged\nline 4\n").Commit("change a", GitTestAuthor.Grace)
            .Delete("b.txt").Commit("delete b", GitTestAuthor.Ada)
            .Rename("src/a.txt", "src/c.txt").Commit("rename a", GitTestAuthor.Linus)
            .Build();

        var report = await WalkAsync(built.WorkingDirectory, Unbounded);

        Assert.Equal(4, report.CommitsScanned);
        Assert.Equal(T0, report.OldestCommitAt);
        Assert.False(report.ReachedCommitLimit);
        Assert.Equal(
            [
                new GitFileActivity("b.txt", 2, 1, 2, 2, T0, T0.AddHours(2), built.Commits[2]),
                new GitFileActivity("src/a.txt", 3, 3, 5, 5, T0, T0.AddHours(3), built.Commits[3]),
                new GitFileActivity("src/c.txt", 1, 1, 4, 0, T0.AddHours(3), T0.AddHours(3), built.Commits[3]),
            ],
            report.Files);
    }

    [Fact]
    public async Task Walk_SkipsMergeCommitsButCountsTheMergedWork()
    {
        using var built = GitRepositoryBuilder.Create()
            .WriteLines("a.txt", 1).Commit("base", GitTestAuthor.Ada)
            .Branch("feature", checkout: true).WriteLines("f.txt", 2).Commit("feature", GitTestAuthor.Grace)
            .Checkout(GitRepositoryBuilder.DefaultBranch).WriteLines("m.txt", 3).Commit("main", GitTestAuthor.Ada)
            .Merge("feature", GitTestAuthor.Linus)
            .Build();

        var report = await WalkAsync(built.WorkingDirectory, Unbounded);

        Assert.Equal(3, report.CommitsScanned);
        Assert.Equal(["a.txt", "f.txt", "m.txt"], report.Files.Select(f => f.Path));
        var feature = Assert.Single(report.Files, f => f.Path == "f.txt");
        Assert.Equal((1, 1, 2L), (feature.CommitCount, feature.DistinctAuthorCount, feature.LinesAdded));
    }

    [Fact]
    public async Task Walk_StopsAtTheCommitLimit()
    {
        using var built = GitRepositoryBuilder.Create()
            .WriteLines("a.txt", 1).Commit("one").WriteLines("a.txt", 2).Commit("two").WriteLines("a.txt", 3).Commit("three")
            .Build();

        var report = await WalkAsync(built.WorkingDirectory, new GitHistoryWindow(2, since: null));

        Assert.True(report.ReachedCommitLimit);
        Assert.Equal(2, report.CommitsScanned);
        Assert.Equal(T0.AddHours(1), report.OldestCommitAt);
        var file = Assert.Single(report.Files);
        Assert.Equal((2, 2L, 0L), (file.CommitCount, file.LinesAdded, file.LinesDeleted));
    }

    [Fact]
    public async Task Walk_IgnoresCommitsBeforeTheWindowStart()
    {
        using var built = GitRepositoryBuilder.Create()
            .WriteLines("old.txt", 1).Commit("old", at: T0)
            .WriteLines("new.txt", 1).Commit("new", at: T0.AddDays(400))
            .Build();

        var report = await WalkAsync(built.WorkingDirectory, GitHistoryWindow.Default(T0.AddDays(401)));

        Assert.Equal(["new.txt"], report.Files.Select(f => f.Path));
        Assert.False(report.ReachedCommitLimit);
    }

    [Fact]
    public async Task Walk_FromSubfolder_ReportsOnlyItsFilesRelativeToIt()
    {
        using var built = GitRepositoryBuilder.Create()
            .WriteLines("src/app/a.txt", 2).WriteLines("src/application.txt", 1).WriteLines("root.txt", 1).Commit("one")
            .Build();

        var report = await WalkAsync(built.PathOf("src/app"), Unbounded);

        Assert.Equal(["a.txt"], report.Files.Select(f => f.Path));
        Assert.Equal(1, report.CommitsScanned);
    }

    [Fact]
    public async Task Walk_CountsAuthorsByCaseInsensitiveEmail()
    {
        using var built = GitRepositoryBuilder.Create()
            .WriteLines("a.txt", 1).Commit("one", new GitTestAuthor("Ada", "Ada@Example.com"))
            .WriteLines("a.txt", 2).Commit("two", new GitTestAuthor("Ada L.", "ada@example.com"))
            .WriteLines("a.txt", 3).Commit("three", new GitTestAuthor("Grace", "grace@example.com"))
            .Build();

        var report = await WalkAsync(built.WorkingDirectory, Unbounded);

        Assert.Equal(2, Assert.Single(report.Files).DistinctAuthorCount);
    }

    [Fact]
    public async Task Walk_BinaryFiles_CountCommitsButNoLines()
    {
        var builder = GitRepositoryBuilder.Create();
        File.WriteAllBytes(Path.Combine(builder.WorkingDirectory, "logo.bin"), [0, 1, 2, 0, 255, 0]);
        using var built = builder.Commit("binary").Build();

        var report = await WalkAsync(built.WorkingDirectory, Unbounded);

        var file = Assert.Single(report.Files);
        Assert.Equal((1, 0L, 0L), (file.CommitCount, file.LinesAdded, file.LinesDeleted));
    }

    [Fact]
    public async Task Walk_RepositoryWithoutCommits_IsEmpty()
    {
        using var built = GitRepositoryBuilder.Create().Build();

        var report = await WalkAsync(built.WorkingDirectory, Unbounded);

        Assert.Empty(report.Files);
        Assert.Equal(0, report.CommitsScanned);
        Assert.Null(report.OldestCommitAt);
    }

    [Fact]
    public async Task Walk_ManyCommits_IsDeterministicAndReportsProgress()
    {
        var builder = GitRepositoryBuilder.Create();
        var authors = new[] { GitTestAuthor.Ada, GitTestAuthor.Grace, GitTestAuthor.Linus };
        for (var i = 1; i <= 60; i++)
        {
            builder.WriteLines($"file{i % 7}.txt", i).Commit($"commit {i}", authors[i % 3]);
        }

        using var built = builder.Build();
        var progress = new List<GitProgress>();

        var first = await WalkAsync(built.WorkingDirectory, Unbounded, new SynchronousProgress(progress));
        var second = await WalkAsync(built.WorkingDirectory, Unbounded);

        Assert.Equal(first.Files, second.Files);
        Assert.Equal(60, first.CommitsScanned);
        Assert.Equal(60, first.Files.Sum(f => f.CommitCount));
        // Each file grows to its largest line count (no deletions in this history).
        Assert.Equal(Enumerable.Range(0, 7).Sum(r => Enumerable.Range(1, 60).Where(i => i % 7 == r).Max()), first.Files.Sum(f => f.LinesAdded));
        Assert.Equal(new GitProgress(GitProgressStage.ReadingHistory, 60, 60), progress[^1]);
    }

    [Fact]
    public async Task Walk_Cancelled_Throws()
    {
        using var built = GitRepositoryBuilder.Create().WriteLines("a.txt", 1).Commit("one").Build();
        var info = (await _reader.OpenAsync(built.WorkingDirectory, Ct)).Value;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _reader.ReadFileActivityAsync(info, Unbounded, null, new CancellationToken(canceled: true)));
    }

    /// <summary>
    /// Benchmark on a real repository (TEST_STRATEGY.md §8): set CODEBASE_EXPLORER_BENCH_GIT_REPO to a local clone and run
    /// with <c>--explicit only</c>. Replaced by the performance fixture of QA-002 once it exists.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Walk_Benchmark_OnRepositoryFromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("CODEBASE_EXPLORER_BENCH_GIT_REPO");
        if (string.IsNullOrEmpty(path))
        {
            Assert.Skip("CODEBASE_EXPLORER_BENCH_GIT_REPO is not set.");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var report = await WalkAsync(path, Unbounded);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{report.CommitsScanned} commits, {report.Files.Count} files, {report.Files.Sum(f => f.LinesAdded)} lines added in {stopwatch.ElapsedMilliseconds} ms ({Environment.ProcessorCount} logical CPUs)");
    }

    private async Task<GitFileActivityReport> WalkAsync(string path, GitHistoryWindow window, IProgress<GitProgress>? progress = null)
    {
        var info = (await _reader.OpenAsync(path, Ct)).Value;
        var result = await _reader.ReadFileActivityAsync(info, window, progress, Ct);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private sealed class SynchronousProgress(List<GitProgress> reports) : IProgress<GitProgress>
    {
        public void Report(GitProgress value)
        {
            lock (reports)
            {
                reports.Add(value);
            }
        }
    }
}
