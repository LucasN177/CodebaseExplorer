using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Git.LibGit2;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Git;

/// <summary>
/// Registration entry point of the Git module. Reads local repositories and clones remotes (GIT_INTEGRATION.md).
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers the services of the Git module (LibGit2Sharp adapter).</summary>
    public static IServiceCollection AddGitModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IGitRepositoryReader, LibGit2RepositoryReader>();
        services.AddSingleton<IGitRemoteCloner, Cloning.LibGit2RemoteCloner>();

        return services;
    }
}
