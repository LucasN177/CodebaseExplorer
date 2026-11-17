using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Evidence;

/// <summary>
/// Why a relationship exists: which analyzer found it, by which rule, how certain, and where
/// (DOMAIN_MODEL.md §4.8, ADR-007). Created against <c>CodeRelationship.IdFor(…)</c>.
/// </summary>
public sealed class AnalysisEvidence
{
    /// <summary>Maximum stored excerpt length; longer excerpts are truncated.</summary>
    public const int MaxExcerptLength = 240;

    private AnalysisEvidence(
        ElementId relationshipId,
        AnalyzerId analyzer,
        string analyzerVersion,
        EvidenceLevel level,
        string rule,
        SourceLocation? location,
        string? excerpt,
        AnalysisRunId analysisRunId)
    {
        NaturalKey = NaturalKey.ForEvidence(
            relationshipId,
            analyzer.Value,
            rule,
            location?.FileId,
            location?.StartLine ?? 0,
            location?.StartColumn ?? 0);
        Id = ElementId.FromNaturalKey(NaturalKey);
        RelationshipId = relationshipId;
        Analyzer = analyzer;
        AnalyzerVersion = analyzerVersion;
        Level = level;
        Rule = rule;
        Location = location;
        Excerpt = excerpt;
        AnalysisRunId = analysisRunId;
    }

    public ElementId Id { get; }

    public NaturalKey NaturalKey { get; }

    public ElementId RelationshipId { get; }

    public AnalyzerId Analyzer { get; }

    public string AnalyzerVersion { get; }

    public EvidenceLevel Level { get; }

    /// <summary>Machine-readable rule id, e.g. <c>csproj.ProjectReference</c>.</summary>
    public string Rule { get; }

    /// <summary>Source range; absent only for evidence from non-file sources (e.g. Git).</summary>
    public SourceLocation? Location { get; }

    /// <summary>Up to <see cref="MaxExcerptLength"/> characters of the source, for display.</summary>
    public string? Excerpt { get; }

    public AnalysisRunId AnalysisRunId { get; }

    /// <exception cref="ArgumentException">An argument violates an evidence invariant.</exception>
    public static AnalysisEvidence Create(
        ElementId relationshipId,
        AnalyzerId analyzer,
        string analyzerVersion,
        EvidenceLevel level,
        string rule,
        SourceLocation? location,
        AnalysisRunId analysisRunId,
        string? excerpt = null)
    {
        if (relationshipId.Value == Guid.Empty)
        {
            throw new ArgumentException("Evidence requires a relationship id.", nameof(relationshipId));
        }

        if (analyzer.Value is null)
        {
            throw new ArgumentException("Evidence requires an analyzer.", nameof(analyzer));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerVersion);

        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, null);
        }

        if (analysisRunId.Value == Guid.Empty)
        {
            throw new ArgumentException("Evidence requires the id of the run that produced it.", nameof(analysisRunId));
        }

        var trimmedExcerpt = string.IsNullOrWhiteSpace(excerpt) ? null : excerpt.Trim();
        if (trimmedExcerpt is { Length: > MaxExcerptLength })
        {
            trimmedExcerpt = trimmedExcerpt[..MaxExcerptLength];
        }

        // Rule validation (no ':' or whitespace) happens in NaturalKey.ForEvidence.
        return new AnalysisEvidence(
            relationshipId, analyzer, analyzerVersion.Trim(), level, rule, location, trimmedExcerpt, analysisRunId);
    }
}
