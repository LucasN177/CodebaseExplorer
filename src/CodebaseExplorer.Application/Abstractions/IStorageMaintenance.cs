using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.Abstractions;

/// <summary>
/// Housekeeping of the local database (Settings page): how much disk space it uses and compacting it. Removing a
/// repository deletes its rows, but SQLite keeps the freed pages inside the file for reuse; compacting returns them to
/// the file system (DATABASE_SCHEMA.md §5).
/// </summary>
public interface IStorageMaintenance
{
    /// <summary>Current sizes of the database files and the backups; missing files count as zero.</summary>
    StorageUsage GetUsage();

    /// <summary>
    /// Rebuilds the database file without unused pages (<c>VACUUM</c>) and truncates the write-ahead log. Fails with
    /// <see cref="DatabaseErrorCodes.CompactionFailed"/> e.g. while an import writes to the database; the data is
    /// never changed.
    /// </summary>
    Task<Result<StorageCompaction>> CompactAsync(CancellationToken cancellationToken);
}

/// <summary>Disk usage of the local data.</summary>
/// <param name="DatabaseBytes">The database file plus its write-ahead log and shared-memory file.</param>
/// <param name="BackupBytes">Backups made before migrations (<c>&lt;data&gt;/backups</c>).</param>
public sealed record StorageUsage(long DatabaseBytes, long BackupBytes);

/// <summary>Database size before and after compacting.</summary>
public sealed record StorageCompaction(long BytesBefore, long BytesAfter)
{
    public long BytesFreed => Math.Max(0, BytesBefore - BytesAfter);
}
