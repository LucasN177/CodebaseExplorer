using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CodebaseExplorer.Contracts.Graph.Messages;

/// <summary>Creates and validates protocol messages using <see cref="GraphJsonContext"/>.</summary>
public static class GraphMessageSerializer
{
    /// <summary>Serializes an envelope with the given type and payload.</summary>
    public static string Serialize<TPayload>(string type, TPayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);

        var payloadElement = JsonSerializer.SerializeToElement(payload, TypeInfo<TPayload>());
        return JsonSerializer.Serialize(new GraphMessage(type, GraphMessageTypes.ProtocolVersion, payloadElement), GraphJsonContext.Default.GraphMessage);
    }

    /// <summary>
    /// Parses an envelope. Fails (without throwing) on invalid JSON, a missing type or payload, or a
    /// different protocol version.
    /// </summary>
    public static bool TryParse(string json, [NotNullWhen(true)] out GraphMessage? message, [NotNullWhen(false)] out string? error)
    {
        message = null;
        try
        {
            var parsed = JsonSerializer.Deserialize(json, GraphJsonContext.Default.GraphMessage);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Type))
            {
                error = "The message has no type.";
                return false;
            }

            if (parsed.Version != GraphMessageTypes.ProtocolVersion)
            {
                error = $"Unsupported protocol version {parsed.Version}.";
                return false;
            }

            if (parsed.Payload.ValueKind is not JsonValueKind.Object)
            {
                error = "The message payload must be an object.";
                return false;
            }

            message = parsed;
            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            error = $"Invalid JSON: {exception.Message}";
            return false;
        }
    }

    /// <summary>Reads the payload as <typeparamref name="TPayload"/>; fails (without throwing) if it does not match.</summary>
    public static bool TryReadPayload<TPayload>(GraphMessage message, [NotNullWhen(true)] out TPayload? payload)
        where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            payload = message.Payload.Deserialize(TypeInfo<TPayload>());
            return payload is not null;
        }
        catch (JsonException)
        {
            payload = null;
            return false;
        }
    }

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        GraphJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new NotSupportedException($"{typeof(T).Name} is not registered in {nameof(GraphJsonContext)}.");
}
