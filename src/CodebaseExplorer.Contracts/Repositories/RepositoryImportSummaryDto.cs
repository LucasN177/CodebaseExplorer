namespace CodebaseExplorer.Contracts.Repositories;

/// <summary>Outcome of a successful repository import, for display.</summary>
public sealed record RepositoryImportSummaryDto
{
    /// <summary>Repository id (32 hex characters); use it for graph queries.</summary>
    public required string RepositoryId { get; init; }

    public required string Name { get; init; }

    /// <summary>Absolute path of the analyzed folder.</summary>
    public required string RootPath { get; init; }

    public required string AnalysisRunId { get; init; }

    /// <summary><c>succeeded</c> or <c>partially-succeeded</c>.</summary>
    public required string Status { get; init; }

    public int FileCount { get; init; }

    public int ProjectCount { get; init; }

    public int RelationshipCount { get; init; }

    public int WarningCount { get; init; }

    public int ErrorCount { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Warnings and errors (info diagnostics are counted elsewhere and not listed).</summary>
    public IReadOnlyList<DiagnosticDto> Diagnostics { get; init; } = [];
}
