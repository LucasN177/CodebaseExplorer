namespace CodebaseExplorer.Desktop.Services;

/// <summary>The theme modes the user can choose. <see cref="System"/> follows the OS setting.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Switches the Fluent theme variant. Keeps <c>Application.Current</c> access out of ViewModels.
/// </summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    void Apply(AppTheme theme);
}
