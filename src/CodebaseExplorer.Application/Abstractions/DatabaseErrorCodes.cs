namespace CodebaseExplorer.Application.Abstractions;

/// <summary>Stable <see cref="Common.ApplicationError"/> codes of database initialization.</summary>
public static class DatabaseErrorCodes
{
    /// <summary>The database could not be opened, backed up or migrated.</summary>
    public const string InitializationFailed = "database.initialization-failed";

    /// <summary>The database could not be compacted (e.g. it is in use by an import); nothing was changed.</summary>
    public const string CompactionFailed = "database.compaction-failed";
}
