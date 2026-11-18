namespace CodebaseExplorer.Contracts.Repositories;

/// <summary>One analysis run of a repository for the run history (UI-005).</summary>
public sealed record AnalysisRunDto
{
    /// <summary>Run id (32 hex characters).</summary>
    public required string RunId { get; init; }

    /// <summary>See <see cref="RunStatuses"/>.</summary>
    public required string Status { get; init; }

    /// <summary><c>initial</c>, <c>manual</c> or <c>incremental</c>.</summary>
    public required string Trigger { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Wall-clock duration of a finished run.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Analyzed commit; always <see langword="null"/> until Git support (M5).</summary>
    public string? CommitSha { get; init; }

    public string? Branch { get; init; }

    /// <summary>True for the run that produced the current model.</summary>
    public bool IsCurrent { get; init; }

    public int FilesScanned { get; init; }

    public int ProjectCount { get; init; }

    public int RelationshipCount { get; init; }

    public int ErrorCount { get; init; }

    public int WarningCount { get; init; }

    public int InfoCount { get; init; }

    /// <summary>All diagnostics of the run in reporting order.</summary>
    public IReadOnlyList<DiagnosticDto> Diagnostics { get; init; } = [];
}
