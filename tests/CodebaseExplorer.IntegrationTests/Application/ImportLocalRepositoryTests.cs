using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>
/// ImportLocalRepository and GetGraphView with a substituted analyzer and the real SQLite model store
/// (temporary database, STORE-004).
/// </summary>
public sealed class ImportLocalRepositoryTests : IAsyncLifetime
{
    private static readonly string FolderPath = Path.Combine(Path.GetTempPath(), "ce-import-test", "shop");

    private readonly IRepositoryRootValidator _validator = Substitute.For<IRepositoryRootValidator>();
    private readonly IRepositoryAnalyzer _analyzer = Substitute.For<IRepositoryAnalyzer>();
    private readonly TestApplication _app = new();
    private readonly ImportLocalRepository _import;

    public ImportLocalRepositoryTests()
    {
        _validator.Validate(Arg.Any<string>()).Returns(Result<RepositoryRoot>.Success(new RepositoryRoot(FolderPath)));
        _analyzer.AnalyzeAsync(Arg.Any<AnalysisRequest>(), Arg.Any<IProgress<AnalysisProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(EmptyAnalysis(call.Arg<AnalysisRequest>())));
        _import = new ImportLocalRepository(_validator, _analyzer, Store, TimeProvider.System);
    }

    private IArchitectureModelStore Store => _app.Services.GetRequiredService<IArchitectureModelStore>();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    /// <summary>The stored current model of the test folder, or null.</summary>
    private async Task<StoredRepositoryModel?> StoredAsync()
    {
        var repository = await Store.FindRepositoryByRootAsync(FolderPath, TestContext.Current.CancellationToken);
        return repository is null ? null : await Store.LoadCurrentModelAsync(repository.Id, TestContext.Current.CancellationToken);
    }

    private static Result<RepositoryAnalysis> EmptyAnalysis(AnalysisRequest request, params AnalysisDiagnostic[] diagnostics) =>
        new RepositoryAnalysis(
            ArchitectureModel.Create(request.RepositoryId, request.AnalysisRunId, [], [], [], [], [], []),
            diagnostics,
            AnalysisStatistics.Empty,
            new Dictionary<string, string> { ["test.analyzer"] = "1.0" });

    private Task<Result<Contracts.Repositories.RepositoryImportSummaryDto>> ImportAsync(CancellationToken? token = null) =>
        _import.ExecuteAsync(FolderPath, null, token ?? TestContext.Current.CancellationToken);

    [Fact]
    public async Task SuccessfulImport_StoresModelAndRun()
    {
        var summary = (await ImportAsync()).Value;

        var entry = await StoredAsync();
        Assert.NotNull(entry);
        Assert.Equal(summary.RepositoryId, entry.Repository.Id.ToString());
        Assert.Equal("shop", summary.Name);
        Assert.Equal(AnalysisRunStatus.Succeeded, entry.Run.Status);
        Assert.Equal(AnalysisTrigger.Initial, entry.Run.Trigger);
        Assert.Equal(entry.Run.Id, entry.Repository.LastAnalysisRunId);
        Assert.Equal("1.0", entry.Run.AnalyzerVersions["test.analyzer"]);
    }

    [Fact]
    public async Task ReImport_KeepsRepositoryIdAndIsManual()
    {
        var first = (await ImportAsync()).Value;
        var second = (await ImportAsync()).Value;

        Assert.Equal(first.RepositoryId, second.RepositoryId);
        Assert.NotEqual(first.AnalysisRunId, second.AnalysisRunId);
        Assert.Equal(AnalysisTrigger.Manual, (await StoredAsync())!.Run.Trigger);
    }

    [Fact]
    public async Task ErrorDiagnostics_MakeTheRunPartiallySucceeded()
    {
        _analyzer.AnalyzeAsync(Arg.Any<AnalysisRequest>(), Arg.Any<IProgress<AnalysisProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(EmptyAnalysis(call.Arg<AnalysisRequest>(),
                new AnalysisDiagnostic(DiagnosticSeverity.Error, "analysis.analyzer-failed", "boom"),
                new AnalysisDiagnostic(DiagnosticSeverity.Info, "x.info", "note"))));

        var summary = (await ImportAsync()).Value;

        Assert.Equal("partially-succeeded", summary.Status);
        Assert.Equal(1, summary.ErrorCount);
        Assert.Equal(["error"], summary.Diagnostics.Select(d => d.Severity)); // info is not listed
    }

