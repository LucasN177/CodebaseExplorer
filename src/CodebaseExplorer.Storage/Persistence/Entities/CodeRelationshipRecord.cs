using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Relationships;

namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>
/// Table <c>CodeRelationships</c> (§4.9). Endpoints are polymorphic and have no foreign keys; the
/// container ids are denormalized for fast aggregation (projection at project/module level).
/// </summary>
internal sealed class CodeRelationshipRecord
{
    public Guid Id { get; set; }

    public Guid RepositoryId { get; set; }

    public ElementKind SourceKind { get; set; }

    public Guid SourceId { get; set; }

    public ElementKind TargetKind { get; set; }

    public Guid TargetId { get; set; }

    public RelationshipKind Kind { get; set; }

    /// <summary>Strongest evidence level (derived in the Domain; stored for filtering).</summary>
    public EvidenceLevel Level { get; set; }

    /// <summary>1 (heuristic) … 3 (confirmed), for range filters.</summary>
    public int LevelRank { get; set; }

    public int Occurrences { get; set; }

    public string OriginAnalyzer { get; set; } = string.Empty;

    public string? AttributesJson { get; set; }

    public Guid? SourceProjectId { get; set; }

    public Guid? TargetProjectId { get; set; }

    public Guid? SourceModuleId { get; set; }

    public Guid? TargetModuleId { get; set; }

    public Guid LastSeenRunId { get; set; }
}
