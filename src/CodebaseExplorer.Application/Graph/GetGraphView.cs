using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.Graph;

/// <summary>
/// Use case: the graph view for an analyzed repository (USER_WORKFLOWS.md WF-03). Reads the current
/// model from the <see cref="IArchitectureModelStore"/> (persisted since STORE-004) and projects it.
/// </summary>
public sealed class GetGraphView(IArchitectureModelStore store, IGitActivityStore gitStore, IGraphProjector projector)
{
    public async Task<Result<GraphViewDto>> ExecuteAsync(GraphQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!RepositoryContractMapping.TryParseRepositoryId(query.RepositoryId, out var repositoryId))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        if (await store.LoadCurrentModelAsync(repositoryId, cancellationToken) is not { } stored)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotAnalyzed, "The repository has not been analyzed yet.");
        }

        var activity = query.IncludeActivity && await gitStore.LoadCurrentAsync(repositoryId, cancellationToken) is { } git
            ? new GitActivityIndex(stored.Model, git)
            : null;
        return projector.Project(stored.Model, query, activity);
    }
}
