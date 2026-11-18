using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.Abstractions;

/// <summary>
/// Prepares the local database before any UI that reads data (DATABASE_SCHEMA.md §5): creates the data
/// directory and database file with user-only permissions where the OS supports it, backs up an existing
/// database before migrating it, and applies pending migrations.
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Returns what was done, or a <see cref="ErrorKind.Failure"/> error (codes in <see cref="DatabaseErrorCodes"/>);
    /// a failure never deletes or changes the existing database file.
    /// </summary>
    Task<Result<DatabaseInitialization>> InitializeAsync(CancellationToken cancellationToken);
}
