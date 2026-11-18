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
    /// unsupported level. With <paramref name="activity"/> (GIT-005), every project, module and entity node gets
    /// <see cref="GraphNodeMetricsDto.ChangeCount"/> = its file changes (<see cref="Git.GitActivityIndex"/>); without, it stays
    /// <see langword="null"/>.
    /// </summary>
    Result<GraphViewDto> Project(ArchitectureModel model, GraphQuery query, Git.GitActivityIndex? activity = null);

    /// <summary>
    /// Returns the stored relationships (with evidence) that make up the edge <paramref name="edgeId"/> of
    /// the view <see cref="Project"/> returns for the same model and query. Same validation errors as
    /// <see cref="Project"/>, or <see cref="GraphErrorCodes.EdgeNotFound"/> (<see cref="ErrorKind.NotFound"/>).
    /// </summary>
    Result<GraphEdgeDetailsDto> DescribeEdge(ArchitectureModel model, GraphQuery query, string edgeId);
}
