using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Infrastructure.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Infrastructure;

/// <summary>
/// Registration entry point for cross-cutting technical services (file system, paths, clock, secrets).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApplicationPaths, ApplicationPaths>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
