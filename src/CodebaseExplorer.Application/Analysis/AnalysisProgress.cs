namespace CodebaseExplorer.Application.Analysis;

/// <summary>
/// Progress of a running analysis, reported through <see cref="IProgress{T}"/>
/// (ANALYSIS_ENGINE.md §4).
/// </summary>
public sealed record AnalysisProgress
{
    public AnalysisProgress(AnalysisStage stage, int processed, int? total = null, string? currentItem = null)
    {
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(processed);

        if (total is { } knownTotal)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(knownTotal, nameof(total));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(processed, knownTotal);
        }

        Stage = stage;
        Processed = processed;
        Total = total;
        CurrentItem = string.IsNullOrWhiteSpace(currentItem) ? null : currentItem.Trim();
    }

    public AnalysisStage Stage { get; }

    /// <summary>Items completed in the current stage.</summary>
    public int Processed { get; }

    /// <summary>Items in the current stage; <see langword="null"/> while unknown (e.g. during scanning).</summary>
    public int? Total { get; }

    /// <summary>Item being processed (repository-relative path or project name); never file contents.</summary>
    public string? CurrentItem { get; }

    /// <summary>Completed fraction in [0, 1]; <see langword="null"/> if the total is unknown. An empty stage counts as complete.</summary>
    public double? Fraction => Total switch
    {
        null => null,
        0 => 1d,
        var total => (double)Processed / total.Value,
    };
}
