using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Infrastructure.FileSystem;
using CodebaseExplorer.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Infrastructure;

/// <summary>
/// Registration entry point for cross-cutting technical services (file system, paths, clock, file logging, secrets).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApplicationPaths, ApplicationPaths>();
        services.AddSingleton(TimeProvider.System);

        // Picked up by the logger factory like every registered ILoggerProvider (CORE-007).
        services.AddSingleton<FileLoggerProvider>();
        services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<FileLoggerProvider>());

        return services;
    }
}
