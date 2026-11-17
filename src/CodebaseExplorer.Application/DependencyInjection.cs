using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Application;

/// <summary>
/// Registration entry point of the Application layer (use cases and ports).
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers application use cases. Use cases are added by the tasks in
    /// docs/implementation/TASK_BREAKDOWN.md; none exist yet.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
