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
        ArchitectureExplorerPageViewModel architectureExplorer,
        SettingsPageViewModel settings,
        RepositorySession session,
        ShellStatus shell)
    {
        Pages = [start, architectureExplorer, settings];
        CurrentPage = start;
        Shell = shell;

        // A successful import opens the explorer (UI-002, WF-01 step 5).
        session.CurrentChanged += (_, _) => CurrentPage = architectureExplorer;
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
