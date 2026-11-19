using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodebaseExplorer.Storage.Persistence;

/// <summary>
/// EF Core context of the Codebase Explorer database (DATABASE_SCHEMA.md, ADR-005). Holds the
/// persistence records (<c>Persistence/Entities</c>), which never leave the Storage module.
/// </summary>
/// <remarks>
/// Conventions: every <see cref="Guid"/> column is a 16-byte BLOB (EF's SQLite default would be TEXT);
/// enums are stored as TEXT (configured per property); timestamps are fixed-width ISO-8601 UTC TEXT
/// (<see cref="UtcIsoTextConverter"/>), so they can be ordered in SQL.
/// </remarks>
internal sealed class CodebaseExplorerDbContext(DbContextOptions<CodebaseExplorerDbContext> options) : DbContext(options)
{
    public DbSet<RepositoryRecord> Repositories => Set<RepositoryRecord>();

    public DbSet<AnalysisRunRecord> AnalysisRuns => Set<AnalysisRunRecord>();

    public DbSet<AnalysisDiagnosticRecord> AnalysisDiagnostics => Set<AnalysisDiagnosticRecord>();

    public DbSet<FileRecord> Files => Set<FileRecord>();

    public DbSet<ProjectRecord> Projects => Set<ProjectRecord>();

    public DbSet<ModuleRecord> Modules => Set<ModuleRecord>();

    public DbSet<CodeEntityRecord> CodeEntities => Set<CodeEntityRecord>();

    public DbSet<CodeEntityDeclarationRecord> CodeEntityDeclarations => Set<CodeEntityDeclarationRecord>();

    public DbSet<ExternalDependencyRecord> ExternalDependencies => Set<ExternalDependencyRecord>();

    public DbSet<CodeRelationshipRecord> CodeRelationships => Set<CodeRelationshipRecord>();

    public DbSet<AnalysisEvidenceRecord> AnalysisEvidence => Set<AnalysisEvidenceRecord>();

    public DbSet<UserSettingRecord> UserSettings => Set<UserSettingRecord>();

    public DbSet<GitSnapshotRecord> GitSnapshots => Set<GitSnapshotRecord>();

    public DbSet<GitChangeRecord> GitChanges => Set<GitChangeRecord>();

    public DbSet<SearchIndexStateRecord> SearchIndexStates => Set<SearchIndexStateRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Guid>().HaveConversion<GuidToBytesConverter>();
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcIsoTextConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CodebaseExplorerDbContext).Assembly);
    }
}
