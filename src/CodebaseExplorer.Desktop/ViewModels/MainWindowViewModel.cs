using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodebaseExplorer.Desktop.ViewModels;

/// <summary>
/// Shell ViewModel: owns the navigation pages, the currently displayed page, and the shell status
/// (status bar, busy indicator, notifications; UI-001).
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(
        StartPageViewModel start,
        OverviewPageViewModel overview,
        ArchitectureExplorerPageViewModel architectureExplorer,
        AssistantPageViewModel assistant,
        RunHistoryPageViewModel runHistory,
        ExportPageViewModel export,
        SettingsPageViewModel settings,
        RepositorySession session,
        ShellStatus shell)
    {
        Pages = [start, overview, architectureExplorer, assistant, runHistory, export, settings];
        CurrentPage = start;
        Shell = shell;
        overview.ExplorerRequested += (_, _) => CurrentPage = architectureExplorer;

        // A citation of an AI answer that names a model element is shown in the graph (AI-006).
        assistant.ElementRequested += async (_, elementId) =>
        {
            CurrentPage = architectureExplorer;
            if (!await architectureExplorer.ShowElementAsync(elementId))
            {
                Shell.Notify(NotificationSeverity.Warning, "The cited element could not be shown in the graph.");
            }
        };

        // A successful import or opening a stored repository shows the overview (WF-01 step 5, UI-004), which links to the explorer.
        _session = session;
        session.CurrentChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RepositoryName));
            OnPropertyChanged(nameof(RepositoryPath));
            OnPropertyChanged(nameof(HasRepository));
            if (session.Current is not null)
            {
                CurrentPage = overview;
            }
        };
    }

    private readonly RepositorySession _session;

    /// <summary>The open repository, shown in the sidebar.</summary>
    public string? RepositoryName => _session.Current?.Name;

    public string? RepositoryPath => _session.Current?.RootPath;

    public bool HasRepository => _session.Current is not null;

    // Pages refresh their data whenever they are shown (e.g. the start page's recent repositories).
    partial void OnCurrentPageChanged(PageViewModel value)
    {
        // The navigation list may report no selection while it updates.
        if (value is not null)
        {
            _ = value.ActivateAsync();
        }
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    [ObservableProperty]
    public partial PageViewModel CurrentPage { get; set; }

    /// <summary>Background operations and notifications shown by the shell.</summary>
    public ShellStatus Shell { get; }

    [RelayCommand]
    private void DismissNotification(ShellNotification? notification)
    {
        if (notification is not null)
        {
            Shell.Dismiss(notification);
        }
    }
}
