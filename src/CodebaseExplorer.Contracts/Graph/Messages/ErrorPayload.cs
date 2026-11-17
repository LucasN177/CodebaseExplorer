namespace CodebaseExplorer.Contracts.Graph.Messages;

/// <summary>Payload of <c>error</c> (reported by the web view).</summary>
public sealed record ErrorPayload(string Message);
