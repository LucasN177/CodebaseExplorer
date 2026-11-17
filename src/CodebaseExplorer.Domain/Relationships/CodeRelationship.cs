using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;

namespace CodebaseExplorer.Domain.Relationships;

/// <summary>
/// A directed dependency between two elements, backed by at least one evidence record
/// (DOMAIN_MODEL.md §4.7, ADR-007). There is one relationship per (source, kind, target);
/// further occurrences are additional evidence.
/// </summary>
public sealed class CodeRelationship
{
    private CodeRelationship(
        ElementId id,
        NaturalKey naturalKey,
        ElementReference source,
        RelationshipKind kind,
        ElementReference target,
        AnalyzerId origin,
        IReadOnlyList<AnalysisEvidence> evidence,
        IReadOnlyDictionary<string, string> attributes)
    {
        Id = id;
        NaturalKey = naturalKey;
        Source = source;
        Kind = kind;
        Target = target;
        Origin = origin;
        Evidence = evidence;
        Attributes = attributes;
        Level = evidence.Max(e => e.Level);
    }

    public ElementId Id { get; }

    public NaturalKey NaturalKey { get; }

    public ElementReference Source { get; }

    public RelationshipKind Kind { get; }

    public ElementReference Target { get; }

    /// <summary>Strongest level among the evidence (derived).</summary>
    public EvidenceLevel Level { get; }

    /// <summary>Number of evidence records.</summary>
    public int Occurrences => Evidence.Count;

    /// <summary>Analyzer that first produced the relationship.</summary>
    public AnalyzerId Origin { get; }

    /// <summary>At least one evidence record, all belonging to this relationship, without duplicates.</summary>
    public IReadOnlyList<AnalysisEvidence> Evidence { get; }

    /// <summary>
    /// Kind-specific facts with camelCase keys (<c>versionSpec</c>, <c>devOnly</c>, <c>transitive</c>,
    /// <c>resolvedVersion</c>, <c>alias</c>).
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    /// <summary>The id of the relationship (source, kind, target); use it to create evidence first.</summary>
    public static ElementId IdFor(ElementReference source, RelationshipKind kind, ElementReference target) =>
        ElementId.FromNaturalKey(KeyFor(source, kind, target));

    /// <summary>
    /// Creates a relationship. <paramref name="origin"/> defaults to the analyzer of the first evidence
    /// and must be one of the evidence analyzers.
    /// </summary>
    /// <exception cref="ArgumentException">An argument violates a relationship invariant.</exception>
    public static CodeRelationship Create(
        ElementReference source,
        RelationshipKind kind,
        ElementReference target,
        IEnumerable<AnalysisEvidence> evidence,
        IReadOnlyDictionary<string, string>? attributes = null,
        AnalyzerId? origin = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        ValidateEndpoints(source, kind, target);
        ArgumentNullException.ThrowIfNull(evidence);

        var naturalKey = KeyFor(source, kind, target);
        var id = ElementId.FromNaturalKey(naturalKey);
        var evidenceList = DistinctEvidence(id, evidence);

        var resolvedOrigin = origin ?? evidenceList[0].Analyzer;
        if (evidenceList.All(e => e.Analyzer != resolvedOrigin))
        {
            throw new ArgumentException("The origin analyzer must have produced at least one evidence record.", nameof(origin));
        }

        return new CodeRelationship(
            id, naturalKey, source, kind, target, resolvedOrigin, evidenceList, CopyAttributes(attributes));
    }

    /// <summary>
    /// Returns a relationship with <paramref name="additionalEvidence"/> merged in (duplicates by id are
    /// ignored). Origin and attributes are kept; attribute keys that are new are added.
    /// </summary>
    public CodeRelationship WithEvidence(
        IEnumerable<AnalysisEvidence> additionalEvidence,
        IReadOnlyDictionary<string, string>? additionalAttributes = null)
    {
        ArgumentNullException.ThrowIfNull(additionalEvidence);

        var merged = DistinctEvidence(Id, Evidence.Concat(additionalEvidence));

        var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in CopyAttributes(additionalAttributes))
        {
            attributes[key] = value;
        }

        foreach (var (key, value) in Attributes)
        {
            attributes[key] = value;
        }

        return new CodeRelationship(Id, NaturalKey, Source, Kind, Target, Origin, merged, attributes.AsReadOnly());
    }

    private static NaturalKey KeyFor(ElementReference source, RelationshipKind kind, ElementReference target) =>
        NaturalKey.ForRelationship(source.Id, kind.ToKeyToken(), target.Id);

    private static AnalysisEvidence[] DistinctEvidence(ElementId relationshipId, IEnumerable<AnalysisEvidence> evidence)
    {
        var seen = new HashSet<ElementId>();
        var result = new List<AnalysisEvidence>();
        foreach (var item in evidence)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(evidence));

            if (item.RelationshipId != relationshipId)
            {
                throw new ArgumentException(
                    $"Evidence '{item.Id}' belongs to relationship '{item.RelationshipId}', not '{relationshipId}'.",
                    nameof(evidence));
            }

            if (seen.Add(item.Id))
            {
                result.Add(item);
            }
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("A relationship requires at least one evidence record.", nameof(evidence));
        }

        return [.. result];
    }

    private static IReadOnlyDictionary<string, string> CopyAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in attributes ?? new Dictionary<string, string>())
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(attributes));
            copy[key] = value ?? throw new ArgumentException($"Attribute '{key}' has no value.", nameof(attributes));
        }

        return copy.AsReadOnly();
    }

    private static void ValidateEndpoints(ElementReference source, RelationshipKind kind, ElementReference target)
    {
        if (source.Id.Value == Guid.Empty || target.Id.Value == Guid.Empty)
        {
            throw new ArgumentException("Both endpoints of a relationship must be set.");
        }

        var (allowedSources, allowedTargets) = kind switch
        {
            RelationshipKind.ProjectReference => (Kinds(ElementKind.Project), Kinds(ElementKind.Project)),
            RelationshipKind.PackageReference or RelationshipKind.FrameworkReference =>
                (Kinds(ElementKind.Project), Kinds(ElementKind.ExternalDependency)),
            RelationshipKind.Imports => (
                Kinds(ElementKind.File, ElementKind.Module, ElementKind.Entity),
                Kinds(ElementKind.File, ElementKind.Module, ElementKind.Entity, ElementKind.ExternalDependency)),
            RelationshipKind.Inherits or RelationshipKind.Implements or RelationshipKind.UsesType
                or RelationshipKind.Instantiates or RelationshipKind.Calls or RelationshipKind.RegistersService =>
                (Kinds(ElementKind.Entity), Kinds(ElementKind.Entity)),
            _ => (Enum.GetValues<ElementKind>(), Enum.GetValues<ElementKind>()),
        };

        if (!allowedSources.Contains(source.Kind) || !allowedTargets.Contains(target.Kind))
        {
            throw new ArgumentException(
                $"A {kind} relationship cannot connect {source.Kind} to {target.Kind}.");
        }

        if (source == target && kind != RelationshipKind.Calls)
        {
            throw new ArgumentException($"A {kind} relationship cannot point to its own source.");
        }
    }

    private static ElementKind[] Kinds(params ElementKind[] kinds) => kinds;
}
