using CodebaseExplorer.Contracts.Repositories;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// Use case: the start page's list of recent repositories (REPO-005, WF-01 step 1), most recently used first
/// (last opened, last run or registration, whichever is latest).
/// </summary>
public sealed class ListRecentRepositories(IArchitectureModelStore store)
{
    public async Task<IReadOnlyList<RecentRepositoryDto>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var entries = await store.ListRepositoriesAsync(cancellationToken);

        return [.. entries
            .OrderByDescending(LastUsed)
            .ThenBy(e => e.Repository.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new RecentRepositoryDto
            {
                RepositoryId = e.Repository.Id.ToString(),
                Name = e.Repository.Name,
                RootPath = e.Repository.Location.LocalPath,
                HasModel = e.Repository.LastAnalysisRunId is not null,
                LastRunStatus = e.LastRunStatus is { } status ? RepositoryContractMapping.ToContract(status) : null,
                LastRunAt = e.LastRunAt,
                LastOpenedAt = e.LastOpenedAt,
                RemoteUrl = e.Repository.Location.RemoteUrl?.AbsoluteUri,
            })];
    }

    private static DateTimeOffset LastUsed(RepositoryListEntry entry)
    {
        var lastUsed = entry.Repository.AddedAt;
        if (entry.LastOpenedAt > lastUsed)
        {
            lastUsed = entry.LastOpenedAt.Value;
        }

        if (entry.LastRunAt > lastUsed)
        {
            lastUsed = entry.LastRunAt.Value;
        }

        return lastUsed;
    }
}
