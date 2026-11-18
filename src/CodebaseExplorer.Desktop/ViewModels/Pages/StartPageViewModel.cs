using System.Collections.ObjectModel;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// Start page (USER_WORKFLOWS.md WF-01): import flow for a local folder (UI-002: folder picker, progress
/// panel with stages and cancel, inline errors) and the list of recent repositories (REPO-005: open the
/// stored model without a new analysis, re-analyze, remove). A successful import or opening becomes the
/// current repository of the <see cref="RepositorySession"/>, which makes the shell navigate.
/// </summary>
public sealed partial class StartPageViewModel : PageViewModel
{
    private readonly ImportLocalRepository _import;
    private readonly ListRecentRepositories _listRecent;
    private readonly OpenRepository _open;
    private readonly RemoveRepository _remove;
    private readonly IFolderPicker _folderPicker;
    private readonly RepositorySession _session;
    private readonly ShellStatus _shell;
    private readonly ILogger<StartPageViewModel> _logger;
    private int _importNumber;
    private BackgroundOperation? _operation;

    public StartPageViewModel(
        ImportLocalRepository import,
        ListRecentRepositories listRecent,
        OpenRepository open,
        RemoveRepository remove,
        IFolderPicker folderPicker,
        RepositorySession session,
        ShellStatus shell,
        ILogger<StartPageViewModel> logger)
    {
        _import = import;
        _listRecent = listRecent;
        _open = open;
        _remove = remove;
        _folderPicker = folderPicker;
        _session = session;
        _shell = shell;
        _logger = logger;

        Stages =
        [
            new(AnalysisStage.Preparing, "Preparing"),
            new(AnalysisStage.Scanning, "Scanning files"),
            new(AnalysisStage.DetectingProjects, "Detecting projects"),
            new(AnalysisStage.AnalyzingCode, "Analyzing code"),
            new(AnalysisStage.Linking, "Linking"),
            new(AnalysisStage.Validating, "Validating"),
            new(AnalysisStage.ReadingGitHistory, "Reading Git history"),
            new(AnalysisStage.Saving, "Saving"),
        ];
        RecentRepositories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentRepositories));
    }

    public override string Title => "Start";

    public override string Glyph => "⌂";

    /// <summary>Analysis stages in pipeline order, with their state during the current import.</summary>
    public IReadOnlyList<ImportStageViewModel> Stages { get; }

    /// <summary>Stored repositories, most recently used first (REPO-005).</summary>
    public ObservableCollection<RecentRepositoryItemViewModel> RecentRepositories { get; } = [];

    public bool HasRecentRepositories => RecentRepositories.Count > 0;

    /// <summary>True while an import runs (shows the progress panel).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLocalFolderCommand), nameof(ReanalyzeCommand), nameof(OpenRecentCommand), nameof(RequestRemoveCommand))]
    public partial bool IsImporting { get; private set; }

    /// <summary>Folder being imported.</summary>
    [ObservableProperty]
    public partial string? ImportPath { get; private set; }

    /// <summary>Completed percentage of the current stage (0–100) when its total is known.</summary>
    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; private set; } = true;

    /// <summary>Counts of the current stage, e.g. <c>12 / 40</c>.</summary>
    [ObservableProperty]
    public partial string? ProgressText { get; private set; }

    /// <summary>Item being processed (repository-relative path or project name).</summary>
    [ObservableProperty]
    public partial string? CurrentItem { get; private set; }

    /// <summary>Outcome of the last import that did not fail (completed or cancelled), or of a removal.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    /// <summary>Why the last import, opening or removal failed; shown inline.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public override Task ActivateAsync() => LoadRecentRepositoriesAsync();

    /// <summary>Reloads the recent repositories; failures are logged and shown inline.</summary>
    public async Task LoadRecentRepositoriesAsync()
    {
        try
        {
            var repositories = await _listRecent.ExecuteAsync(CancellationToken.None);
            RecentRepositories.Clear();
            foreach (var repository in repositories)
            {
                RecentRepositories.Add(new RecentRepositoryItemViewModel(repository));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Loading the recent repositories failed");
            ErrorMessage = "The recent repositories could not be loaded. Details are in the application log.";
        }
    }

    /// <summary>
    /// Picks a folder and analyzes it. The generated <c>OpenLocalFolderCancelCommand</c> cancels a
    /// running import; the command cannot run twice at the same time.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanStartImport))]
    private async Task OpenLocalFolderAsync(CancellationToken cancellationToken)
    {
        var path = await _folderPicker.PickFolderAsync("Open local folder", cancellationToken);
        if (path is null)
        {
            return;
        }

        await ImportAsync(path, cancellationToken);
    }

    /// <summary>Runs a new full analysis of a stored repository's folder (WF-10); <c>ReanalyzeCancelCommand</c> cancels it.</summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanStartImport))]
    private Task ReanalyzeAsync(RecentRepositoryItemViewModel? item, CancellationToken cancellationToken) =>
        item is null ? Task.CompletedTask : ImportAsync(item.RootPath, cancellationToken);

    /// <summary>Cancels the running import, whichever command started it.</summary>
    [RelayCommand]
    private void CancelImport()
    {
        OpenLocalFolderCancelCommand.Execute(null);
        ReanalyzeCancelCommand.Execute(null);
    }

    /// <summary>Opens the stored model of a recent repository without a new analysis.</summary>
    [RelayCommand(CanExecute = nameof(CanStartImport))]
    private async Task OpenRecentAsync(RecentRepositoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var result = await _open.ExecuteAsync(item.Repository.RepositoryId, CancellationToken.None);
            if (!result.TryGetValue(out var repository, out var error))
            {
                _logger.LogWarning("Opening a stored repository failed with {ErrorCode}", error.Code);
                ErrorMessage = error.Message;
                await LoadRecentRepositoriesAsync();
                return;
            }

            _session.SetCurrent(repository);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Opening a stored repository failed unexpectedly");
            ErrorMessage = "The repository could not be opened. Details are in the application log.";
        }
    }

    /// <summary>Shows the inline confirmation for removing a repository's stored data.</summary>
    [RelayCommand(CanExecute = nameof(CanStartImport))]
    private void RequestRemove(RecentRepositoryItemViewModel? item)
    {
        foreach (var other in RecentRepositories)
        {
            other.IsConfirmingRemove = ReferenceEquals(other, item);
        }
    }

    [RelayCommand]
    private static void CancelRemove(RecentRepositoryItemViewModel? item)
    {
        if (item is not null)
        {
            item.IsConfirmingRemove = false;
        }
    }

    /// <summary>Deletes all stored data of the repository (runs, diagnostics, model); the folder is not touched.</summary>
    [RelayCommand]
    private async Task ConfirmRemoveAsync(RecentRepositoryItemViewModel? item)
    {
        if (item is null || IsImporting)
        {
            return;
        }

        item.IsConfirmingRemove = false;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var result = await _remove.ExecuteAsync(item.Repository.RepositoryId, CancellationToken.None);
            if (result.IsFailure)
            {
                _logger.LogWarning("Removing a repository failed with {ErrorCode}", result.Error!.Code);
                ErrorMessage = result.Error.Message;
            }
            else
            {
                StatusMessage = $"Removed the stored data of {item.Name}. The folder was not changed.";
            }

            if (_session.Current?.RepositoryId == item.Repository.RepositoryId)
            {
                _session.Clear();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Removing a repository failed unexpectedly");
            ErrorMessage = "The repository could not be removed. Details are in the application log.";
        }

        await LoadRecentRepositoriesAsync();
    }

    private bool CanStartImport() => !IsImporting;

    private async Task ImportAsync(string path, CancellationToken cancellationToken)
    {
        var importNumber = ++_importNumber;
        StartImport(path);

        // Reports are marshalled to the UI thread by Progress<T>; late reports of a finished import are dropped.
        var progress = new Progress<AnalysisProgress>(report =>
        {
            if (importNumber == _importNumber && IsImporting)
            {
                ApplyProgress(report);
            }
        });

        try
        {
            // The whole use case runs on the thread pool so that no analysis work ever runs on the UI thread.
            var result = await Task.Run(() => _import.ExecuteAsync(path, progress, cancellationToken), cancellationToken);

            if (!result.TryGetValue(out var summary, out var error))
            {
                _logger.LogWarning("Import failed with {ErrorCode}", error.Code);
                FinishImport(errorMessage: error.Message);
                return;
            }

            // Finish first: progress reports that arrive afterwards are dropped (IsImporting is false).
            FinishImport(statusMessage:
                $"Analyzed {summary.Name}: {summary.ProjectCount} projects, {summary.FileCount} files, " +
                $"{RunItemViewModel.Count(summary.WarningCount, "warning")}, {RunItemViewModel.Count(summary.ErrorCount, "error")}.");
            foreach (var stage in Stages)
            {
                stage.State = ImportStageState.Done;
            }

            _session.SetCurrent(new OpenRepositoryDto
            {
                RepositoryId = summary.RepositoryId,
                Name = summary.Name,
                RootPath = summary.RootPath,
                AnalysisRunId = summary.AnalysisRunId,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            FinishImport(statusMessage: "Analysis cancelled. Nothing was changed.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Last line of defense for the UI: unexpected failures are logged and shown, never thrown into the dispatcher.
            _logger.LogError(exception, "Import failed unexpectedly");
            FinishImport(errorMessage: "The analysis failed unexpectedly. Details are in the application log.");
        }

        // Every import (also a failed or cancelled one) records a run, which the list shows.
        await LoadRecentRepositoriesAsync();
    }

    private void StartImport(string path)
    {
        ErrorMessage = null;
        StatusMessage = null;
        ImportPath = path;
        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        ProgressText = null;
        CurrentItem = null;
        foreach (var stage in Stages)
        {
            stage.State = ImportStageState.Pending;
        }

        foreach (var item in RecentRepositories)
        {
            item.IsConfirmingRemove = false;
        }

        IsImporting = true;
        _operation?.Dispose();
        _operation = _shell.BeginOperation($"Analyzing {Path.GetFileName(Path.TrimEndingDirectorySeparator(path))}");
    }

    private void FinishImport(string? statusMessage = null, string? errorMessage = null)
    {
        IsImporting = false;
        CurrentItem = null;
        StatusMessage = statusMessage;
        ErrorMessage = errorMessage;
        _operation?.Dispose();
        _operation = null;

        // The page shows the error inline; the notification keeps it visible after navigating away.
        if (errorMessage is not null)
        {
            _shell.Notify(NotificationSeverity.Error, $"Import of {ImportPath} failed: {errorMessage}");
        }
    }

    private void ApplyProgress(AnalysisProgress report)
    {
        var activeIndex = Stages.Select((stage, index) => (stage, index)).First(s => s.stage.Stage == report.Stage).index;
        for (var i = 0; i < Stages.Count; i++)
        {
            Stages[i].State = i < activeIndex ? ImportStageState.Done
                : i == activeIndex ? ImportStageState.Active
                : ImportStageState.Pending;
        }

        IsProgressIndeterminate = report.Fraction is null;
        ProgressPercent = (report.Fraction ?? 0) * 100;
        ProgressText = report.Total is { } total ? $"{report.Processed} / {total}"
            : report.Processed > 0 ? report.Processed.ToString(System.Globalization.CultureInfo.CurrentCulture)
            : null;
        CurrentItem = report.CurrentItem;

        if (_operation is not null)
        {
            _operation.Detail = ProgressText is null ? Stages[activeIndex].Name : $"{Stages[activeIndex].Name} {ProgressText}";
        }
    }
}
