using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.IntegrationTests.Security;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>
/// AI-003: the search index is built from the analyzed files on first use, skips secret and changed files, and is
/// rebuilt only for a new run (real composition root, SQLite FTS5).
/// </summary>
public sealed class EnsureSearchIndexTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly TempDirectory _repository = ZeroExecutionHarness.CopyFixture("dotnet-layered");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _repository.Dispose();
    }

    [Fact]
    public async Task Index_FindsCodeByIdentifierParts()
    {
        var stored = await ImportAsync();

        var state = (await _app.Services.GetRequiredService<EnsureSearchIndex>().ExecuteAsync(stored, Token)).Value;
        var hits = await _app.Services.GetRequiredService<ISearchIndexStore>().SearchAsync(
            stored.Repository.Id, SearchTerms.ForQuery("Where are orders placed?"), 5, Token);

        Assert.Equal(stored.Model.AnalysisRunId, state.RunId);
        Assert.True(state.ChunkCount > 5, state.ChunkCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var paths = hits.Select(h => stored.Model.FindFile(h.FileId)!.Path.Value).ToList();
        Assert.Contains("src/Shop.Application/Orders/PlaceOrderHandler.cs", paths);
    }

    [Fact]
    public async Task SecretFiles_AndFilesChangedSinceTheAnalysis_AreNotIndexed()
    {
        _repository.WriteText(".env", "API_KEY=supersecretvalue");
        _repository.WriteText("src/Shop.Web/appsettings.Production.json", """{ "ConnectionStrings": { "Db": "Password=supersecretvalue" } }""");
        var stored = await ImportAsync();
        _repository.WriteText("src/Shop.Domain/Orders/Order.cs", "// edited after the analysis: changedafteranalysis");

        await _app.Services.GetRequiredService<EnsureSearchIndex>().ExecuteAsync(stored, Token);
        var search = _app.Services.GetRequiredService<ISearchIndexStore>();

        Assert.Empty(await search.SearchAsync(stored.Repository.Id, ["supersecretvalue"], 5, Token));
        Assert.Empty(await search.SearchAsync(stored.Repository.Id, ["changedafteranalysis"], 5, Token));
        Assert.Contains(await search.SearchAsync(stored.Repository.Id, ["order"], 20, Token),
            h => stored.Model.FindFile(h.FileId)!.Path.Value == "src/Shop.Domain/Orders/OrderLine.cs");
    }

    [Fact]
    public async Task SameRun_IsNotIndexedAgain_ANewRunIs()
    {
        var stored = await ImportAsync();
        var ensure = _app.Services.GetRequiredService<EnsureSearchIndex>();
        var first = (await ensure.ExecuteAsync(stored, Token)).Value;

        var again = (await ensure.ExecuteAsync(stored, Token)).Value;
        var reanalyzed = await ImportAsync();
        var rebuilt = (await ensure.ExecuteAsync(reanalyzed, Token)).Value;

        Assert.Equal(first, again);
        Assert.NotEqual(first.RunId, rebuilt.RunId);
        Assert.Equal(reanalyzed.Model.AnalysisRunId, rebuilt.RunId);
    }

    private async Task<StoredRepositoryModel> ImportAsync()
    {
        var summary = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(_repository.FullPath, null, Token)).Value;
        var id = new CodebaseExplorer.Domain.Common.RepositoryId(Guid.ParseExact(summary.RepositoryId, "N"));
        return (await _app.Services.GetRequiredService<IArchitectureModelStore>().LoadCurrentModelAsync(id, Token))!;
    }
}
