using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>Repositories</c> (DATABASE_SCHEMA.md §4.1).</summary>
internal sealed class RepositoryRecordConfiguration : IEntityTypeConfiguration<RepositoryRecord>
{
    public void Configure(EntityTypeBuilder<RepositoryRecord> builder)
    {
        builder.ToTable("Repositories");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).IsRequired();
        builder.Property(r => r.LocalPath).IsRequired();
        builder.Property(r => r.SourceKind).HasConversion<string>().IsRequired();

        builder.HasIndex(r => r.LocalPath).IsUnique().HasDatabaseName("UX_Repositories_LocalPath");
        builder.HasIndex(r => r.LastOpenedAt).HasDatabaseName("IX_Repositories_LastOpenedAt");

        // The run of the current model; cleared when that run row is deleted.
        builder.HasOne<AnalysisRunRecord>()
            .WithMany()
            .HasForeignKey(r => r.LastAnalysisRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
