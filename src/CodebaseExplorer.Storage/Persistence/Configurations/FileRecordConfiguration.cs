using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>Files</c> (DATABASE_SCHEMA.md §4.4).</summary>
internal sealed class FileRecordConfiguration : IEntityTypeConfiguration<FileRecord>
{
    public void Configure(EntityTypeBuilder<FileRecord> builder)
    {
        builder.ToTable("Files");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.RelativePath).IsRequired();
        builder.Property(f => f.NaturalKey).IsRequired();
        builder.Property(f => f.Language).IsRequired();
        builder.Property(f => f.Classification).HasConversion<string>().IsRequired();
        builder.Property(f => f.ContentHash).IsRequired();

        builder.HasOne<RepositoryRecord>()
            .WithMany()
            .HasForeignKey(f => f.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ProjectRecord>()
            .WithMany()
            .HasForeignKey(f => f.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(f => new { f.RepositoryId, f.RelativePath }).IsUnique().HasDatabaseName("UX_Files_RepositoryId_RelativePath");
        builder.HasIndex(f => f.ProjectId).HasDatabaseName("IX_Files_ProjectId");
        builder.HasIndex(f => new { f.RepositoryId, f.Language }).HasDatabaseName("IX_Files_RepositoryId_Language");
    }
}
