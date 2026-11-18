using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Contracts.Repositories;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// Use case: open a stored repository without a new analysis (REPO-005). The graph and the other views
/// then read the stored model; the repository is marked as opened for the recent list.
/// </summary>
public sealed class OpenRepository(IArchitectureModelStore store, TimeProvider timeProvider)
{
    public async Task<Result<OpenRepositoryDto>> ExecuteAsync(string repositoryId, CancellationToken cancellationToken)
    {
        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        if (await store.FindRepositoryAsync(id, cancellationToken) is not { } repository)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotFound, "The repository is no longer stored.");
        }

        if (repository.LastAnalysisRunId is not { } runId)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotAnalyzed,
                $"{repository.Name} has no analysis result yet. Re-analyze it to create one.");
        }

        await store.MarkOpenedAsync(id, timeProvider.GetUtcNow(), cancellationToken);
        return new OpenRepositoryDto
        {
            RepositoryId = repository.Id.ToString(),
            Name = repository.Name,
            RootPath = repository.Location.LocalPath,
            AnalysisRunId = runId.ToString(),
        };
    }
}
