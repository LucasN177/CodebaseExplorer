using CodebaseExplorer.Desktop.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodebaseExplorer.Desktop.ViewModels;

/// <summary>Shell ViewModel: owns the navigation pages and the currently displayed page.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(
        StartPageViewModel start,
        ArchitectureExplorerPageViewModel architectureExplorer,
        SettingsPageViewModel settings)
    {
        Pages = [start, architectureExplorer, settings];
        CurrentPage = start;
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    [ObservableProperty]
    public partial PageViewModel CurrentPage { get; set; }
}
