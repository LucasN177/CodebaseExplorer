using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Domain.Model;

namespace CodebaseExplorer.Application.Graph;

/// <summary>
/// Projects an architecture model into a graph view (GRAPH_ENGINE.md). Pure: the result depends only
/// on the model and the query.
/// </summary>
public interface IGraphProjector
{
    /// <summary>
    /// Returns the view, or a <see cref="ErrorKind.Validation"/> error (codes in
    /// <see cref="GraphErrorCodes"/>) if the query does not match the model or asks for an
    /// unsupported level.
    /// </summary>
    Result<GraphViewDto> Project(ArchitectureModel model, GraphQuery query);
}
