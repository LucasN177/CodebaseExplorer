using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>AIConversations</c> (DATABASE_SCHEMA.md §4.14).</summary>
internal sealed class AiConversationRecordConfiguration : IEntityTypeConfiguration<AiConversationRecord>
{
    public void Configure(EntityTypeBuilder<AiConversationRecord> builder)
    {
        builder.ToTable("AIConversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Title).IsRequired();
        builder.Property(c => c.ProviderId).IsRequired();
        builder.Property(c => c.ModelId).IsRequired();

        builder.HasOne<RepositoryRecord>()
            .WithMany()
            .HasForeignKey(c => c.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.RepositoryId, c.UpdatedAt }).HasDatabaseName("IX_AIConversations_RepositoryId_UpdatedAt");
    }
}

/// <summary>Table <c>AIMessages</c> (DATABASE_SCHEMA.md §4.15).</summary>
internal sealed class AiMessageRecordConfiguration : IEntityTypeConfiguration<AiMessageRecord>
{
    public void Configure(EntityTypeBuilder<AiMessageRecord> builder)
    {
        builder.ToTable("AIMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Role).IsRequired();
        builder.Property(m => m.Content).IsRequired();

        builder.HasOne<AiConversationRecord>()
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.ConversationId, m.Ordinal }).IsUnique().HasDatabaseName("UX_AIMessages_ConversationId_Ordinal");
    }
}

/// <summary>Table <c>AIArtifacts</c> (DATABASE_SCHEMA.md §4.16).</summary>
internal sealed class AiArtifactRecordConfiguration : IEntityTypeConfiguration<AiArtifactRecord>
{
    public void Configure(EntityTypeBuilder<AiArtifactRecord> builder)
    {
        builder.ToTable("AIArtifacts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.ElementKind).IsRequired();
        builder.Property(a => a.ArtifactKind).IsRequired();
        builder.Property(a => a.Content).IsRequired();

        builder.HasOne<RepositoryRecord>()
            .WithMany()
            .HasForeignKey(a => a.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AiMessageRecord>()
            .WithMany()
            .HasForeignKey(a => a.MessageId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => new { a.ElementId, a.ArtifactKind }).HasDatabaseName("IX_AIArtifacts_ElementId_ArtifactKind");
    }
}
