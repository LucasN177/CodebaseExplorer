namespace CodebaseExplorer.Contracts.Repositories;

/// <summary>Summary of a repository's current model for the overview page (UI-004, WF-01 step 5).</summary>
public sealed record RepositoryOverviewDto
{
    /// <summary>Repository id (32 hex characters).</summary>
    public required string RepositoryId { get; init; }

    public required string Name { get; init; }

    /// <summary>Absolute path of the analyzed folder.</summary>
    public required string RootPath { get; init; }

    public int FileCount { get; init; }

    /// <summary>Files per detected language, most files first.</summary>
    public IReadOnlyList<LanguageStatisticsDto> Languages { get; init; } = [];

    /// <summary>Detected projects per ecosystem, most projects first; placeholders for missing projects are not counted.</summary>
    public IReadOnlyList<EcosystemStatisticsDto> ProjectsByEcosystem { get; init; } = [];

    /// <summary>Referenced projects whose project file does not exist (placeholders in the model).</summary>
    public int MissingProjectCount { get; init; }

    public int ExternalDependencyCount { get; init; }

    public int RelationshipCount { get; init; }

    /// <summary>The run that produced the current model.</summary>
    public required RunInfoDto ModelRun { get; init; }

    /// <summary>The most recent run, if it is newer than <see cref="ModelRun"/> (e.g. a failed or cancelled re-analysis).</summary>
    public RunInfoDto? NewerRun { get; init; }

    /// <summary>Warnings of <see cref="ModelRun"/>.</summary>
    public int WarningCount { get; init; }

    /// <summary>Errors of <see cref="ModelRun"/>.</summary>
    public int ErrorCount { get; init; }
}

/// <param name="Language">Language id, e.g. <c>csharp</c> or <c>unknown</c>.</param>
/// <param name="FileCount">Number of files.</param>
/// <param name="LineCount">Lines of the text files of that language (binary files have none).</param>
public sealed record LanguageStatisticsDto(string Language, int FileCount, long LineCount);

/// <param name="Ecosystem"><c>dotnet</c>, <c>node</c>, <c>python</c> or <c>other</c>.</param>
/// <param name="ProjectCount">Number of projects.</param>
public sealed record EcosystemStatisticsDto(string Ecosystem, int ProjectCount);

/// <summary>Status and timing of an analysis run.</summary>
/// <param name="RunId">Run id (32 hex characters).</param>
/// <param name="Status">See <see cref="RunStatuses"/>.</param>
/// <param name="StartedAt">Start of the run.</param>
/// <param name="CompletedAt">End of the run, if finished.</param>
public sealed record RunInfoDto(string RunId, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt)
{
    public TimeSpan? Duration => CompletedAt - StartedAt;
}
