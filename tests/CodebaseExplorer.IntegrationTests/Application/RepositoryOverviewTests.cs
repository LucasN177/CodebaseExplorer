using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>
/// UI-004: the overview of the <c>dotnet-layered</c> fixture matches its oracle (EXPECTED.md) and the files
/// on disk; the ViewModel shows it for the open repository.
/// </summary>
public sealed class RepositoryOverviewTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string FixturePath => TestRepositoryCatalog.GetFixturePath("dotnet-layered");

    private T Get<T>()
        where T : notnull => _app.Services.GetRequiredService<T>();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Overview_MatchesTheFixtureExpectations()
    {
        var imported = await ImportFixtureAsync();

        var overview = (await Get<GetRepositoryOverview>().ExecuteAsync(imported.RepositoryId, Token)).Value;

        Assert.Equal("dotnet-layered", overview.Name);
        Assert.Equal(imported.RootPath, overview.RootPath);

        // Every file of the fixture is part of the model; C# files and their lines are counted from disk.
        var files = Directory.GetFiles(FixturePath, "*", SearchOption.AllDirectories);
        Assert.Equal(files.Length, overview.FileCount);
        var csharpFiles = files.Where(f => f.EndsWith(".cs", StringComparison.Ordinal)).ToList();
        var csharp = Assert.Single(overview.Languages, l => l.Language == "csharp");
        Assert.Equal(csharpFiles.Count, csharp.FileCount);
        Assert.Equal(csharpFiles.Sum(f => (long)File.ReadAllLines(f).Length), csharp.LineCount);
        Assert.Same(csharp, overview.Languages[0]); // most files first
        Assert.Equal(overview.FileCount, overview.Languages.Sum(l => l.FileCount));

        // EXPECTED.md §3–§7: 5 real .NET projects plus the Shop.Legacy placeholder, 5 external dependencies
        // (4 NuGet packages, 1 framework), 1 warning, no errors; relationships = 13 manifest + C# code relationships.
        Assert.Equal([new EcosystemStatisticsDto("dotnet", 5)], overview.ProjectsByEcosystem);
        Assert.Equal(1, overview.MissingProjectCount);
        Assert.Equal(5, overview.ExternalDependencyCount);
        Assert.Equal(imported.RelationshipCount, overview.RelationshipCount);
        Assert.Equal((1, 0), (overview.WarningCount, overview.ErrorCount));

        Assert.Equal(imported.AnalysisRunId, overview.ModelRun.RunId);
        Assert.Equal(RunStatuses.Succeeded, overview.ModelRun.Status);
        Assert.NotNull(overview.ModelRun.Duration);
        Assert.Null(overview.NewerRun);
    }

    [Fact]
    public async Task FailedReanalysis_IsShownAsNewerRun_AndTheStoredResultStays()
    {
        var imported = await ImportFixtureAsync();
        var analyzer = Substitute.For<IRepositoryAnalyzer>();
        analyzer.AnalyzeAsync(default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(
            Task.FromResult<Result<RepositoryAnalysis>>(ApplicationError.Failure("analysis.failed", "boom")));
        await new ImportLocalRepository(Get<IRepositoryRootValidator>(), analyzer, Get<IArchitectureModelStore>(), Get<IGitRepositoryReader>(), Get<IGitActivityStore>(), TimeProvider.System)
            .ExecuteAsync(FixturePath, null, Token);

        var overview = (await Get<GetRepositoryOverview>().ExecuteAsync(imported.RepositoryId, Token)).Value;

        Assert.Equal(imported.AnalysisRunId, overview.ModelRun.RunId);
        Assert.Equal(RunStatuses.Failed, overview.NewerRun?.Status);
        Assert.Equal(imported.RelationshipCount, overview.RelationshipCount);
    }

    [Theory]
    [InlineData("x", RepositoryErrorCodes.IdInvalid)]
    [InlineData("0123456789abcdef0123456789abcdef", RepositoryErrorCodes.NotAnalyzed)]
    public async Task InvalidOrUnknownRepository_IsAnError(string repositoryId, string code)
    {
        Assert.Equal(code, (await Get<GetRepositoryOverview>().ExecuteAsync(repositoryId, Token)).Error.Code);
    }

    [Fact]
    public async Task Page_ShowsTheOverviewOfTheOpenRepository_AndRequestsTheExplorer()
    {
        var imported = await ImportFixtureAsync();
        var session = new RepositorySession();
        var page = new OverviewPageViewModel(Get<GetRepositoryOverview>(), session, NullLogger<OverviewPageViewModel>.Instance);
        await page.ActivateAsync();
        Assert.True(page.ShowEmptyState);

        session.SetCurrent(new OpenRepositoryDto
        {
            RepositoryId = imported.RepositoryId,
            Name = imported.Name,
            RootPath = imported.RootPath,
            AnalysisRunId = imported.AnalysisRunId,
        });
        await page.LoadAsync();

        Assert.True(page.HasOverview);
        Assert.Equal("dotnet-layered", page.Name);
        Assert.Contains(page.Tiles, t => t is { Label: "Projects", Value: "5" });
        Assert.Contains(page.Tiles, t => t is { Label: "Warnings", Value: "1" });
        Assert.StartsWith("C# · ", page.Languages[0], StringComparison.Ordinal);
        Assert.Equal([".NET · 5 projects"], page.Ecosystems);
        Assert.False(page.HasNoProjects);
        Assert.NotNull(page.MissingProjectsText);
        Assert.StartsWith("Succeeded · ", page.ModelRunText, StringComparison.Ordinal);
        Assert.Null(page.NewerRunText);

        var requested = false;
        page.ExplorerRequested += (_, _) => requested = true;
        page.OpenExplorerCommand.Execute(null);
        Assert.True(requested);
    }

    private async Task<RepositoryImportSummaryDto> ImportFixtureAsync() =>
        (await Get<ImportLocalRepository>().ExecuteAsync(FixturePath, null, Token)).Value;
}
