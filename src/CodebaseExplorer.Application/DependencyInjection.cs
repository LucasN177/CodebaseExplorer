using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Application;

/// <summary>
/// Registration entry point of the Application layer (use cases and ports).
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the application use cases. Ports (e.g. <see cref="IArchitectureModelStore"/>) are implemented and
    /// registered by the technical modules.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Singletons: ImportLocalRepository tracks running imports across calls.
        services.AddSingleton<ImportLocalRepository>();
        services.AddSingleton<Git.ImportRemoteRepository>();
        services.AddSingleton<ListRecentRepositories>();
        services.AddSingleton<OpenRepository>();
        services.AddSingleton<RemoveRepository>();
        services.AddSingleton<GetRunHistory>();
        services.AddSingleton<GetRepositoryOverview>();
        services.AddSingleton<Source.GetSourceSnippet>();
        services.AddSingleton<Inspector.GetElementDetails>();
        services.AddSingleton<Search.SearchElements>();
        services.AddSingleton<GetGraphView>();
        services.AddSingleton<GetGraphEdgeDetails>();
        services.AddSingleton<Export.ExportArchitectureReport>();
        services.AddSingleton<AI.AiSettingsStore>();
        services.AddSingleton<AI.EnsureSearchIndex>();

        return services;
    }
}
