using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.Graph;

/// <summary>
/// Use case: the graph view for an analyzed repository (USER_WORKFLOWS.md WF-03). Reads the current
/// model from the session cache (M1; the SQLite store from STORE-004) and projects it.
/// </summary>
public sealed class GetGraphView(IArchitectureModelCache cache, IGraphProjector projector)
{
    /// <remarks>Asynchronous because the model will be read from storage (STORE-004).</remarks>
    public Task<Result<GraphViewDto>> ExecuteAsync(GraphQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Guid.TryParseExact(query.RepositoryId, "N", out var guid) || guid == Guid.Empty)
        {
            return Task.FromResult<Result<GraphViewDto>>(ApplicationError.Validation(
                RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id."));
        }

        if (cache.Find(new RepositoryId(guid)) is not { } entry)
        {
            return Task.FromResult<Result<GraphViewDto>>(ApplicationError.NotFound(
                RepositoryErrorCodes.NotAnalyzed, "The repository has not been analyzed in this session."));
        }

        return Task.FromResult(projector.Project(entry.Model, query));
    }
}
