namespace CodebaseExplorer.Contracts.Graph.Messages;

/// <summary>Payload of <c>view/theme</c>; <paramref name="Mode"/> is <c>light</c> or <c>dark</c>.</summary>
public sealed record ViewThemePayload(string Mode);
