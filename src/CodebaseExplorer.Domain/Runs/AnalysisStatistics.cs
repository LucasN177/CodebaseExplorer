using CodebaseExplorer.Domain.Evidence;

namespace CodebaseExplorer.Domain.Runs;

/// <summary>Counts and timings of one analysis run.</summary>
public sealed record AnalysisStatistics
{
    public AnalysisStatistics(
        int filesScanned,
        int filesSkipped,
        int projects,
        int modules,
        int entities,
        int relationships,
        int externalDependencies,
        IReadOnlyDictionary<EvidenceLevel, int>? relationshipsByLevel = null,
        IReadOnlyDictionary<string, TimeSpan>? stageDurations = null)
    {
        FilesScanned = NotNegative(filesScanned, nameof(filesScanned));
        FilesSkipped = NotNegative(filesSkipped, nameof(filesSkipped));
        Projects = NotNegative(projects, nameof(projects));
        Modules = NotNegative(modules, nameof(modules));
        Entities = NotNegative(entities, nameof(entities));
        Relationships = NotNegative(relationships, nameof(relationships));
        ExternalDependencies = NotNegative(externalDependencies, nameof(externalDependencies));

        var byLevel = new SortedDictionary<EvidenceLevel, int>();
        foreach (var (level, count) in relationshipsByLevel ?? new Dictionary<EvidenceLevel, int>())
        {
            if (!Enum.IsDefined(level))
            {
                throw new ArgumentOutOfRangeException(nameof(relationshipsByLevel), level, null);
            }

            byLevel[level] = NotNegative(count, nameof(relationshipsByLevel));
        }

        if (byLevel.Count > 0 && byLevel.Values.Sum() != relationships)
        {
            throw new ArgumentException("Relationship counts per level must add up to the relationship count.", nameof(relationshipsByLevel));
        }

        var durations = new SortedDictionary<string, TimeSpan>(StringComparer.Ordinal);
        foreach (var (stage, duration) in stageDurations ?? new Dictionary<string, TimeSpan>())
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stage, nameof(stageDurations));
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(stageDurations), duration, "Durations must not be negative.");
            }

            durations[stage] = duration;
        }

        RelationshipsByLevel = byLevel.AsReadOnly();
        StageDurations = durations.AsReadOnly();
    }

    public static AnalysisStatistics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public int FilesScanned { get; }

    public int FilesSkipped { get; }

    public int Projects { get; }

    public int Modules { get; }

    public int Entities { get; }

    public int Relationships { get; }

    public int ExternalDependencies { get; }

    public IReadOnlyDictionary<EvidenceLevel, int> RelationshipsByLevel { get; }

    /// <summary>Duration per pipeline stage (e.g. <c>scan</c>, <c>link</c>).</summary>
    public IReadOnlyDictionary<string, TimeSpan> StageDurations { get; }

    private static int NotNegative(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        return value;
    }
}
