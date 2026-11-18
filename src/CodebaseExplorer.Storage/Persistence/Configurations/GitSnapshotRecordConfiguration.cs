using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>GitSnapshots</c> (DATABASE_SCHEMA.md §4.12).</summary>
internal sealed class GitSnapshotRecordConfiguration : IEntityTypeConfiguration<GitSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<GitSnapshotRecord> builder)
    {
        builder.ToTable("GitSnapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.HeadCommitSha).IsRequired();
        builder.Property(s => s.RemotesJson).IsRequired();

        builder.HasOne<RepositoryRecord>()
            .WithMany()
            .HasForeignKey(s => s.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AnalysisRunRecord>()
            .WithOne()
            .HasForeignKey<GitSnapshotRecord>(s => s.AnalysisRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.AnalysisRunId).IsUnique().HasDatabaseName("UX_GitSnapshots_AnalysisRunId");
        builder.HasIndex(s => new { s.RepositoryId, s.CapturedAt }).HasDatabaseName("IX_GitSnapshots_RepositoryId_CapturedAt");
    }
}
