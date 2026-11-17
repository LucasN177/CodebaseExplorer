using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Graph.Projection;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Graph;

/// <summary>
/// Registration entry point of the Graph module. Projects the architecture model into aggregated
/// graph DTOs (GRAPH_ENGINE.md).
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers the services of the Graph module.</summary>
    public static IServiceCollection AddGraphModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IGraphProjector, GraphProjector>();

        return services;
    }
}
