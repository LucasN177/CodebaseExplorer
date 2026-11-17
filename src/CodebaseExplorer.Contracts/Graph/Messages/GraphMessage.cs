using System.Text.Json;

namespace CodebaseExplorer.Contracts.Graph.Messages;

/// <summary>Envelope of every protocol message: <c>{ "type", "version", "payload" }</c>.</summary>
/// <param name="Type">One of <see cref="GraphMessageTypes"/>.</param>
/// <param name="Version">Protocol version; must equal <see cref="GraphMessageTypes.ProtocolVersion"/>.</param>
/// <param name="Payload">Type-specific payload.</param>
public sealed record GraphMessage(string Type, int Version, JsonElement Payload);
