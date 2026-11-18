using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// Use case: remove a repository and all its stored data — runs, diagnostics and model (REPO-005,
/// SECURITY.md §5). The analyzed folder itself is never touched. For app-managed clones (M5) the clone
/// directory will be deleted as well; until then every repository is a local folder of the user.
/// </summary>
public sealed class RemoveRepository(IArchitectureModelStore store)
{
    public async Task<Result> ExecuteAsync(string repositoryId, CancellationToken cancellationToken)
    {
        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        return await store.DeleteRepositoryAsync(id, cancellationToken)
            ? Result.Success()
            : ApplicationError.NotFound(RepositoryErrorCodes.NotFound, "The repository is no longer stored.");
    }
}
