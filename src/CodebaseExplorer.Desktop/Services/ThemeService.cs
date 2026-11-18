using Avalonia.Styling;
using CodebaseExplorer.Application.Settings;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.Services;

internal sealed class ThemeService(IUserSettingsStore settings, ILogger<ThemeService> logger) : IThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        string? stored;
        try
        {
            stored = await settings.GetAsync(UserSettingKeys.Theme, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "The stored theme could not be read; the system theme is used");
            stored = null;
        }

        Apply(Parse(stored));
    }

    public async Task ChangeAsync(AppTheme theme, CancellationToken cancellationToken)
    {
        Apply(theme);
        await settings.SetAsync(UserSettingKeys.Theme, Format(theme), cancellationToken);
    }

    /// <summary>Stored values (DATABASE_SCHEMA.md §4.17).</summary>
    internal static string Format(AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
        _ => "system",
    };

    internal static AppTheme Parse(string? value) => value switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => AppTheme.System,
    };

    private void Apply(AppTheme theme)
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
