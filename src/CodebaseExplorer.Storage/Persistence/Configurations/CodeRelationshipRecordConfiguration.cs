using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>
/// Table <c>CodeRelationships</c> (DATABASE_SCHEMA.md §4.9). No foreign keys on the polymorphic endpoints
/// or the denormalized container ids; integrity comes from <c>ArchitectureModel</c> validation.
/// </summary>
internal sealed class CodeRelationshipRecordConfiguration : IEntityTypeConfiguration<CodeRelationshipRecord>
{
    public void Configure(EntityTypeBuilder<CodeRelationshipRecord> builder)
    {
        builder.ToTable("CodeRelationships");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.SourceKind).HasConversion<string>().IsRequired();
        builder.Property(r => r.TargetKind).HasConversion<string>().IsRequired();
        builder.Property(r => r.Kind).HasConversion<string>().IsRequired();
        builder.Property(r => r.Level).HasConversion<string>().IsRequired();
        builder.Property(r => r.OriginAnalyzer).IsRequired();

        builder.HasOne<RepositoryRecord>().WithMany().HasForeignKey(r => r.RepositoryId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.SourceId).HasDatabaseName("IX_CodeRelationships_SourceId");
        builder.HasIndex(r => r.TargetId).HasDatabaseName("IX_CodeRelationships_TargetId");
        builder.HasIndex(r => new { r.RepositoryId, r.Kind, r.LevelRank }).HasDatabaseName("IX_CodeRelationships_RepositoryId_Kind_LevelRank");
        builder.HasIndex(r => new { r.SourceProjectId, r.TargetProjectId }).HasDatabaseName("IX_CodeRelationships_SourceProjectId_TargetProjectId");
        builder.HasIndex(r => new { r.SourceModuleId, r.TargetModuleId }).HasDatabaseName("IX_CodeRelationships_SourceModuleId_TargetModuleId");
    }
}
