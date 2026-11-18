using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>GitChanges</c> (DATABASE_SCHEMA.md §4.13).</summary>
internal sealed class GitChangeRecordConfiguration : IEntityTypeConfiguration<GitChangeRecord>
{
    public void Configure(EntityTypeBuilder<GitChangeRecord> builder)
    {
        builder.ToTable("GitChanges");
        builder.HasKey(c => new { c.GitSnapshotId, c.FileId });

        builder.HasOne<GitSnapshotRecord>()
            .WithMany()
            .HasForeignKey(c => c.GitSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(c => c.FileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.FileId).HasDatabaseName("IX_GitChanges_FileId");
    }
}
