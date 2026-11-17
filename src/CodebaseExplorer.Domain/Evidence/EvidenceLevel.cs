namespace CodebaseExplorer.Domain.Evidence;

/// <summary>
/// How certain a relationship is (ADR-007). Ordered: a higher value is stronger evidence.
/// </summary>
public enum EvidenceLevel
{
    /// <summary>Inferred from patterns or conventions; may be wrong.</summary>
    Heuristic = 1,

    /// <summary>Found in syntax or a manifest; target resolved by name or path rules only.</summary>
    Syntactic = 2,

    /// <summary>Resolved by a semantic model or an authoritative declaration whose target exists.</summary>
    Confirmed = 3,
}
