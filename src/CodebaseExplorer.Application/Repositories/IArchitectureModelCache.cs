using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// Session-scoped storage of analyzed models (M1). Replaced by the SQLite-backed model store in
/// STORE-004; until then models are lost when the application exits.
/// </summary>
public interface IArchitectureModelCache
{
    CachedRepositoryModel? Find(RepositoryId repositoryId);

    /// <summary>Finds the repository registered for an analyzed folder (path as returned by root validation).</summary>
    CachedRepositoryModel? FindByRoot(string rootPath);

    /// <summary>Stores or replaces the model of a repository.</summary>
    void Store(CachedRepositoryModel entry);
}
