using Avalonia.Styling;

namespace CodebaseExplorer.Desktop.Services;

internal sealed class ThemeService : IThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public void Apply(AppTheme theme)
    {
        if (Avalonia.Application.Current is { } application)
        {
            application.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }

        CurrentTheme = theme;
    }
}
