using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Desktop.Composition;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels;
using CodebaseExplorer.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Desktop;

public sealed partial class App : Avalonia.Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _services = CompositionRoot.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // No window exists while the database is prepared; the app ends when the first shown window closes.
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            desktop.Exit += (_, _) => _services.Dispose();
            _ = StartAsync(desktop, _services);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Prepares the database before any window that reads data (DATABASE_SCHEMA.md §5), then shows the main
    /// window — or, if that failed, the startup error window.
    /// </summary>
    private static async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, ServiceProvider services)
    {
        var initializer = services.GetRequiredService<IDatabaseInitializer>();
        var result = await Task.Run(() => initializer.InitializeAsync(CancellationToken.None));

        Window window;
        if (result.TryGetValue(out _, out var error))
        {
            // Before the first window, so it opens in the stored theme (STORE-005).
            await services.GetRequiredService<IThemeService>().RestoreAsync(CancellationToken.None);
            window = new MainWindow { DataContext = services.GetRequiredService<MainWindowViewModel>() };
        }
        else
        {
            var paths = services.GetRequiredService<IApplicationPaths>();
            window = new StartupErrorWindow
            {
                DataContext = new StartupErrorViewModel(error.Message, paths.DatabaseFilePath, Path.Combine(paths.DataDirectory, "backups")),
            };
        }

        desktop.MainWindow = window;
        window.Show();
    }
}
