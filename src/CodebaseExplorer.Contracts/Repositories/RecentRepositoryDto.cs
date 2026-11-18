namespace CodebaseExplorer.Contracts.Repositories;

/// <summary>An entry of the start page's list of recent repositories (REPO-005).</summary>
public sealed record RecentRepositoryDto
{
    /// <summary>Repository id (32 hex characters).</summary>
    public required string RepositoryId { get; init; }

    public required string Name { get; init; }

    /// <summary>Absolute path of the analyzed folder.</summary>
    public required string RootPath { get; init; }

    /// <summary>True if a stored model exists, i.e. the repository can be opened without a new analysis.</summary>
    public bool HasModel { get; init; }

    /// <summary>Status of the most recent run (see <see cref="RunStatuses"/>), or <see langword="null"/> without runs.</summary>
    public string? LastRunStatus { get; init; }

    /// <summary>When the most recent run finished (or started, if it did not finish).</summary>
    public DateTimeOffset? LastRunAt { get; init; }

    public DateTimeOffset? LastOpenedAt { get; init; }

    /// <summary>Remote URL (without credentials) of an app-managed clone (GIT-003); <see langword="null"/> for a local folder.</summary>
    public string? RemoteUrl { get; init; }
}
