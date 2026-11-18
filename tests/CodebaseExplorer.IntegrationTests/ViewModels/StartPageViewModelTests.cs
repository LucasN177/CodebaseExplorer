using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Desktop.Composition;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CodebaseExplorer.IntegrationTests.ViewModels;

/// <summary>
/// UI-002: import flow of the start page without UI. The use case is real; its analyzer port is the
/// real pipeline (success) or a substitute (error, cancellation, progress, threading).
/// </summary>
public sealed class StartPageViewModelTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ServiceProvider _services = CompositionRoot.BuildServiceProvider();
    private readonly IFolderPicker _picker = Substitute.For<IFolderPicker>();
    private readonly IRepositoryAnalyzer _analyzer = Substitute.For<IRepositoryAnalyzer>();
    private readonly RepositorySession _session = new();

    public StartPageViewModelTests() =>
        _picker.PickFolderAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(TestRepositoryCatalog.GetFixturePath("dotnet-layered"));

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task OpenLocalFolder_Success_SetsCurrentRepository()
    {
        var viewModel = CreateViewModel(_services.GetRequiredService<IRepositoryAnalyzer>());

        await viewModel.OpenLocalFolderCommand.ExecuteAsync(null);

        Assert.Equal("dotnet-layered", _session.Current?.Name);
        Assert.False(viewModel.IsImporting);
        Assert.Null(viewModel.ErrorMessage);
        Assert.StartsWith("Analyzed dotnet-layered: 6 projects", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.All(viewModel.Stages, s => Assert.Equal(ImportStageState.Done, s.State));
    }

    [Fact]
    public async Task OpenLocalFolder_PickerCancelled_DoesNothing()
    {
        _picker.PickFolderAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs((string?)null);
        var viewModel = CreateViewModel(_analyzer);

        await viewModel.OpenLocalFolderCommand.ExecuteAsync(null);

        await _analyzer.DidNotReceiveWithAnyArgs().AnalyzeAsync(default!, default, Arg.Any<CancellationToken>());
        Assert.Null(viewModel.StatusMessage);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Null(_session.Current);
    }

    [Fact]
    public async Task OpenLocalFolder_InvalidFolder_ShowsErrorInline()
    {
        _picker.PickFolderAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var viewModel = CreateViewModel(_analyzer);

        await viewModel.OpenLocalFolderCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.False(viewModel.IsImporting);
        Assert.Null(_session.Current);
    }

    [Fact]
    public async Task OpenLocalFolder_AnalysisFails_ShowsErrorInline()
    {
        _analyzer.AnalyzeAsync(default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(
            Task.FromResult<Result<RepositoryAnalysis>>(ApplicationError.Failure(AnalysisErrorCodes.ModelInvalid, "The model is invalid.")));
        var viewModel = CreateViewModel(_analyzer);

        await viewModel.OpenLocalFolderCommand.ExecuteAsync(null);

        Assert.Equal("The model is invalid.", viewModel.ErrorMessage);
        Assert.Null(viewModel.StatusMessage);
        Assert.Null(_session.Current);
    }

    [Fact]
    public async Task OpenLocalFolder_UnexpectedException_ShowsGenericErrorInline()
    {
        _analyzer.AnalyzeAsync(default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs<Task<Result<RepositoryAnalysis>>>(_ => throw new IOException("disk gone"));
        var viewModel = CreateViewModel(_analyzer);

        await viewModel.OpenLocalFolderCommand.ExecuteAsync(null);

        Assert.Equal("The analysis failed unexpectedly. Details are in the application log.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsImporting);
    }

    [Fact]
    public async Task Cancel_StopsTheImport_AndKeepsTheSessionUnchanged()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _analyzer.AnalyzeAsync(default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(async call =>
        {
            started.SetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, call.ArgAt<CancellationToken>(2));
            return (Result<RepositoryAnalysis>)ApplicationError.Failure("unreachable", "unreachable");
        });
        var viewModel = CreateViewModel(_analyzer);

        var running = viewModel.OpenLocalFolderCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.True(viewModel.IsImporting);
        Assert.False(viewModel.OpenLocalFolderCommand.CanExecute(null));

        viewModel.OpenLocalFolderCancelCommand.Execute(null);
        await running.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.False(viewModel.IsImporting);
        Assert.Equal("Analysis cancelled. Nothing was changed.", viewModel.StatusMessage);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Null(_session.Current);
    }

    [Fact]
    public async Task Import_NeverRunsAnalysisWorkOnTheCallingThread()
    {
        // The analyzer blocks synchronously before returning its task. If the ViewModel ran the use case on
        // the calling (UI) thread, ExecuteAsync would not return until the gate opens. Thread ids are not
        // compared: the thread pool may reuse the test's thread once it is free (flaky on CI runners).
        using var gate = new ManualResetEventSlim();
        using var analyzerStarted = new ManualResetEventSlim();
        _analyzer.AnalyzeAsync(default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(_ =>
        {
            analyzerStarted.Set();
            gate.Wait(Timeout);
            return Task.FromResult<Result<RepositoryAnalysis>>(ApplicationError.Failure("test", "stopped"));
        });
        var viewModel = CreateViewModel(_analyzer);

        var running = viewModel.OpenLocalFolderCommand.ExecuteAsync(null);

        // The command has returned while the analysis is blocked: the work runs elsewhere.
        Assert.False(running.IsCompleted);
        Assert.True(viewModel.IsImporting);
        Assert.True(analyzerStarted.Wait(Timeout, TestContext.Current.CancellationToken), "the analysis did not start in the background");
        Assert.False(running.IsCompleted);

        gate.Set();
        await running.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.False(viewModel.IsImporting);
    }

    [Fact]
    public async Task Progress_UpdatesStagesCountsAndCurrentItem()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());
        try
        {
            var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _analyzer.AnalyzeAsync(default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(async call =>
            {
                call.ArgAt<IProgress<AnalysisProgress>?>(1)!.Report(new AnalysisProgress(AnalysisStage.DetectingProjects, 2, 4, "src/Shop.Web/Shop.Web.csproj"));
                reported.SetResult();
                await finish.Task;
                return (Result<RepositoryAnalysis>)ApplicationError.Failure("test", "stopped");
            });
            var viewModel = CreateViewModel(_analyzer);

            var running = viewModel.OpenLocalFolderCommand.ExecuteAsync(null);
            await reported.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

            Assert.Equal(
                [ImportStageState.Done, ImportStageState.Done, ImportStageState.Active, ImportStageState.Pending,
                 ImportStageState.Pending, ImportStageState.Pending, ImportStageState.Pending],
                viewModel.Stages.Select(s => s.State));
            Assert.Equal("2 / 4", viewModel.ProgressText);
            Assert.Equal(50, viewModel.ProgressPercent);
            Assert.False(viewModel.IsProgressIndeterminate);
            Assert.Equal("src/Shop.Web/Shop.Web.csproj", viewModel.CurrentItem);

            finish.SetResult();
            await running.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            Assert.Null(viewModel.CurrentItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private StartPageViewModel CreateViewModel(IRepositoryAnalyzer analyzer) => new(
        new ImportLocalRepository(
            _services.GetRequiredService<IRepositoryRootValidator>(),
            analyzer,
            _services.GetRequiredService<IArchitectureModelCache>(),
            TimeProvider.System),
        _picker,
        _session,
        NullLogger<StartPageViewModel>.Instance);

    /// <summary>Runs posted callbacks immediately, so Progress&lt;T&gt; reports are applied deterministically.</summary>
    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }
}
