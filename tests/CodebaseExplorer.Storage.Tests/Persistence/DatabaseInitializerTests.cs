using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Storage.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CodebaseExplorer.Storage.Tests.Persistence;

/// <summary>
/// STORE-003: startup migration with backup (DATABASE_SCHEMA.md §5), against real files in a temporary
/// data directory, wired exactly like the application (<see cref="DependencyInjection.AddStorageModule"/>).
/// </summary>
public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "codebase-explorer-storage-tests", Guid.NewGuid().ToString("N"), "data");
    private readonly SettableTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _services;

    public DatabaseInitializerTests()
    {
        var paths = Substitute.For<IApplicationPaths>();
        paths.DataDirectory.Returns(_dataDirectory);
        paths.DatabaseFilePath.Returns(DatabaseFile);

        _services = new ServiceCollection()
            .AddSingleton(paths)
            .AddSingleton<TimeProvider>(_time)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddStorageModule()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private string DatabaseFile => Path.Combine(_dataDirectory, "codebase-explorer.db");

    private string BackupDirectory => Path.Combine(_dataDirectory, "backups");

    private IDatabaseInitializer Initializer => _services.GetRequiredService<IDatabaseInitializer>();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path.GetDirectoryName(_dataDirectory)!, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public async Task FirstStart_CreatesDirectoryAndDatabase_WithoutBackup()
    {
        var result = (await Initializer.InitializeAsync(Token)).Value;

        Assert.True(result.Created);
        Assert.Contains(result.AppliedMigrations, m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Null(result.BackupPath);
        Assert.True(File.Exists(DatabaseFile));
        Assert.Equal(1L, await ScalarAsync(DatabaseFile, "SELECT count(*) FROM sqlite_master WHERE name = 'Repositories'"));
        Assert.False(Directory.Exists(BackupDirectory));
    }

    [Fact]
    public async Task SecondStart_WithCurrentSchema_ChangesNothing()
    {
        await Initializer.InitializeAsync(Token);

        var result = (await Initializer.InitializeAsync(Token)).Value;

        Assert.False(result.Created);
        Assert.Empty(result.AppliedMigrations);
        Assert.Null(result.BackupPath);
    }

    [Fact]
    public async Task OutdatedDatabase_IsBackedUpBeforeMigrating()
    {
        // A database with user data but without the current migrations (stands in for an older version).
        await CreateOutdatedDatabaseAsync();

        var result = (await Initializer.InitializeAsync(Token)).Value;

        Assert.False(result.Created);
        Assert.NotEmpty(result.AppliedMigrations);
        Assert.Equal(Path.Combine(BackupDirectory, "codebase-explorer-20261118-090000-000.db"), result.BackupPath);
        Assert.Equal("kept", await ScalarAsync(result.BackupPath!, "SELECT Value FROM UserNotes"));
        Assert.Equal(0L, await ScalarAsync(result.BackupPath!, "SELECT count(*) FROM sqlite_master WHERE name = 'Repositories'"));
        Assert.Equal("kept", await ScalarAsync(DatabaseFile, "SELECT Value FROM UserNotes")); // migration preserves other data
        Assert.Equal(1L, await ScalarAsync(DatabaseFile, "SELECT count(*) FROM sqlite_master WHERE name = 'Repositories'"));
    }

    [Fact]
    public async Task Backups_KeepOnlyTheLastThree()
    {
        Directory.CreateDirectory(BackupDirectory);
        foreach (var stamp in new[] { "20250101-000000-000", "20250201-000000-000", "20250301-000000-000" })
        {
            await File.WriteAllTextAsync(Path.Combine(BackupDirectory, $"codebase-explorer-{stamp}.db"), "old", Token);
        }

        await File.WriteAllTextAsync(Path.Combine(BackupDirectory, "unrelated.txt"), "keep", Token);
        await CreateOutdatedDatabaseAsync();

        await Initializer.InitializeAsync(Token);

        Assert.Equal(
            ["codebase-explorer-20250201-000000-000.db", "codebase-explorer-20250301-000000-000.db", "codebase-explorer-20261118-090000-000.db", "unrelated.txt"],
            Directory.EnumerateFiles(BackupDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task UnreadableDatabase_FailsWithoutChangingTheFile()
    {
        Directory.CreateDirectory(_dataDirectory);
        var garbage = Enumerable.Range(0, 4096).Select(i => (byte)(i * 7)).ToArray();
        await File.WriteAllBytesAsync(DatabaseFile, garbage, Token);

        var result = await Initializer.InitializeAsync(Token);

        Assert.True(result.IsFailure);
        Assert.Equal(DatabaseErrorCodes.InitializationFailed, result.Error.Code);
        Assert.Equal(DatabaseFile, result.Error.Details["path"]);
        SqliteConnection.ClearAllPools();
        Assert.Equal(garbage, await File.ReadAllBytesAsync(DatabaseFile, Token));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task DataDirectoryAndFiles_AreUserOnly_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes; on Windows the per-user profile ACL applies.");
        }

        await CreateOutdatedDatabaseAsync(); // also produces a backup
        File.SetUnixFileMode(DatabaseFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var result = (await Initializer.InitializeAsync(Token)).Value;

        const UnixFileMode privateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        const UnixFileMode privateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        Assert.Equal(privateDirectory, File.GetUnixFileMode(_dataDirectory));
        Assert.Equal(privateDirectory, File.GetUnixFileMode(BackupDirectory));
        Assert.Equal(privateFile, File.GetUnixFileMode(result.BackupPath!));

        // A database created by the initializer itself is user-only as well.
        SqliteConnection.ClearAllPools();
        File.Delete(DatabaseFile);
        await Initializer.InitializeAsync(Token);
        Assert.Equal(privateFile, File.GetUnixFileMode(DatabaseFile));
    }

    private async Task CreateOutdatedDatabaseAsync()
    {
        Directory.CreateDirectory(_dataDirectory);
        await using var connection = new SqliteConnection($"Data Source={DatabaseFile};Pooling=False");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; CREATE TABLE UserNotes (Value TEXT); INSERT INTO UserNotes VALUES ('kept');";
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<object?> ScalarAsync(string databaseFile, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFile};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Token);
    }
}