    [Fact]
    public async Task InvalidFolder_IsRejectedWithoutAnalysis()
    {
        _validator.Validate(Arg.Any<string>()).Returns(Result<RepositoryRoot>.Failure(
            ApplicationError.NotFound(RepositoryErrorCodes.PathNotFound, "missing")));

        var result = await ImportAsync();

        Assert.Equal(RepositoryErrorCodes.PathNotFound, result.Error.Code);
        await _analyzer.DidNotReceiveWithAnyArgs().AnalyzeAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnalyzerFailure_IsReturnedAndNoModelIsStored()
    {
        _analyzer.AnalyzeAsync(Arg.Any<AnalysisRequest>(), Arg.Any<IProgress<AnalysisProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result<RepositoryAnalysis>.Failure(ApplicationError.Failure(AnalysisErrorCodes.ModelInvalid, "inconsistent")));

        var result = await ImportAsync();

        Assert.Equal(AnalysisErrorCodes.ModelInvalid, result.Error.Code);
        Assert.Null(await StoredAsync());
    }

    [Fact]
    public async Task Cancellation_PropagatesAndLeavesNoModel()
    {
        using var cancellation = new CancellationTokenSource();
        _analyzer.AnalyzeAsync(Arg.Any<AnalysisRequest>(), Arg.Any<IProgress<AnalysisProgress>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImportAsync(cancellation.Token));

        Assert.Null(await StoredAsync());
    }

    [Fact]
    public async Task Cancellation_KeepsThePreviousModel()
    {
        var first = (await ImportAsync()).Value;
        _analyzer.AnalyzeAsync(Arg.Any<AnalysisRequest>(), Arg.Any<IProgress<AnalysisProgress>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImportAsync());

        Assert.Equal(first.AnalysisRunId, (await StoredAsync())!.Run.Id.ToString());
    }

    [Fact]
    public async Task ConcurrentImportOfSameFolder_IsRejected_AndAllowedAgainAfterwards()
    {
        var release = new TaskCompletionSource();
        _analyzer.AnalyzeAsync(Arg.Any<AnalysisRequest>(), Arg.Any<IProgress<AnalysisProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await release.Task;
                return EmptyAnalysis(call.Arg<AnalysisRequest>());
            });

        var running = ImportAsync();
        var concurrent = await ImportAsync();

        Assert.Equal(RepositoryErrorCodes.ImportInProgress, concurrent.Error.Code);
        Assert.Equal(ErrorKind.Conflict, concurrent.Error.Kind);

        release.SetResult();
        Assert.True((await running).IsSuccess);
        Assert.True((await ImportAsync()).IsSuccess);
    }

    [Fact]
    public async Task GetGraphView_UnknownOrInvalidRepository_IsRejected()
    {
        var getGraph = new GetGraphView(Store, Substitute.For<IGraphProjector>());

        var notAnalyzed = await getGraph.ExecuteAsync(
            new GraphQuery { RepositoryId = Guid.NewGuid().ToString("N") }, TestContext.Current.CancellationToken);
        var invalid = await getGraph.ExecuteAsync(
            new GraphQuery { RepositoryId = "not-an-id" }, TestContext.Current.CancellationToken);

        Assert.Equal(RepositoryErrorCodes.NotAnalyzed, notAnalyzed.Error.Code);
        Assert.Equal(RepositoryErrorCodes.IdInvalid, invalid.Error.Code);
    }

    [Fact]
    public async Task GetGraphView_ProjectsTheStoredModel()
    {
        var summary = (await ImportAsync()).Value;
        var projector = Substitute.For<IGraphProjector>();
        var expected = new GraphViewDto { RepositoryId = summary.RepositoryId, Level = GraphLevels.Project };
        projector.Project(Arg.Any<ArchitectureModel>(), Arg.Any<GraphQuery>()).Returns(Result<GraphViewDto>.Success(expected));

        var result = await new GetGraphView(Store, projector).ExecuteAsync(
            new GraphQuery { RepositoryId = summary.RepositoryId }, TestContext.Current.CancellationToken);

        Assert.Same(expected, result.Value);
        projector.Received(1).Project(Arg.Is<ArchitectureModel>(m => m.AnalysisRunId.ToString() == summary.AnalysisRunId), Arg.Any<GraphQuery>());
    }
}
