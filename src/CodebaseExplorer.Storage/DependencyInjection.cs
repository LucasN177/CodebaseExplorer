using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Storage;

/// <summary>
/// Registration entry point of the Storage module. Persists the architecture model in SQLite via EF Core.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the services of the Storage module. The module has no implementations yet;
    /// they are added by the tasks referenced in docs/architecture/DATABASE_SCHEMA.md.
    /// </summary>
    public static IServiceCollection AddStorageModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
