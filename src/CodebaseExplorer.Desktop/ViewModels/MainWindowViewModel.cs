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
        RunHistoryPageViewModel runHistory,
        ExportPageViewModel export,
        SettingsPageViewModel settings,
        RepositorySession session,
        ShellStatus shell)
    {
        Pages = [start, overview, architectureExplorer, runHistory, export, settings];
        CurrentPage = start;
        Shell = shell;
        overview.ExplorerRequested += (_, _) => CurrentPage = architectureExplorer;

        // A successful import or opening a stored repository shows the overview (WF-01 step 5, UI-004), which links to the explorer.
        session.CurrentChanged += (_, _) =>
        {
            if (session.Current is not null)
            {
                CurrentPage = overview;
            }
        };
    }

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
