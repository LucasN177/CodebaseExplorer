using CodebaseExplorer.Storage.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Tests;

/// <summary>
/// A real SQLite database file in a temporary folder, configured exactly like the application
/// (<see cref="DependencyInjection"/>: provider, connection string, pragma interceptor). The schema is
/// created with <c>EnsureCreated</c> until migrations exist (STORE-003).
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "codebase-explorer-storage-tests", Guid.NewGuid().ToString("N"));

    public TestDatabase()
    {
        Directory.CreateDirectory(_directory);
        FilePath = Path.Combine(_directory, "test.db");

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public string FilePath { get; }

    public CodebaseExplorerDbContext CreateContext() =>
        new(DependencyInjection.Configure(new DbContextOptionsBuilder<CodebaseExplorerDbContext>(), DependencyInjection.ConnectionString(FilePath))
            .Options as DbContextOptions<CodebaseExplorerDbContext>
            ?? throw new InvalidOperationException("Unexpected options type."));

    /// <summary>Runs a scalar query on a fresh connection that goes through EF (and therefore the pragmas).</summary>
    public async Task<object?> ScalarAsync(string sql)
    {
        await using var context = CreateContext();
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a lingering handle must not fail the test.
        }
    }
}
