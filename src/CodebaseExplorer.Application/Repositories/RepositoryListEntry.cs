using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>A registered repository with what the list of recent repositories needs (REPO-005).</summary>
/// <param name="Repository">The repository.</param>
/// <param name="LastOpenedAt">When it was last opened or imported, if ever.</param>
/// <param name="LastRunStatus">Status of its most recent run of any outcome, if it has runs.</param>
/// <param name="LastRunAt">When that run finished (or started, if it has not finished).</param>
public sealed record RepositoryListEntry(
    Repository Repository,
    DateTimeOffset? LastOpenedAt,
    AnalysisRunStatus? LastRunStatus,
    DateTimeOffset? LastRunAt);
