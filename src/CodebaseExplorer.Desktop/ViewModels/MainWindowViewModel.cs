using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodebaseExplorer.Desktop.ViewModels;

/// <summary>Shell ViewModel: owns the navigation pages and the currently displayed page.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(
        StartPageViewModel start,
        ArchitectureExplorerPageViewModel architectureExplorer,
        SettingsPageViewModel settings,
        RepositorySession session)
    {
        Pages = [start, architectureExplorer, settings];
        CurrentPage = start;

        // A successful import opens the explorer (UI-002, WF-01 step 5).
        session.CurrentChanged += (_, _) => CurrentPage = architectureExplorer;
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    [ObservableProperty]
    public partial PageViewModel CurrentPage { get; set; }
}
