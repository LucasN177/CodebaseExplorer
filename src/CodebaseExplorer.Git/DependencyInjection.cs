using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Git;

/// <summary>
/// Registration entry point of the Git module. Reads local repositories and clones remotes.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the services of the Git module. The module has no implementations yet;
    /// they are added by the tasks referenced in docs/architecture/GIT_INTEGRATION.md.
    /// </summary>
    public static IServiceCollection AddGitModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
