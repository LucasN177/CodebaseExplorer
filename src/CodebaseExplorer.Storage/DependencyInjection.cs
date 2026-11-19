using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Application.Settings;
using CodebaseExplorer.Storage.Persistence;
using CodebaseExplorer.Storage.Stores;
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
    /// the database at <see cref="IApplicationPaths.DatabaseFilePath"/> (STORE-001), the
    /// <see cref="IDatabaseInitializer"/> that creates, backs up and migrates it at startup (STORE-003),
    /// the <see cref="IArchitectureModelStore"/> (STORE-004), the <see cref="IUserSettingsStore"/> (STORE-005), the <see cref="IGitActivityStore"/> (GIT-005) and the <see cref="IStorageMaintenance"/> (Settings: size, compaction). Nothing is opened at registration.
    /// </summary>
    public static IServiceCollection AddStorageModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IArchitectureModelStore, ArchitectureModelStore>();
        services.AddSingleton<IUserSettingsStore, UserSettingsStore>();
        services.AddSingleton<IGitActivityStore, GitActivityStore>();
        services.AddSingleton<IStorageMaintenance, StorageMaintenance>();
        services.AddSingleton<Application.AI.ISearchIndexStore, SearchIndexStore>();
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
