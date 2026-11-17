namespace CodebaseExplorer.Contracts.Graph.Messages;

/// <summary>Payload of <c>graph/focus</c>, <c>node/expandRequested</c>, <c>node/collapseRequested</c>, <c>node/openInspector</c>.</summary>
public sealed record ElementPayload(string ElementId);
