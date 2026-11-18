using CodebaseExplorer.AI;
using CodebaseExplorer.Analysis;
using CodebaseExplorer.Application;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using CodebaseExplorer.Git;
using CodebaseExplorer.Graph;
using CodebaseExplorer.Infrastructure;
using CodebaseExplorer.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.Composition;

/// <summary>
/// The single place where modules are wired together (see docs/architecture/SYSTEM_ARCHITECTURE.md,
/// "Application composition root"). No other project builds a service provider.
/// </summary>
public static class CompositionRoot
{
    /// <param name="overrides">Registrations applied last (later registrations win); used by tests to point the
    /// application at a temporary data directory. The application itself passes nothing.</param>
    public static ServiceProvider BuildServiceProvider(Action<IServiceCollection>? overrides = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            // EF Core logs every SQL command at information level; only its warnings and errors are of interest.
            logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            logging.AddDebug();
        });

        services
            .AddApplication()
            .AddInfrastructure()
            .AddStorageModule()
            .AddAnalysisModule()
            .AddGraphModule()
            .AddGitModule()
            .AddAiModule()
            .AddDesktop();

        overrides?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static IServiceCollection AddDesktop(this IServiceCollection services)
    {
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IFolderPicker, FolderPicker>();
        services.AddSingleton<RepositorySession>();
        services.AddSingleton<ShellStatus>();
        services.AddSingleton<GlobalExceptionHandler>();

        // One graph web view in the application; the bridge is bound to GraphHostControl.Host and
        // ViewModels use it through IGraphViewHost (GRAPH-004).
        services.AddSingleton<GraphViewBridge>();
        services.AddSingleton<IGraphViewHost>(provider => provider.GetRequiredService<GraphViewBridge>());

        services.AddSingleton<StartPageViewModel>();
        services.AddSingleton<OverviewPageViewModel>();
        services.AddSingleton<ArchitectureExplorerPageViewModel>();
        services.AddSingleton<RunHistoryPageViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services;
    }
}
