using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Desktop.Composition;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels;
using CodebaseExplorer.Desktop.ViewModels.Pages;
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
        InstallExceptionHandlers(_services.GetRequiredService<GlobalExceptionHandler>());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // No window exists while the database is prepared; the app ends when the first shown window closes.
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            desktop.Exit += (_, _) => _services.Dispose();
            _ = StartAsync(desktop, _services);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Global exception handling (CORE-007): log everything, tell the user, keep the UI alive where possible.</summary>
    private static void InstallExceptionHandlers(GlobalExceptionHandler handler)
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            handler.HandleUiException(e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => handler.HandleFatalException(e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            handler.HandleUnobservedTaskException(e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// Prepares the database before any window that reads data (DATABASE_SCHEMA.md §5), then shows the main
    /// window — or, if that failed, the startup error window.
    /// </summary>
    private static async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, ServiceProvider services)
    {
        var paths = services.GetRequiredService<IApplicationPaths>();
        Window window;
        try
        {
            var initializer = services.GetRequiredService<IDatabaseInitializer>();
            var result = await Task.Run(() => initializer.InitializeAsync(CancellationToken.None));

            if (result.TryGetValue(out _, out var error))
            {
                // Before the first window, so it opens in the stored theme (STORE-005).
                await services.GetRequiredService<IThemeService>().RestoreAsync(CancellationToken.None);
                // The graph view receives the stored layout before any graph (GRAPH-005).
                await services.GetRequiredService<ArchitectureExplorerPageViewModel>().RestoreLayoutAsync(CancellationToken.None);
                window = new MainWindow { DataContext = services.GetRequiredService<MainWindowViewModel>() };
                services.GetRequiredService<GlobalExceptionHandler>().ReportPreviousUnexpectedExit();
            }
            else
            {
                window = new StartupErrorWindow { DataContext = StartupErrorViewModel.DatabaseFailed(error.Message, paths) };
            }
        }
        catch (Exception exception)
        {
            // Startup runs outside the dispatcher's exception handling (fire-and-forget task).
            services.GetRequiredService<GlobalExceptionHandler>().HandleStartupException(exception);
            window = new StartupErrorWindow { DataContext = StartupErrorViewModel.Unexpected(paths) };
        }

        desktop.MainWindow = window;
        window.Show();
    }
}
