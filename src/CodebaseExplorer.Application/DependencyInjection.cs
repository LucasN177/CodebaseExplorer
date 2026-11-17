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
    /// Registers the application use cases and the session model cache. Ports are implemented and
    /// registered by the technical modules.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Session-scoped model storage for M1 (replaced by the SQLite model store in STORE-004).
        services.AddSingleton<IArchitectureModelCache, InMemoryArchitectureModelCache>();

        // Singletons: ImportLocalRepository tracks running imports across calls.
        services.AddSingleton<ImportLocalRepository>();
        services.AddSingleton<GetGraphView>();

        return services;
    }
}
