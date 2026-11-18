using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>Modules</c> (DATABASE_SCHEMA.md §4.6).</summary>
internal sealed class ModuleRecordConfiguration : IEntityTypeConfiguration<ModuleRecord>
{
    public void Configure(EntityTypeBuilder<ModuleRecord> builder)
    {
        builder.ToTable("Modules");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Name).IsRequired();
        builder.Property(m => m.QualifiedName).IsRequired();
        builder.Property(m => m.NaturalKey).IsRequired();
        builder.Property(m => m.Kind).HasConversion<string>().IsRequired();

        builder.HasOne<RepositoryRecord>()
            .WithMany()
            .HasForeignKey(m => m.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ProjectRecord>()
            .WithMany()
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ModuleRecord>()
            .WithMany()
            .HasForeignKey(m => m.ParentModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.ProjectId).HasDatabaseName("IX_Modules_ProjectId");
        builder.HasIndex(m => m.ParentModuleId).HasDatabaseName("IX_Modules_ParentModuleId");
        builder.HasIndex(m => new { m.RepositoryId, m.QualifiedName }).HasDatabaseName("IX_Modules_RepositoryId_QualifiedName");
    }
}
