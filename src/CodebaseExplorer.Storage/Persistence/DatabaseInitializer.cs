using System.Globalization;
using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Storage.Persistence;

/// <summary>
/// Startup migration (STORE-003, DATABASE_SCHEMA.md §5): creates the data directory and database file with
/// user-only permissions (Unix 0700/0600; on Windows the per-user profile ACL applies), backs up an existing
/// database before pending migrations are applied (keeps the last <see cref="BackupsToKeep"/>), then migrates.
/// </summary>
/// <remarks>
/// The backup uses SQLite's online backup API instead of a file copy: in WAL mode recent changes may still
/// live in the <c>-wal</c> file, which a plain copy of the main file would miss.
/// </remarks>
internal sealed class DatabaseInitializer(
    IDbContextFactory<CodebaseExplorerDbContext> contextFactory,
    IApplicationPaths paths,
    TimeProvider timeProvider,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public const int BackupsToKeep = 3;
    public const string BackupFilePrefix = "codebase-explorer-";

    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Where backups are written: <c>&lt;data&gt;/backups</c>.</summary>
    public string BackupDirectory => Path.Combine(paths.DataDirectory, "backups");

    public async Task<Result<DatabaseInitialization>> InitializeAsync(CancellationToken cancellationToken)
    {
        var databaseFile = paths.DatabaseFilePath;
        try
        {
            EnsurePrivateDirectory(paths.DataDirectory);
            EnsurePrivateDirectory(Path.GetDirectoryName(databaseFile)!);

            // The file is created by SQLite on first open. Pre-creating an empty file (to set its mode first)
            // makes EF Core's SQLite migrator fail with "attempt to write a readonly database" (found in
            // STORE-003); the 0700 data directory already keeps other users out, and the file modes are
            // tightened right after opening.
            var existed = File.Exists(databaseFile) && new FileInfo(databaseFile).Length > 0;

            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            string? backup = null;
            if (existed && pending.Count > 0)
            {
                backup = Backup(databaseFile);
                logger.LogInformation("Backed up the database before applying {MigrationCount} migrations", pending.Count);
            }

            if (pending.Count > 0)
            {
                await context.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Applied {MigrationCount} database migrations", pending.Count);
            }

            RestrictDatabaseFiles(databaseFile);
            return new DatabaseInitialization(!existed, pending, backup);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Opening, backing up or migrating failed; the existing file is left as it is.
            logger.LogError(exception, "The database could not be initialized");
            return ApplicationError.Failure(DatabaseErrorCodes.InitializationFailed,
                $"The local database could not be prepared: {exception.Message}",
                new Dictionary<string, string> { ["path"] = databaseFile });
        }
    }

    private string Backup(string databaseFile)
    {
        EnsurePrivateDirectory(BackupDirectory);

        var stamp = timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var target = Path.Combine(BackupDirectory, $"{BackupFilePrefix}{stamp}.db");
        CreatePrivateFile(target);

        using (var source = new SqliteConnection(DependencyInjection.ConnectionString(databaseFile)))
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString()))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }

        foreach (var old in Directory.EnumerateFiles(BackupDirectory, $"{BackupFilePrefix}*.db")
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                     .Skip(BackupsToKeep))
        {
            File.Delete(old);
        }

        return target;
    }

    private static void EnsurePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory, PrivateDirectoryMode);
        }

        File.SetUnixFileMode(directory, PrivateDirectoryMode);
    }

    /// <summary>Sets 0600 on the database file and its WAL/shared-memory companions (Unix only).</summary>
    private static void RestrictDatabaseFiles(string databaseFile)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var file in new[] { databaseFile, databaseFile + "-wal", databaseFile + "-shm" }.Where(File.Exists))
        {
            File.SetUnixFileMode(file, PrivateFileMode);
        }
    }

    private static void CreatePrivateFile(string file)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        using var stream = new FileStream(file, options);
    }
}
