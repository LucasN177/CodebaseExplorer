namespace CodebaseExplorer.Contracts.Graph.Messages;

/// <summary>Payload of <c>selection/changed</c>.</summary>
public sealed record SelectionChangedPayload(IReadOnlyList<string> NodeIds, IReadOnlyList<string> EdgeIds);
