using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.AI;

/// <summary>
/// Registration entry point of the AI module. Optional, disabled by default; local-first AI provider abstraction.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the services of the AI module. The module has no implementations yet;
    /// they are added by the tasks referenced in docs/architecture/AI_ARCHITECTURE.md.
    /// </summary>
    public static IServiceCollection AddAiModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
