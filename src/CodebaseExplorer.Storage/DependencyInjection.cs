using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Storage.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Storage;

/// <summary>
/// Registration entry point of the Storage module. Persists the architecture model in SQLite via EF Core.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the services of the Storage module: the <see cref="IDbContextFactory{TContext}"/> for
    /// the database at <see cref="IApplicationPaths.DatabaseFilePath"/> (STORE-001) and the
    /// <see cref="IDatabaseInitializer"/> that creates, backs up and migrates it at startup (STORE-003).
    /// Nothing is opened at registration; the stores (ports) follow with STORE-004/005.
    /// </summary>
    public static IServiceCollection AddStorageModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddDbContextFactory<CodebaseExplorerDbContext>((provider, options) =>
            Configure(options, ConnectionString(provider.GetRequiredService<IApplicationPaths>().DatabaseFilePath)));

        return services;
    }

    /// <summary>The connection string for the database file; pooling stays on, the cache is private.</summary>
    internal static string ConnectionString(string databaseFilePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString();

    /// <summary>Provider and interceptors shared by the application and the tests.</summary>
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseSqlite(connectionString)
            .AddInterceptors(new SqliteConnectionPragmas());
}
