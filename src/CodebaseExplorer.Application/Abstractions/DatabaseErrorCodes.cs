namespace CodebaseExplorer.Application.Abstractions;

/// <summary>Stable <see cref="Common.ApplicationError"/> codes of database initialization.</summary>
public static class DatabaseErrorCodes
{
    /// <summary>The database could not be opened, backed up or migrated.</summary>
    public const string InitializationFailed = "database.initialization-failed";
}
