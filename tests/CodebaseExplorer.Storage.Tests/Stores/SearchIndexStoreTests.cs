using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Storage.Stores;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Tests.Stores;

/// <summary>
/// AI-003: FTS5 is available in the SQLite bundled with the EF Core provider, and the search index stores, ranks,
/// replaces and deletes chunks per repository.
/// </summary>
public sealed class SearchIndexStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _database = new();
    private readonly DomainSamples _samples = new();
    private readonly SearchIndexStore _store;

    public SearchIndexStoreTests() => _store = new SearchIndexStore(new TestContextFactory(_database));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private RepositoryId Repository => _samples.Repository.Id;

    private ElementId File(int index) => _samples.Files[index].Id;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task BundledSqlite_HasFts5()
    {
        Assert.Equal(1L, await _database.ScalarAsync("SELECT sqlite_compileoption_used('ENABLE_FTS5');"));
        Assert.Equal("table", await _database.ScalarAsync("SELECT type FROM sqlite_master WHERE name = 'SearchChunks';"));
    }

    [Fact]
    public async Task Search_FindsTheBestMatchingChunks_OfTheRepositoryOnly()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);
        var run = _samples.Run.Id;
        await _store.ReplaceAsync(Repository, run,
        [
            new SearchChunk(File(0), 1, 40, "public class InvoiceService { decimal Total(); }", "invoice service"),
            new SearchChunk(File(0), 31, 70, "// unrelated helper code", string.Empty),
            new SearchChunk(File(1), 1, 12, "invoice invoice invoice totals", string.Empty),
        ], Now, Token);

        var hits = await _store.SearchAsync(Repository, ["invoice", "service"], 10, Token);
        var other = await _store.SearchAsync(new RepositoryId(Guid.NewGuid()), ["invoice"], 10, Token);

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.FileId == File(0) && h.StartLine == 1 && h.EndLine == 40 && h.Text.Contains("InvoiceService", StringComparison.Ordinal));
        Assert.True(hits[0].Rank <= hits[1].Rank);
        Assert.Empty(other);
        Assert.Equal(new SearchIndexState(run, 3, Now), await _store.GetStateAsync(Repository, Token));
    }

    [Fact]
    public async Task PrefixAndKeywords_Match()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);
        await _store.ReplaceAsync(Repository, _samples.Run.Id,
            [new SearchChunk(File(0), 1, 2, "class OrderRepository {}", "order repository")], Now, Token);

        Assert.Single(await _store.SearchAsync(Repository, ["reposit"], 5, Token)); // prefix of the keyword "repository"
        Assert.Single(await _store.SearchAsync(Repository, ["order"], 5, Token));
    }

    [Fact]
    public async Task Replace_RemovesTheChunksOfThePreviousRun()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);
        await _store.ReplaceAsync(Repository, _samples.Run.Id, [new SearchChunk(File(0), 1, 2, "alpha", string.Empty)], Now, Token);
        var next = AnalysisRunId.New(Now);

        await _store.ReplaceAsync(Repository, next, [new SearchChunk(File(0), 1, 2, "beta", string.Empty)], Now.AddHours(1), Token);

        Assert.Empty(await _store.SearchAsync(Repository, ["alpha"], 5, Token));
        Assert.Single(await _store.SearchAsync(Repository, ["beta"], 5, Token));
        Assert.Equal(next, (await _store.GetStateAsync(Repository, Token))!.RunId);
    }

    [Fact]
    public async Task DeletingTheRepository_DeletesItsChunksAndState()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);
        await _store.ReplaceAsync(Repository, _samples.Run.Id, [new SearchChunk(File(0), 1, 2, "alpha", string.Empty)], Now, Token);

        await using (var context = _database.CreateContext())
        {
            await context.Repositories.ExecuteDeleteAsync(Token);
        }

        Assert.Equal(0L, await _database.ScalarAsync("SELECT count(*) FROM SearchChunks;"));
        Assert.Null(await _store.GetStateAsync(Repository, Token));
    }

    [Theory]
    [InlineData(new[] { "invoice", "to" }, "\"invoice\"* OR \"to\"")]
    [InlineData(new[] { "a\" OR x", "NEAR(", "ok" }, "\"ok\"")]
    [InlineData(new string[0], "")]
    public void MatchExpression_QuotesTerms_AndDropsAnythingElse(string[] terms, string expected) =>
        Assert.Equal(expected, SearchIndexStore.MatchExpression(terms));

    [Fact]
    public async Task NoUsableTerms_FindNothing()
    {
        Assert.Empty(await _store.SearchAsync(Repository, ["*"], 5, Token));
    }
}
