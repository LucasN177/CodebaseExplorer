namespace CodebaseExplorer.Desktop.Services;

/// <summary>The theme modes the user can choose. <see cref="System"/> follows the OS setting.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Switches the Fluent theme variant and persists the choice (STORE-005). Keeps <c>Application.Current</c>
/// access out of ViewModels.
/// </summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    /// <summary>
    /// Applies the stored theme choice; a missing, unknown or unreadable setting means <see cref="AppTheme.System"/>.
    /// Called once at startup before the main window is shown. Never throws for storage errors (they are logged).
    /// </summary>
    Task RestoreAsync(CancellationToken cancellationToken);

    /// <summary>Applies a theme immediately and stores it; storage errors propagate after the theme was applied.</summary>
    Task ChangeAsync(AppTheme theme, CancellationToken cancellationToken);
}
