using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// Use case: delete all stored repository data from inside the app (UI-009 "cache cleanup"): every repository with its
/// runs, models, Git statistics, AI conversations, explanations and search index (cascades), every app-managed clone,
/// then compact the database. Local folders of the user are never touched; settings are kept.
/// </summary>
public sealed class RemoveAllRepositories(IArchitectureModelStore store, RemoveRepository remove, IStorageMaintenance maintenance)
{
    public async Task<Result<RemoveAllSummary>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var repositories = await store.ListRepositoriesAsync(cancellationToken);
        var removed = 0;
        var problems = new List<string>();
        foreach (var entry in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await remove.ExecuteAsync(entry.Repository.Id.ToString(), cancellationToken);
            if (result.IsSuccess)
            {
                removed++;
            }
            else
            {
                problems.Add($"{entry.Repository.Name}: {result.Error.Message}");
            }
        }

        var compaction = await maintenance.CompactAsync(cancellationToken);
        return new RemoveAllSummary(removed, problems, compaction.IsSuccess ? compaction.Value : null);
    }
}

/// <summary>Result of <see cref="RemoveAllRepositories"/>; <c>Compaction</c> is null if compacting failed (data is deleted anyway).</summary>
public sealed record RemoveAllSummary(int Removed, IReadOnlyList<string> Problems, StorageCompaction? Compaction);
