using System.Collections.Concurrent;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>Thread-safe in-memory <see cref="IArchitectureModelCache"/> for the current session.</summary>
internal sealed class InMemoryArchitectureModelCache : IArchitectureModelCache
{
    private readonly ConcurrentDictionary<RepositoryId, CachedRepositoryModel> _byId = new();
    private readonly ConcurrentDictionary<string, RepositoryId> _byRoot = new(RootPathComparer.Instance);

    public CachedRepositoryModel? Find(RepositoryId repositoryId) => _byId.GetValueOrDefault(repositoryId);

    public CachedRepositoryModel? FindByRoot(string rootPath)
    {
        ArgumentNullException.ThrowIfNull(rootPath);
        return _byRoot.TryGetValue(rootPath, out var id) ? Find(id) : null;
    }

    public void Store(CachedRepositoryModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _byId[entry.Repository.Id] = entry;
        _byRoot[entry.Repository.Location.LocalPath] = entry.Repository.Id;
    }
}
