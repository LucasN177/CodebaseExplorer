using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>
/// Table <c>SearchIndexStates</c> (DATABASE_SCHEMA.md §4.18). The FTS5 table <c>SearchChunks</c> itself is not an EF
/// entity: it is created by raw SQL in the migration <c>AddSearchIndex</c>.
/// </summary>
internal sealed class SearchIndexStateRecordConfiguration : IEntityTypeConfiguration<SearchIndexStateRecord>
{
    public void Configure(EntityTypeBuilder<SearchIndexStateRecord> builder)
    {
        builder.ToTable("SearchIndexStates");
        builder.HasKey(s => s.RepositoryId);
        builder.Property(s => s.RepositoryId).ValueGeneratedNever();

        builder.HasOne<RepositoryRecord>()
            .WithOne()
            .HasForeignKey<SearchIndexStateRecord>(s => s.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
