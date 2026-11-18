namespace CodebaseExplorer.Contracts.Graph.Messages;

/// <summary>
/// Payload of <c>graph/patch</c> (GRAPH-006): the difference between the displayed graph and the next one after an
/// expand or collapse. Removed ids are removed first, then nodes are added (parents before children) and updated,
/// then edges are added.
/// </summary>
/// <param name="RemovedNodeIds">Nodes to remove (with their edges).</param>
/// <param name="RemovedEdgeIds">Edges to remove.</param>
/// <param name="AddedNodes">New nodes, parents before children.</param>
/// <param name="UpdatedNodes">Nodes whose data changed (same id and parent), e.g. expansion state or metrics.</param>
/// <param name="AddedEdges">New edges; endpoints exist after the node changes.</param>
public sealed record GraphPatchPayload(
    IReadOnlyList<string> RemovedNodeIds,
    IReadOnlyList<string> RemovedEdgeIds,
    IReadOnlyList<GraphNodeDto> AddedNodes,
    IReadOnlyList<GraphNodeDto> UpdatedNodes,
    IReadOnlyList<GraphEdgeDto> AddedEdges);
