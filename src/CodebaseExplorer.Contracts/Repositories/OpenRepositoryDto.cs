namespace CodebaseExplorer.Contracts.Repositories;

/// <summary>A repository opened with its stored model, after an import or from the recent list (REPO-005).</summary>
public sealed record OpenRepositoryDto
{
    /// <summary>Repository id (32 hex characters); use it for graph and overview queries.</summary>
    public required string RepositoryId { get; init; }

    public required string Name { get; init; }

    /// <summary>Absolute path of the analyzed folder.</summary>
    public required string RootPath { get; init; }

    /// <summary>The run that produced the current model.</summary>
    public required string AnalysisRunId { get; init; }
}
