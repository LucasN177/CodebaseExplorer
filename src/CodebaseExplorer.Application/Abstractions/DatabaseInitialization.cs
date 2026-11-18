namespace CodebaseExplorer.Application.Abstractions;

/// <summary>Outcome of <see cref="IDatabaseInitializer.InitializeAsync"/>.</summary>
/// <param name="Created">The database file did not exist (or was empty) and was created.</param>
/// <param name="AppliedMigrations">Migrations applied in this start, oldest first; empty if the schema was current.</param>
/// <param name="BackupPath">Copy of the previous database taken before migrating, or <see langword="null"/> if none was needed.</param>
public sealed record DatabaseInitialization(bool Created, IReadOnlyList<string> AppliedMigrations, string? BackupPath);
