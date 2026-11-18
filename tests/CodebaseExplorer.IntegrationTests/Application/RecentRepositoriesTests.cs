using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>
/// REPO-005: list, reopen (without analysis) and remove stored repositories, through the real composition
/// root and SQLite store.
/// </summary>
public sealed class RecentRepositoriesTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private T Get<T>()
        where T : notnull => _app.Services.GetRequiredService<T>();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task NoRepositories_EmptyList()
    {
        Assert.Empty(await Get<ListRecentRepositories>().ExecuteAsync(Token));
    }

    [Fact]
    public async Task ImportedRepository_IsListedWithItsLastRun()
    {
        var imported = await ImportFixtureAsync();

        var entry = Assert.Single(await Get<ListRecentRepositories>().ExecuteAsync(Token));

        Assert.Equal(imported.RepositoryId, entry.RepositoryId);
        Assert.Equal("dotnet-layered", entry.Name);
        Assert.Equal(imported.RootPath, entry.RootPath);
        Assert.True(entry.HasModel);
        Assert.Equal(RunStatuses.Succeeded, entry.LastRunStatus);
        Assert.NotNull(entry.LastRunAt);
        Assert.NotNull(entry.LastOpenedAt);
    }

    [Fact]
    public async Task FailedFirstImport_IsListedWithoutModel_AndCannotBeOpened()
    {
        using var folder = new TempDirectory();
        var analyzer = Substitute.For<IRepositoryAnalyzer>();
        analyzer.AnalyzeAsync(default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(
            Task.FromResult<Result<RepositoryAnalysis>>(ApplicationError.Failure("analysis.failed", "boom")));
        var import = new ImportLocalRepository(Get<IRepositoryRootValidator>(), analyzer, Get<IArchitectureModelStore>(), Get<IGitRepositoryReader>(), Get<IGitActivityStore>(), TimeProvider.System);
        await import.ExecuteAsync(folder.FullPath, null, Token);

        var entry = Assert.Single(await Get<ListRecentRepositories>().ExecuteAsync(Token));
        Assert.False(entry.HasModel);
        Assert.Equal(RunStatuses.Failed, entry.LastRunStatus);

        var opened = await Get<OpenRepository>().ExecuteAsync(entry.RepositoryId, Token);
        Assert.Equal(RepositoryErrorCodes.NotAnalyzed, opened.Error.Code);
    }

    [Fact]
    public async Task List_IsOrderedByLastUse()
    {
        using var other = new TempDirectory();
        other.WriteText("App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var first = await ImportFixtureAsync();
        var second = (await Get<ImportLocalRepository>().ExecuteAsync(other.FullPath, null, Token)).Value;

        Assert.Equal([second.RepositoryId, first.RepositoryId], (await Get<ListRecentRepositories>().ExecuteAsync(Token)).Select(r => r.RepositoryId));

        await Task.Delay(20, Token); // timestamps must differ
        await Get<OpenRepository>().ExecuteAsync(first.RepositoryId, Token);

        Assert.Equal([first.RepositoryId, second.RepositoryId], (await Get<ListRecentRepositories>().ExecuteAsync(Token)).Select(r => r.RepositoryId));
    }

    [Fact]
    public async Task Open_ReturnsTheStoredModelsRun_AndTheGraphLoadsWithoutAnalysis()
    {
        var imported = await ImportFixtureAsync();

        // A second application instance (restart) has nothing in memory: the graph comes from SQLite.
        await using var restarted = _app.Restart();
        var opened = (await restarted.Services.GetRequiredService<OpenRepository>().ExecuteAsync(imported.RepositoryId, Token)).Value;
        var graph = await restarted.Services.GetRequiredService<GetGraphView>().ExecuteAsync(new GraphQuery { RepositoryId = opened.RepositoryId }, Token);

        Assert.Equal(imported.AnalysisRunId, opened.AnalysisRunId);
        Assert.Equal(imported.Name, opened.Name);
        Assert.Equal(5, graph.Value.Nodes.Count(n => n.Kind == GraphNodeKinds.Project)); // Shop.Tests is hidden by default (GRAPH-007)
    }

    [Theory]
    [InlineData("not-an-id", RepositoryErrorCodes.IdInvalid)]
    [InlineData("0123456789abcdef0123456789abcdef", RepositoryErrorCodes.NotFound)]
    public async Task OpenAndRemove_RejectInvalidOrUnknownIds(string repositoryId, string code)
    {
        Assert.Equal(code, (await Get<OpenRepository>().ExecuteAsync(repositoryId, Token)).Error.Code);
        Assert.Equal(code, (await Get<RemoveRepository>().ExecuteAsync(repositoryId, Token)).Error.Code);
    }

    [Fact]
    public async Task Remove_DeletesAllDataOfTheRepository()
    {
        var imported = await ImportFixtureAsync();

        var removed = await Get<RemoveRepository>().ExecuteAsync(imported.RepositoryId, Token);

        Assert.True(removed.IsSuccess);
        Assert.Empty(await Get<ListRecentRepositories>().ExecuteAsync(Token));
        var graph = await Get<GetGraphView>().ExecuteAsync(new GraphQuery { RepositoryId = imported.RepositoryId }, Token);
        Assert.Equal(RepositoryErrorCodes.NotAnalyzed, graph.Error.Code);
        Assert.True(Directory.Exists(imported.RootPath), "the analyzed folder must not be touched");
    }

    private async Task<RepositoryImportSummaryDto> ImportFixtureAsync() =>
        (await Get<ImportLocalRepository>().ExecuteAsync(TestRepositoryCatalog.GetFixturePath("dotnet-layered"), null, Token)).Value;
}
