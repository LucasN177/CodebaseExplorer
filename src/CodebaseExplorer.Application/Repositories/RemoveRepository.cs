using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Domain.Repositories;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// Use case: remove a repository and all its stored data — runs, diagnostics and model (REPO-005,
/// SECURITY.md §5). A local folder of the user is never touched; for an app-managed clone (GIT-003) the clone
/// directory is deleted as well.
/// </summary>
public sealed class RemoveRepository(IArchitectureModelStore store, IGitRemoteCloner cloner)
{
    public async Task<Result> ExecuteAsync(string repositoryId, CancellationToken cancellationToken)
    {
        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        var repository = await store.FindRepositoryAsync(id, cancellationToken);
        if (repository is null || !await store.DeleteRepositoryAsync(id, cancellationToken))
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotFound, "The repository is no longer stored.");
        }

        // The cloner refuses any path outside the clones directory, so a user's folder can never be deleted here.
        return repository.Location.SourceKind == RepositorySourceKind.Local
            ? Result.Success()
            : await cloner.DeleteCloneAsync(repository.Location.LocalPath, cancellationToken);
    }
}
