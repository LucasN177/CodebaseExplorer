using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Analysis;

/// <summary>
/// Registration entry point of the Analysis module. Hosts the analyzer pipeline and the language analyzers.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the services of the Analysis module. The module has no implementations yet;
    /// they are added by the tasks referenced in docs/architecture/ANALYSIS_ENGINE.md.
    /// </summary>
    public static IServiceCollection AddAnalysisModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
