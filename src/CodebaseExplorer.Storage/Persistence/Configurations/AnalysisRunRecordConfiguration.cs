using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>AnalysisRuns</c> (DATABASE_SCHEMA.md §4.2).</summary>
internal sealed class AnalysisRunRecordConfiguration : IEntityTypeConfiguration<AnalysisRunRecord>
{
    public void Configure(EntityTypeBuilder<AnalysisRunRecord> builder)
    {
        builder.ToTable("AnalysisRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Status).HasConversion<string>().IsRequired();
        builder.Property(r => r.Trigger).HasConversion<string>().IsRequired();
        builder.Property(r => r.AnalyzerVersionsJson).IsRequired();
        builder.Property(r => r.StatisticsJson).IsRequired();

        builder.HasOne<RepositoryRecord>()
            .WithMany()
            .HasForeignKey(r => r.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Diagnostics)
            .WithOne()
            .HasForeignKey(d => d.AnalysisRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.RepositoryId, r.StartedAt }).HasDatabaseName("IX_AnalysisRuns_RepositoryId_StartedAt");
    }
}
