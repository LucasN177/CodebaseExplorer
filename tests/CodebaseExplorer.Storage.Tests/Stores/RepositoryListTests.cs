using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.Storage.Stores;

namespace CodebaseExplorer.Storage.Tests.Stores;

/// <summary>REPO-005: listing, marking as opened and deleting repositories in the model store.</summary>
public sealed class RepositoryListTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly DomainSamples _samples = new();
    private readonly ArchitectureModelStore _store;

    public RepositoryListTests() => _store = new ArchitectureModelStore(new TestContextFactory(_database));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _store.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task List_ContainsTheRepositoryWithItsLatestRun()
    {
        await _store.SaveRunAsync(_samples.Repository, _samples.Run, _samples.Model, Token);
        var cancelled = AnalysisRun.Create(_samples.Repository.Id, AnalysisTrigger.Manual, _samples.Run.StartedAt.AddHours(1));
        cancelled.Start();
        cancelled.Cancel(cancelled.StartedAt.AddSeconds(3));
        await _store.SaveRunAsync(_samples.Repository, cancelled, model: null, Token);

        var entry = Assert.Single(await _store.ListRepositoriesAsync(Token));

        Assert.Equal(_samples.Repository.Id, entry.Repository.Id);
        Assert.Equal(_samples.Run.Id, entry.Repository.LastAnalysisRunId);
        Assert.Equal(AnalysisRunStatus.Cancelled, entry.LastRunStatus);
        Assert.Equal(cancelled.CompletedAt, entry.LastRunAt);
        Assert.Null(entry.LastOpenedAt);
    }

    [Fact]
    public async Task MarkOpened_SetsTheTimestamp_AndIgnoresUnknownRepositories()
    {
        await _store.SaveRunAsync(_samples.Repository, _samples.Run, _samples.Model, Token);
        var openedAt = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

        await _store.MarkOpenedAsync(_samples.Repository.Id, openedAt, Token);
        await _store.MarkOpenedAsync(RepositoryId.New(), openedAt, Token);

        Assert.Equal(openedAt, Assert.Single(await _store.ListRepositoriesAsync(Token)).LastOpenedAt);
    }

    [Fact]
    public async Task Delete_RemovesEveryRowOfTheRepository_AndTheCachedModel()
    {
        await _store.SaveRunAsync(_samples.Repository, _samples.Run, _samples.Model, Token);
        Assert.NotNull(await _store.LoadCurrentModelAsync(_samples.Repository.Id, Token)); // now cached

        Assert.True(await _store.DeleteRepositoryAsync(_samples.Repository.Id, Token));

        foreach (var table in new[] { "Repositories", "AnalysisRuns", "AnalysisDiagnostics", "Files", "Projects", "Modules",
            "CodeEntities", "CodeEntityDeclarations", "ExternalDependencies", "CodeRelationships", "AnalysisEvidence" })
        {
            Assert.Equal(0L, await _database.ScalarAsync($"SELECT count(*) FROM {table}"));
        }

        Assert.Null(await _store.LoadCurrentModelAsync(_samples.Repository.Id, Token));
        Assert.Empty(await _store.ListRepositoriesAsync(Token));
    }

    [Fact]
    public async Task ListRuns_ReturnsAllRunsNewestFirst_WithDiagnostics()
    {
        // UI-005
        await _store.SaveRunAsync(_samples.Repository, _samples.Run, _samples.Model, Token);
        var failed = AnalysisRun.Create(_samples.Repository.Id, AnalysisTrigger.Manual, _samples.Run.StartedAt.AddHours(1));
        failed.Start();
        failed.Fail(failed.StartedAt.AddSeconds(2), [new AnalysisDiagnostic(DiagnosticSeverity.Error, "analysis.failed", "boom")]);
        await _store.SaveRunAsync(_samples.Repository, failed, model: null, Token);

        var runs = await _store.ListRunsAsync(_samples.Repository.Id, Token);

        Assert.Equal([failed.Id, _samples.Run.Id], runs.Select(r => r.Id));
        Assert.Equal("analysis.failed", Assert.Single(runs[0].Diagnostics).Code);
        Assert.Equal(_samples.Run.Diagnostics, runs[1].Diagnostics);
        Assert.Empty(await _store.ListRunsAsync(RepositoryId.New(), Token));
    }

    [Fact]
    public async Task Delete_UnknownRepository_ReturnsFalse()
    {
        Assert.False(await _store.DeleteRepositoryAsync(RepositoryId.New(), Token));
    }
}
