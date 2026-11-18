using CodebaseExplorer.Domain.Evidence;

namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>AnalysisEvidence</c> (§4.10). All evidence is stored; no sampling (ADR-007, decided in STORE-002).</summary>
internal sealed class AnalysisEvidenceRecord
{
    public Guid Id { get; set; }

    public Guid RepositoryId { get; set; }

    public Guid RelationshipId { get; set; }

    public Guid AnalysisRunId { get; set; }

    public string AnalyzerId { get; set; } = string.Empty;

    public string AnalyzerVersion { get; set; } = string.Empty;

    public EvidenceLevel Level { get; set; }

    public string Rule { get; set; } = string.Empty;

    public Guid? FileId { get; set; }

    public int? StartLine { get; set; }

    public int? StartColumn { get; set; }

    public int? EndLine { get; set; }

    public int? EndColumn { get; set; }

    /// <summary>At most 240 characters (Domain limit).</summary>
    public string? Excerpt { get; set; }
}
