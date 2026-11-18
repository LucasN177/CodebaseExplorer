using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>ExternalDependencies</c> (DATABASE_SCHEMA.md §4.11).</summary>
internal sealed class ExternalDependencyRecordConfiguration : IEntityTypeConfiguration<ExternalDependencyRecord>
{
    public void Configure(EntityTypeBuilder<ExternalDependencyRecord> builder)
    {
        builder.ToTable("ExternalDependencies");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Ecosystem).HasConversion<string>().IsRequired();
        builder.Property(d => d.Name).IsRequired();
        builder.Property(d => d.DisplayName).IsRequired();
        builder.Property(d => d.NaturalKey).IsRequired();

        builder.HasOne<RepositoryRecord>().WithMany().HasForeignKey(d => d.RepositoryId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => new { d.RepositoryId, d.Ecosystem, d.Name }).IsUnique().HasDatabaseName("UX_ExternalDependencies_RepositoryId_Ecosystem_Name");
    }
}
