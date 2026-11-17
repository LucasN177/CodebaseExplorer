using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly IThemeService _themeService;

    public SettingsPageViewModel(IThemeService themeService, IApplicationPaths paths)
    {
        _themeService = themeService;
        SelectedTheme = themeService.CurrentTheme;
        DataDirectory = paths.DataDirectory;
    }

    public override string Title => "Settings";

    public override string Glyph => "⚙";

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    [ObservableProperty]
    public partial AppTheme SelectedTheme { get; set; }

    /// <summary>Where analysis results are stored locally (local-first).</summary>
    public string DataDirectory { get; }

    partial void OnSelectedThemeChanged(AppTheme value) => _themeService.Apply(value);
}
