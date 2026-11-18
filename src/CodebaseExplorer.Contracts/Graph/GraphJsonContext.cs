using System.Text.Json.Serialization;
using CodebaseExplorer.Contracts.Graph.Messages;

namespace CodebaseExplorer.Contracts.Graph;

/// <summary>
/// Source-generated System.Text.Json metadata for all graph DTOs and protocol messages
/// (camelCase, nulls omitted, no reflection).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(GraphQuery))]
[JsonSerializable(typeof(GraphFilters))]
[JsonSerializable(typeof(GraphViewDto))]
[JsonSerializable(typeof(GraphNodeDto))]
[JsonSerializable(typeof(GraphEdgeDto))]
[JsonSerializable(typeof(TruncationDto))]
[JsonSerializable(typeof(BreadcrumbDto))]
[JsonSerializable(typeof(GraphMessage))]
[JsonSerializable(typeof(ElementPayload))]
[JsonSerializable(typeof(ViewThemePayload))]
[JsonSerializable(typeof(ViewLayoutPayload))]
[JsonSerializable(typeof(ReadyPayload))]
[JsonSerializable(typeof(SelectionChangedPayload))]
[JsonSerializable(typeof(ErrorPayload))]
[JsonSerializable(typeof(GraphPatchPayload))]
public sealed partial class GraphJsonContext : JsonSerializerContext;
