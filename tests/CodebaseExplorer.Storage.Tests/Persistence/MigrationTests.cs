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
}
