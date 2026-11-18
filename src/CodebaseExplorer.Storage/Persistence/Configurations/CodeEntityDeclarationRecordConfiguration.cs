using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>CodeEntityDeclarations</c> (DATABASE_SCHEMA.md §4.8); the entity relationship is configured on the entity.</summary>
internal sealed class CodeEntityDeclarationRecordConfiguration : IEntityTypeConfiguration<CodeEntityDeclarationRecord>
{
    public void Configure(EntityTypeBuilder<CodeEntityDeclarationRecord> builder)
    {
        builder.ToTable("CodeEntityDeclarations");
        builder.HasKey(d => new { d.EntityId, d.Ordinal });

        builder.HasOne<FileRecord>().WithMany().HasForeignKey(d => d.FileId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.FileId).HasDatabaseName("IX_CodeEntityDeclarations_FileId");
    }
}
