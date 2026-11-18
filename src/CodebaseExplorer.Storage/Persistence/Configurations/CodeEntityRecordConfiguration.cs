using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>CodeEntities</c> (DATABASE_SCHEMA.md §4.7).</summary>
internal sealed class CodeEntityRecordConfiguration : IEntityTypeConfiguration<CodeEntityRecord>
{
    public void Configure(EntityTypeBuilder<CodeEntityRecord> builder)
    {
        builder.ToTable("CodeEntities");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Name).IsRequired();
        builder.Property(e => e.QualifiedName).IsRequired();
        builder.Property(e => e.NaturalKey).IsRequired();
        builder.Property(e => e.SymbolKey).IsRequired();
        builder.Property(e => e.Kind).HasConversion<string>().IsRequired();
        builder.Property(e => e.Language).IsRequired();
        builder.Property(e => e.Visibility).HasConversion<string>().IsRequired();
        builder.Property(e => e.Modifiers).HasConversion<int>().IsRequired();

        builder.HasOne<RepositoryRecord>().WithMany().HasForeignKey(e => e.RepositoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProjectRecord>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ModuleRecord>().WithMany().HasForeignKey(e => e.ModuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CodeEntityRecord>().WithMany().HasForeignKey(e => e.ContainingEntityId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Declarations)
            .WithOne()
            .HasForeignKey(d => d.EntityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.ModuleId).HasDatabaseName("IX_CodeEntities_ModuleId");
        builder.HasIndex(e => e.ContainingEntityId).HasDatabaseName("IX_CodeEntities_ContainingEntityId");
        builder.HasIndex(e => new { e.RepositoryId, e.Name }).HasDatabaseName("IX_CodeEntities_RepositoryId_Name");
        builder.HasIndex(e => new { e.ProjectId, e.Kind }).HasDatabaseName("IX_CodeEntities_ProjectId_Kind");
    }
}
