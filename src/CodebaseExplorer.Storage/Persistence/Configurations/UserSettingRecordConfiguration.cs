using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>UserSettings</c> (DATABASE_SCHEMA.md §4.17).</summary>
internal sealed class UserSettingRecordConfiguration : IEntityTypeConfiguration<UserSettingRecord>
{
    public void Configure(EntityTypeBuilder<UserSettingRecord> builder)
    {
        builder.ToTable("UserSettings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).ValueGeneratedNever();
        builder.Property(s => s.ValueJson).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();
    }
}
