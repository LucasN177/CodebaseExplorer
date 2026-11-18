using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>Settings page: theme (persisted, STORE-005) and the local data directory.</summary>
public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly IThemeService _themeService;
    private readonly ShellStatus _shell;
    private readonly ILogger<SettingsPageViewModel> _logger;
    private readonly bool _initialized;

    public SettingsPageViewModel(IThemeService themeService, IApplicationPaths paths, ShellStatus shell, ILogger<SettingsPageViewModel> logger)
    {
        _themeService = themeService;
        _shell = shell;
        _logger = logger;
        SelectedTheme = themeService.CurrentTheme;
        DataDirectory = paths.DataDirectory;
        LogsDirectory = paths.LogsDirectory;
        _initialized = true;
    }

    public override string Title => "Settings";

    public override string Glyph => "⚙";

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    [ObservableProperty]
    public partial AppTheme SelectedTheme { get; set; }

    /// <summary>Where analysis results are stored locally (local-first).</summary>
    public string DataDirectory { get; }

    /// <summary>Where the application log files are written (CORE-007).</summary>
    public string LogsDirectory { get; }

    // The initial value comes from the theme service; only user changes are applied and stored.
    partial void OnSelectedThemeChanged(AppTheme value)
    {
        if (_initialized)
        {
            _ = ChangeThemeAsync(value);
        }
    }

    private async Task ChangeThemeAsync(AppTheme theme)
    {
        try
        {
            await _themeService.ChangeAsync(theme, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The theme is applied already; only remembering it failed.
            _logger.LogError(exception, "Saving the theme failed");
            _shell.Notify(NotificationSeverity.Error, "The theme could not be saved and will be reset at the next start.");
        }
    }
}
