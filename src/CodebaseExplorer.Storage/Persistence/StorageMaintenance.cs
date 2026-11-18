using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Storage.Persistence;

/// <summary>
/// SQLite implementation of <see cref="IStorageMaintenance"/>: sizes from the file system, compaction with
/// <c>VACUUM</c> followed by <c>PRAGMA wal_checkpoint(TRUNCATE)</c> (in WAL mode the rebuilt pages pass through the
/// log, which the checkpoint writes back and empties).
/// </summary>
internal sealed class StorageMaintenance(
    IDbContextFactory<CodebaseExplorerDbContext> contextFactory,
    IApplicationPaths paths,
    ILogger<StorageMaintenance> logger) : IStorageMaintenance
{
    private static readonly string[] DatabaseFileSuffixes = [string.Empty, "-wal", "-shm"];

    public StorageUsage GetUsage()
    {
        var database = DatabaseFileSuffixes.Sum(suffix => SizeOf(paths.DatabaseFilePath + suffix));
        var backups = Path.Combine(paths.DataDirectory, "backups");
        var backupBytes = Directory.Exists(backups)
            ? new DirectoryInfo(backups).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;
        return new StorageUsage(database, backupBytes);
    }

    public async Task<Result<StorageCompaction>> CompactAsync(CancellationToken cancellationToken)
    {
        var before = GetUsage().DatabaseBytes;
        try
        {
            await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken))
            {
                // VACUUM must not run inside a transaction; EF executes raw SQL without one unless one is open.
                await context.Database.ExecuteSqlRawAsync("VACUUM;", cancellationToken);
                await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
            }

            var after = GetUsage().DatabaseBytes;
            logger.LogInformation("Compacted the database from {BytesBefore} to {BytesAfter} bytes", before, after);
            return new StorageCompaction(before, after);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Compacting the database failed");
            return ApplicationError.Failure(DatabaseErrorCodes.CompactionFailed,
                $"The database could not be compacted (is an analysis running?): {exception.Message}");
        }
    }

    private static long SizeOf(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
}
