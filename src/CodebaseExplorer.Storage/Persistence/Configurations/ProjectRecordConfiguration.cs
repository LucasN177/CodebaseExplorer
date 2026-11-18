using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>Projects</c> (DATABASE_SCHEMA.md §4.5).</summary>
internal sealed class ProjectRecordConfiguration : IEntityTypeConfiguration<ProjectRecord>
{
    public void Configure(EntityTypeBuilder<ProjectRecord> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).IsRequired();
        builder.Property(p => p.NaturalKey).IsRequired();
        builder.Property(p => p.Kind).HasConversion<string>().IsRequired();
        builder.Property(p => p.Ecosystem).HasConversion<string>().IsRequired();
        builder.Property(p => p.PrimaryLanguage).IsRequired();
        builder.Property(p => p.RootDirectory).IsRequired();
        builder.Property(p => p.PropertiesJson).IsRequired();
        builder.Property(p => p.SolutionMembershipsJson).IsRequired();

        builder.HasOne<RepositoryRecord>()
            .WithMany()
            .HasForeignKey(p => p.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(p => p.ProjectFileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => new { p.RepositoryId, p.Ecosystem }).HasDatabaseName("IX_Projects_RepositoryId_Ecosystem");
        builder.HasIndex(p => new { p.RepositoryId, p.Name }).HasDatabaseName("IX_Projects_RepositoryId_Name");
    }
}
