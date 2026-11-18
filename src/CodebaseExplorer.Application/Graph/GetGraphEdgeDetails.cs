using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.Graph;

/// <summary>
/// Use case: the relationships and evidence behind one aggregated edge of a graph view (GRAPH-010,
/// USER_WORKFLOWS.md WF-04 in its M1 form). Reads the current model from the model store, like
/// <see cref="GetGraphView"/>; the full Component Inspector follows with UI-006/UI-007.
/// </summary>
public sealed class GetGraphEdgeDetails(IArchitectureModelStore store, IGraphProjector projector)
{
    /// <param name="query">The query of the view the edge belongs to.</param>
    /// <param name="edgeId">The edge id from <see cref="GraphEdgeDto.Id"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<Result<GraphEdgeDetailsDto>> ExecuteAsync(GraphQuery query, string edgeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(edgeId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Guid.TryParseExact(query.RepositoryId, "N", out var guid) || guid == Guid.Empty)
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        if (await store.LoadCurrentModelAsync(new RepositoryId(guid), cancellationToken) is not { } stored)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotAnalyzed, "The repository has not been analyzed yet.");
        }

        return projector.DescribeEdge(stored.Model, query, edgeId);
    }
}
