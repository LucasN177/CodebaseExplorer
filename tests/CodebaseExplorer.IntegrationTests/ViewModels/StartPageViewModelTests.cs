using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
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
public sealed class StartPageViewModelTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TestApplication _app = new();
    private readonly IFolderPicker _picker = Substitute.For<IFolderPicker>();
    private readonly IRepositoryAnalyzer _analyzer = Substitute.For<IRepositoryAnalyzer>();
    private readonly RepositorySession _session = new();
    private readonly ShellStatus _shell = new(TimeProvider.System);

    public StartPageViewModelTests() =>
        _picker.PickFolderAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(TestRepositoryCatalog.GetFixturePath("dotnet-layered"));

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task OpenLocalFolder_Success_SetsCurrentRepository()
    {
        var viewModel = CreateViewModel(_app.Services.GetRequiredService<IRepositoryAnalyzer>());

        // Like the UI thread, the inline context applies progress reports in order, before completion;
        // without a context Progress<T> would apply them on the thread pool, racing with completion.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());
        try
        {
            await viewModel.OpenLocalFolderCommand.ExecuteAsync(null);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal("dotnet-layered", _session.Current?.Name);
        Assert.False(viewModel.IsImporting);
        Assert.Null(viewModel.ErrorMessage);
        Assert.StartsWith("Analyzed dotnet-layered: 6 projects", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.All(viewModel.Stages, s => Assert.Equal(ImportStageState.Done, s.State));
        Assert.False(_shell.IsBusy);
        Assert.Equal("Ready", _shell.StatusText);
        Assert.Empty(_shell.Notifications);
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

        // The error stays visible in the shell after navigating away from the start page (UI-001).
        var notification = Assert.Single(_shell.Notifications);
        Assert.Equal(NotificationSeverity.Error, notification.Severity);
        Assert.EndsWith("dotnet-layered failed: The model is invalid.", notification.Message, StringComparison.Ordinal);
        Assert.False(_shell.IsBusy);
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
        Assert.Empty(_shell.Notifications);
        Assert.False(_shell.IsBusy);
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
            Assert.True(_shell.IsBusy);
            Assert.Equal("Analyzing dotnet-layered: Detecting projects 2 / 4", _shell.StatusText);

            finish.SetResult();
            await running.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            Assert.Null(viewModel.CurrentItem);
            Assert.False(_shell.IsBusy);
            Assert.Equal("Ready", _shell.StatusText);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task Activate_ListsStoredRepositories()
    {
        await ImportFixtureAsync();
        var viewModel = CreateViewModel(_analyzer);

        await viewModel.ActivateAsync();

        var item = Assert.Single(viewModel.RecentRepositories);
        Assert.Equal("dotnet-layered", item.Name);
        Assert.Equal(TestRepositoryCatalog.GetFixturePath("dotnet-layered"), item.RootPath);
        Assert.True(item.CanOpen);
        Assert.StartsWith("Succeeded · ", item.StatusText, StringComparison.Ordinal);
        Assert.True(viewModel.HasRecentRepositories);
    }

    [Fact]
    public async Task OpenRecent_SetsTheStoredRepositoryWithoutAnalysis()
    {
        var imported = await ImportFixtureAsync();
        var viewModel = CreateViewModel(_analyzer);
        await viewModel.ActivateAsync();

        await viewModel.OpenRecentCommand.ExecuteAsync(viewModel.RecentRepositories[0]);

        await _analyzer.DidNotReceiveWithAnyArgs().AnalyzeAsync(default!, default, Arg.Any<CancellationToken>());
        Assert.Equal(imported.RepositoryId, _session.Current?.RepositoryId);
        Assert.Equal(imported.AnalysisRunId, _session.Current?.AnalysisRunId);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Reanalyze_RunsANewAnalysisOfTheFolder()
    {
        await ImportFixtureAsync();
        var viewModel = CreateViewModel(_app.Services.GetRequiredService<IRepositoryAnalyzer>());
        await viewModel.ActivateAsync();
        var previousRun = viewModel.RecentRepositories[0].Repository;

        await viewModel.ReanalyzeCommand.ExecuteAsync(viewModel.RecentRepositories[0]);

        Assert.Equal(previousRun.RepositoryId, _session.Current?.RepositoryId);
        Assert.StartsWith("Analyzed dotnet-layered", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Single(viewModel.RecentRepositories);
    }

    [Fact]
    public async Task Remove_AsksFirst_ThenDeletesAndClosesTheRepository()
    {
        var imported = await ImportFixtureAsync();
        var viewModel = CreateViewModel(_analyzer);
        await viewModel.ActivateAsync();
        await viewModel.OpenRecentCommand.ExecuteAsync(viewModel.RecentRepositories[0]);
        var item = viewModel.RecentRepositories[0];

        viewModel.RequestRemoveCommand.Execute(item);
        Assert.True(item.IsConfirmingRemove);
        viewModel.CancelRemoveCommand.Execute(item);
        Assert.False(item.IsConfirmingRemove);
        Assert.Single(viewModel.RecentRepositories);

        viewModel.RequestRemoveCommand.Execute(item);
        await viewModel.ConfirmRemoveCommand.ExecuteAsync(item);

        Assert.Empty(viewModel.RecentRepositories);
        Assert.Null(_session.Current);
        Assert.Equal("Removed the stored data of dotnet-layered. The folder was not changed.", viewModel.StatusMessage);
        var store = _app.Services.GetRequiredService<IArchitectureModelStore>();
        Assert.Null(await store.FindRepositoryAsync(new Domain.Common.RepositoryId(Guid.ParseExact(imported.RepositoryId, "N")), TestContext.Current.CancellationToken));
    }

    private async Task<Contracts.Repositories.RepositoryImportSummaryDto> ImportFixtureAsync() =>
        (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(
            TestRepositoryCatalog.GetFixturePath("dotnet-layered"), null, TestContext.Current.CancellationToken)).Value;

    private StartPageViewModel CreateViewModel(IRepositoryAnalyzer analyzer) => new(
        new ImportLocalRepository(
            _app.Services.GetRequiredService<IRepositoryRootValidator>(),
            analyzer,
            _app.Services.GetRequiredService<IArchitectureModelStore>(),
            TimeProvider.System),
        _app.Services.GetRequiredService<ListRecentRepositories>(),
        _app.Services.GetRequiredService<OpenRepository>(),
        _app.Services.GetRequiredService<RemoveRepository>(),
        _picker,
        _session,
        _shell,
        NullLogger<StartPageViewModel>.Instance);

    /// <summary>Runs posted callbacks immediately, so Progress&lt;T&gt; reports are applied deterministically.</summary>
    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }
}
