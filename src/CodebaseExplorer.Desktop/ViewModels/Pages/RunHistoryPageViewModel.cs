using System.Collections.ObjectModel;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// Run history of the open repository (UI-005, WF-10): runs with status, duration, commit and statistics;
/// the diagnostics of the selected run, filterable by severity, with analyzer and location.
/// </summary>
public sealed partial class RunHistoryPageViewModel : PageViewModel
{
    private readonly GetRunHistory _getRunHistory;
    private readonly RepositorySession _session;
    private readonly ILogger<RunHistoryPageViewModel> _logger;
    private int _loadNumber;

    public RunHistoryPageViewModel(GetRunHistory getRunHistory, RepositorySession session, ILogger<RunHistoryPageViewModel> logger)
    {
        _getRunHistory = getRunHistory;
        _session = session;
        _logger = logger;

        _session.CurrentChanged += (_, _) => _ = LoadAsync();
    }

    public override string Title => "Run History";

    public override string IconKey => "history";

    /// <summary>Name of the repository whose runs are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRepository), nameof(ShowEmptyState))]
    public partial string? RepositoryName { get; private set; }

    public bool HasRepository => RepositoryName is not null;

    public bool ShowEmptyState => !HasRepository;

    /// <summary>Newest first.</summary>
    public ObservableCollection<RunItemViewModel> Runs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRun))]
    public partial RunItemViewModel? SelectedRun { get; set; }

    public bool HasSelectedRun => SelectedRun is not null;

    public IReadOnlyList<DiagnosticFilter> Filters { get; } = Enum.GetValues<DiagnosticFilter>();

    [ObservableProperty]
    public partial DiagnosticFilter SelectedFilter { get; set; }

    /// <summary>Diagnostics of the selected run that match <see cref="SelectedFilter"/>.</summary>
    public ObservableCollection<DiagnosticItemViewModel> Diagnostics { get; } = [];

    /// <summary>E.g. <c>No diagnostics in this run</c> or <c>No errors in this run</c>; null when diagnostics are listed.</summary>
    [ObservableProperty]
    public partial string? DiagnosticsMessage { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public override Task ActivateAsync() => LoadAsync();

    /// <summary>Loads the runs of the open repository; keeps the selected run if it still exists.</summary>
    public async Task LoadAsync()
    {
        var loadNumber = ++_loadNumber;
        if (_session.Current is not { } repository)
        {
            RepositoryName = null;
            ErrorMessage = null;
            Runs.Clear();
            SelectedRun = null;
            return;
        }

        RepositoryName = repository.Name;
        ErrorMessage = null;
        try
        {
            var result = await _getRunHistory.ExecuteAsync(repository.RepositoryId, CancellationToken.None);
            if (loadNumber != _loadNumber)
            {
                return; // the repository changed meanwhile
            }

            if (!result.TryGetValue(out var runs, out var error))
            {
                _logger.LogWarning("Run history failed with {ErrorCode}", error.Code);
                ErrorMessage = error.Message;
                Runs.Clear();
                SelectedRun = null;
                return;
            }

            // Keep the user's selection while the runs are the same; a new run (e.g. after a re-analysis) is selected.
            var selectedId = SelectedRun?.Run.RunId;
            var newestBefore = Runs.FirstOrDefault()?.Run.RunId;
            Runs.Clear();
            foreach (var run in runs)
            {
                Runs.Add(new RunItemViewModel(run));
            }

            var keepSelection = newestBefore is not null && newestBefore == Runs.FirstOrDefault()?.Run.RunId;
            SelectedRun = (keepSelection ? Runs.FirstOrDefault(r => r.Run.RunId == selectedId) : null) ?? Runs.FirstOrDefault();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Loading the run history failed unexpectedly");
            ErrorMessage = "The run history could not be loaded. Details are in the application log.";
        }
    }

    partial void OnSelectedRunChanged(RunItemViewModel? value) => ApplyFilter();

    partial void OnSelectedFilterChanged(DiagnosticFilter value) => ApplyFilter();

    private void ApplyFilter()
    {
        Diagnostics.Clear();
        if (SelectedRun is not { } run)
        {
            DiagnosticsMessage = null;
            return;
        }

        var severity = SelectedFilter switch
        {
            DiagnosticFilter.Errors => "error",
            DiagnosticFilter.Warnings => "warning",
            DiagnosticFilter.Info => "info",
            _ => null,
        };
        foreach (var diagnostic in run.Run.Diagnostics.Where(d => severity is null || d.Severity == severity))
        {
            Diagnostics.Add(new DiagnosticItemViewModel(diagnostic));
        }

        DiagnosticsMessage = Diagnostics.Count > 0 ? null : SelectedFilter switch
        {
            DiagnosticFilter.Errors => "No errors in this run.",
            DiagnosticFilter.Warnings => "No warnings in this run.",
            DiagnosticFilter.Info => "No information messages in this run.",
            _ => "No diagnostics in this run.",
        };
    }
}
