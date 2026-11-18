using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>
/// UI-005: run history with diagnostics through the real SQLite store; partially succeeded runs show their
/// errors with analyzer and location. The analyzer is substituted to produce each run outcome.
/// </summary>
public sealed class RunHistoryTests : IAsyncLifetime
{
    private const string Analyzer = "test.analyzer";

    private readonly TempDirectory _folder = new();
    private readonly TestApplication _app = new();
    private readonly IRepositoryAnalyzer _analyzer = Substitute.For<IRepositoryAnalyzer>();
    private readonly ImportLocalRepository _import;

    public RunHistoryTests() =>
        _import = new ImportLocalRepository(Get<IRepositoryRootValidator>(), _analyzer, Get<IArchitectureModelStore>(), Get<IGitRepositoryReader>(), Get<IGitActivityStore>(), TimeProvider.System);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private T Get<T>()
        where T : notnull => _app.Services.GetRequiredService<T>();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public async Task PartiallySucceededRun_ShowsItsErrorsWithAnalyzerAndLocation()
    {
        var imported = await ImportPartialAsync();

        var run = Assert.Single((await Get<GetRunHistory>().ExecuteAsync(imported.RepositoryId, Token)).Value);

        Assert.Equal(RunStatuses.PartiallySucceeded, run.Status);
        Assert.Equal("initial", run.Trigger);
        Assert.True(run.IsCurrent);
        Assert.NotNull(run.Duration);
        Assert.Null(run.CommitSha);
        Assert.Equal((1, 1, 0), (run.ErrorCount, run.WarningCount, run.InfoCount));
        var error = Assert.Single(run.Diagnostics, d => d.Severity == "error");
        Assert.Equal(new DiagnosticDto("error", "test.project-failed", "Project could not be read.", "src/App/App.csproj", 7, Analyzer), error);
    }

    [Fact]
    public async Task History_ListsEveryOutcomeNewestFirst()
    {
        var imported = await ImportPartialAsync();
        _analyzer.AnalyzeAsync(default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(
            Task.FromResult<Result<RepositoryAnalysis>>(ApplicationError.Failure("analysis.failed", "The analysis failed.")));
        await _import.ExecuteAsync(_folder.FullPath, null, Token);

        var runs = (await Get<GetRunHistory>().ExecuteAsync(imported.RepositoryId, Token)).Value;

        Assert.Equal([RunStatuses.Failed, RunStatuses.PartiallySucceeded], runs.Select(r => r.Status));
        Assert.Equal([false, true], runs.Select(r => r.IsCurrent));
        Assert.Equal("manual", runs[0].Trigger);
        Assert.Equal("analysis.failed", Assert.Single(runs[0].Diagnostics).Code);
    }

    [Theory]
    [InlineData("nope", RepositoryErrorCodes.IdInvalid)]
    [InlineData("0123456789abcdef0123456789abcdef", RepositoryErrorCodes.NotFound)]
    public async Task InvalidOrUnknownRepository_IsAnError(string repositoryId, string code)
    {
        Assert.Equal(code, (await Get<GetRunHistory>().ExecuteAsync(repositoryId, Token)).Error.Code);
    }

    [Fact]
    public async Task Page_ShowsTheRunsOfTheOpenRepository_AndFiltersBySeverity()
    {
        var imported = await ImportPartialAsync();
        var session = new RepositorySession();
        var page = new RunHistoryPageViewModel(Get<GetRunHistory>(), session, NullLogger<RunHistoryPageViewModel>.Instance);
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

        Assert.Equal(imported.Name, page.RepositoryName);
        var run = Assert.Single(page.Runs);
        Assert.Same(run, page.SelectedRun);
        Assert.StartsWith("Partially succeeded · ", run.Title, StringComparison.Ordinal);
        Assert.Contains("1 error, 1 warning", run.Summary, StringComparison.Ordinal);
        Assert.Equal(2, page.Diagnostics.Count);

        page.SelectedFilter = DiagnosticFilter.Errors;
        var error = Assert.Single(page.Diagnostics);
        Assert.Equal("Error", error.SeverityText);
        Assert.Equal($"{Analyzer} · src/App/App.csproj:7", error.Origin);

        page.SelectedFilter = DiagnosticFilter.Info;
        Assert.Empty(page.Diagnostics);
        Assert.Equal("No information messages in this run.", page.DiagnosticsMessage);

        session.Clear();
        await page.LoadAsync();
        Assert.True(page.ShowEmptyState);
        Assert.Empty(page.Runs);
    }

    [Fact]
    public async Task Page_KeepsTheSelection_ButSelectsANewRun()
    {
        var imported = await ImportPartialAsync();
        await _import.ExecuteAsync(_folder.FullPath, null, Token);
        var session = new RepositorySession();
        var page = new RunHistoryPageViewModel(Get<GetRunHistory>(), session, NullLogger<RunHistoryPageViewModel>.Instance);
        session.SetCurrent(new OpenRepositoryDto
        {
            RepositoryId = imported.RepositoryId,
            Name = imported.Name,
            RootPath = imported.RootPath,
            AnalysisRunId = imported.AnalysisRunId,
        });
        await page.LoadAsync();

        page.SelectedRun = page.Runs[1];
        await page.ActivateAsync();
        Assert.Same(page.Runs[1], page.SelectedRun); // same runs: the selection stays

        await _import.ExecuteAsync(_folder.FullPath, null, Token); // re-analysis adds a run
        await page.ActivateAsync();

        Assert.Equal(3, page.Runs.Count);
        Assert.Same(page.Runs[0], page.SelectedRun);
        Assert.True(page.SelectedRun!.IsCurrent);
    }

    /// <summary>An import whose analysis reports an error and a warning, both with analyzer and file location.</summary>
    private async Task<RepositoryImportSummaryDto> ImportPartialAsync()
    {
        _analyzer.AnalyzeAsync(default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(call =>
        {
            var request = call.Arg<AnalysisRequest>();
            var file = RepositoryFile.Create(request.RepositoryId, RelativePath.Create("src/App/App.csproj"), LanguageId.Xml,
                FileClassification.ProjectManifest, 120, ContentHash.FromBytes(new byte[32]));
            var model = ArchitectureModel.Create(request.RepositoryId, request.AnalysisRunId, [file], [], [], [], [], []);
            AnalysisDiagnostic[] diagnostics =
            [
                new(DiagnosticSeverity.Error, "test.project-failed", "Project could not be read.", AnalyzerId.Create(Analyzer), SourceLocation.Line(file.Id, 7)),
                new(DiagnosticSeverity.Warning, "test.condition-ignored", "A condition was ignored.", AnalyzerId.Create(Analyzer), SourceLocation.Line(file.Id, 3)),
            ];
            return Task.FromResult<Result<RepositoryAnalysis>>(new RepositoryAnalysis(model, diagnostics, AnalysisStatistics.Empty,
                new Dictionary<string, string> { [Analyzer] = "1.0" }));
        });

        return (await _import.ExecuteAsync(_folder.FullPath, null, Token)).Value;
    }
}
