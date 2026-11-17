namespace CodebaseExplorer.Contracts.Graph.Messages;

/// <summary>Payload of <c>view/layout</c>; <paramref name="Name"/> is <c>hierarchical</c>, <c>force</c> or <c>concentric</c>.</summary>
public sealed record ViewLayoutPayload(string Name);
