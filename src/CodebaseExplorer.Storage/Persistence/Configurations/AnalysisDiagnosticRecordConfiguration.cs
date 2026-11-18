using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseExplorer.Storage.Persistence.Configurations;

/// <summary>Table <c>AnalysisDiagnostics</c> (DATABASE_SCHEMA.md §4.3); the run relationship is configured on the run.</summary>
internal sealed class AnalysisDiagnosticRecordConfiguration : IEntityTypeConfiguration<AnalysisDiagnosticRecord>
{
    public void Configure(EntityTypeBuilder<AnalysisDiagnosticRecord> builder)
    {
        builder.ToTable("AnalysisDiagnostics");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();
        builder.Property(d => d.Severity).HasConversion<string>().IsRequired();
        builder.Property(d => d.Code).IsRequired();
        builder.Property(d => d.Message).IsRequired();

        builder.HasIndex(d => new { d.AnalysisRunId, d.Severity }).HasDatabaseName("IX_AnalysisDiagnostics_AnalysisRunId_Severity");
    }
}
