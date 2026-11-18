using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>AnalysisEvidence</c> (DATABASE_SCHEMA.md §4.10).</summary>
internal sealed class AnalysisEvidenceRecordConfiguration : IEntityTypeConfiguration<AnalysisEvidenceRecord>
{
    public void Configure(EntityTypeBuilder<AnalysisEvidenceRecord> builder)
    {
        builder.ToTable("AnalysisEvidence");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.AnalyzerId).IsRequired();
        builder.Property(e => e.AnalyzerVersion).IsRequired();
        builder.Property(e => e.Level).HasConversion<string>().IsRequired();
        builder.Property(e => e.Rule).IsRequired();

        builder.HasOne<RepositoryRecord>().WithMany().HasForeignKey(e => e.RepositoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CodeRelationshipRecord>().WithMany().HasForeignKey(e => e.RelationshipId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AnalysisRunRecord>().WithMany().HasForeignKey(e => e.AnalysisRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FileRecord>().WithMany().HasForeignKey(e => e.FileId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.RelationshipId).HasDatabaseName("IX_AnalysisEvidence_RelationshipId");
        builder.HasIndex(e => e.FileId).HasDatabaseName("IX_AnalysisEvidence_FileId");
    }
}
