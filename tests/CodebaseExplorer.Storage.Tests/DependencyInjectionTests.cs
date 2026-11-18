using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Storage.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace CodebaseExplorer.Storage.Tests;

/// <summary>STORE-001: the module registers a context factory for the database file of <see cref="IApplicationPaths"/>.</summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task AddStorageModule_RegistersAContextFactoryForTheDatabaseFile()
    {
        var databaseFile = Path.Combine(Path.GetTempPath(), "codebase-explorer-storage-tests", Guid.NewGuid().ToString("N"), "app.db");
        var paths = Substitute.For<IApplicationPaths>();
        paths.DatabaseFilePath.Returns(databaseFile);

        await using var provider = new ServiceCollection()
            .AddSingleton(paths)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>))
            .AddStorageModule()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var factory = provider.GetRequiredService<IDbContextFactory<CodebaseExplorerDbContext>>();
        await using var context = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        Assert.Equal(databaseFile, new SqliteConnectionStringBuilder(context.Database.GetConnectionString()).DataSource);
        Assert.False(File.Exists(databaseFile)); // nothing is opened or created at registration (STORE-003 migrates)
    }
}
