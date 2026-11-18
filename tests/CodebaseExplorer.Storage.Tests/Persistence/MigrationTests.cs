using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Tests.Persistence;

/// <summary>STORE-003: the migrations create the current schema and match the model.</summary>
public sealed class MigrationTests : IDisposable
{
    private readonly TestDatabase _database = new(); // migrates an empty database to the latest version

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task EmptyDatabase_IsMigratedToTheLatestVersion()
    {
        await using var context = _database.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Contains(context.Database.GetMigrations(), m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Model_HasNoChangesWithoutMigration()
    {
        // Fails when the model was changed without `dotnet ef migrations add` (DEVELOPMENT_GUIDE.md §6).
        await using var context = _database.CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task UserSettingsMigration_CreatesTheSettingsTable()
    {
        // STORE-005: second migration; Key is the primary key, all columns are required.
        await using var context = _database.CreateContext();

        Assert.Contains(context.Database.GetMigrations(), m => m.EndsWith("_AddUserSettings", StringComparison.Ordinal));
        Assert.Equal(1L, await _database.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'UserSettings'"));
        Assert.Equal(3L, await _database.ScalarAsync("SELECT count(*) FROM pragma_table_info('UserSettings') WHERE \"notnull\" = 1 OR pk = 1"));
        Assert.Equal("Key", await _database.ScalarAsync("SELECT name FROM pragma_table_info('UserSettings') WHERE pk = 1"));
    }
}
