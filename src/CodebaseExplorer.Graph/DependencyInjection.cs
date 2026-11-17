using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Graph;

/// <summary>
/// Registration entry point of the Graph module. Projects the architecture model into aggregated graph DTOs.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the services of the Graph module. The module has no implementations yet;
    /// they are added by the tasks referenced in docs/architecture/GRAPH_ENGINE.md.
    /// </summary>
    public static IServiceCollection AddGraphModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
