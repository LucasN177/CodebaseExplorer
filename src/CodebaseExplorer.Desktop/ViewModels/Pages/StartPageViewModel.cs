using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// Start page with the import flow for a local folder (UI-002, USER_WORKFLOWS.md WF-01): folder picker,
/// progress panel with stages and cancel, inline errors. A successful import becomes the current
/// repository of the <see cref="RepositorySession"/>, which makes the shell open the explorer.
/// </summary>
public sealed partial class StartPageViewModel : PageViewModel
{
    private readonly ImportLocalRepository _import;
    private readonly IFolderPicker _folderPicker;
    private readonly RepositorySession _session;
    private readonly ILogger<StartPageViewModel> _logger;
    private int _importNumber;

    public StartPageViewModel(
        ImportLocalRepository import,
        IFolderPicker folderPicker,
        RepositorySession session,
        ILogger<StartPageViewModel> logger)
    {
        _import = import;
        _folderPicker = folderPicker;
        _session = session;
        _logger = logger;

        Stages =
        [
            new(AnalysisStage.Preparing, "Preparing"),
            new(AnalysisStage.Scanning, "Scanning files"),
            new(AnalysisStage.DetectingProjects, "Detecting projects"),
            new(AnalysisStage.AnalyzingCode, "Analyzing code"),
            new(AnalysisStage.Linking, "Linking"),
            new(AnalysisStage.Validating, "Validating"),
            new(AnalysisStage.Saving, "Saving"),
        ];
    }

    public override string Title => "Start";

    public override string Glyph => "⌂";

    /// <summary>Analysis stages in pipeline order, with their state during the current import.</summary>
    public IReadOnlyList<ImportStageViewModel> Stages { get; }

    /// <summary>True while an import runs (shows the progress panel).</summary>
    [ObservableProperty]
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

    /// <summary>Outcome of the last import that did not fail (completed or cancelled).</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    /// <summary>Why the last import failed; shown inline.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>
    /// Picks a folder and analyzes it. The generated <c>OpenLocalFolderCancelCommand</c> cancels a
    /// running import; the command cannot run twice at the same time.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task OpenLocalFolderAsync(CancellationToken cancellationToken)
    {
        var path = await _folderPicker.PickFolderAsync("Open local folder", cancellationToken);
        if (path is null)
        {
            return;
        }

        await ImportAsync(path, cancellationToken);
    }

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

            foreach (var stage in Stages)
            {
                stage.State = ImportStageState.Done;
            }

            FinishImport(statusMessage:
                $"Analyzed {summary.Name}: {summary.ProjectCount} projects, {summary.FileCount} files, " +
                $"{summary.WarningCount} warnings, {summary.ErrorCount} errors.");
            _session.SetCurrent(summary);
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

        IsImporting = true;
    }

    private void FinishImport(string? statusMessage = null, string? errorMessage = null)
    {
        IsImporting = false;
        CurrentItem = null;
        StatusMessage = statusMessage;
        ErrorMessage = errorMessage;
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
    }
}
