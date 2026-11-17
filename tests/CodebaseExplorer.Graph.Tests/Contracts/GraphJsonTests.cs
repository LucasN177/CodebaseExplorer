using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Contracts.Graph.Messages;

namespace CodebaseExplorer.Graph.Tests.Contracts;

/// <summary>JSON round-trips for all graph DTOs and protocol messages (GRAPH-002).</summary>
public sealed class GraphJsonTests
{
    private static readonly GraphNodeDto Node = new()
    {
        Id = "0123456789abcdef0123456789abcdef",
        ParentId = "fedcba9876543210fedcba9876543210",
        Kind = GraphNodeKinds.Project,
        Label = "Billing",
        Sublabel = "src/Billing",
        Language = "csharp",
        Ecosystem = "dotnet",
        IsExpandable = true,
        IsExpanded = false,
        ChildCount = 3,
        Metrics = new GraphNodeMetricsDto(1, 2, 30, 7),
        Badges = [GraphBadges.Missing],
    };

    private static readonly GraphEdgeDto Edge = new()
    {
        Id = "a|b",
        Source = "a",
        Target = "b",
        Count = 3,
        Occurrences = 5,
        Kinds = new Dictionary<string, int> { ["project-reference"] = 1, ["uses-type"] = 2 },
        MaxLevel = GraphEvidenceLevels.Confirmed,
        MinLevel = GraphEvidenceLevels.Heuristic,
        IsDeclared = true,
    };

    private static readonly GraphViewDto View = new()
    {
        RepositoryId = "0193a1b2c3d47e5f80718293a4b5c6d7",
        Level = GraphLevels.Project,
        Nodes = [Node],
        Edges = [Edge],
        Truncation = new TruncationDto(12, "node-budget", ["use-filters"]),
        Breadcrumbs = [new BreadcrumbDto("0193a1b2c3d47e5f80718293a4b5c6d7", "shop", GraphLevels.Repository)],
    };

    private static void AssertRoundTrip<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(value, typeInfo);
        var restored = JsonSerializer.Deserialize(json, typeInfo);

        Assert.NotNull(restored);
        Assert.Equal(json, JsonSerializer.Serialize(restored, typeInfo));
    }

    [Fact]
    public void AllDtos_RoundTrip()
    {
        AssertRoundTrip(View, GraphJsonContext.Default.GraphViewDto);
        AssertRoundTrip(Node, GraphJsonContext.Default.GraphNodeDto);
        AssertRoundTrip(Edge, GraphJsonContext.Default.GraphEdgeDto);
        AssertRoundTrip(new TruncationDto(1, "r", ["s"]), GraphJsonContext.Default.TruncationDto);
        AssertRoundTrip(new BreadcrumbDto("i", "l", GraphLevels.Project), GraphJsonContext.Default.BreadcrumbDto);
        AssertRoundTrip(new GraphQuery { RepositoryId = "r", IncludeExternalDependencies = true }, GraphJsonContext.Default.GraphQuery);
        AssertRoundTrip(new ElementPayload("e"), GraphJsonContext.Default.ElementPayload);
        AssertRoundTrip(new ViewThemePayload("dark"), GraphJsonContext.Default.ViewThemePayload);
        AssertRoundTrip(new ViewLayoutPayload("force"), GraphJsonContext.Default.ViewLayoutPayload);
        AssertRoundTrip(new ReadyPayload(1), GraphJsonContext.Default.ReadyPayload);
        AssertRoundTrip(new SelectionChangedPayload(["n"], ["e"]), GraphJsonContext.Default.SelectionChangedPayload);
        AssertRoundTrip(new ErrorPayload("boom"), GraphJsonContext.Default.ErrorPayload);
    }

    [Fact]
    public void Serialization_UsesCamelCaseAndOmitsNulls()
    {
        var node = Node with { ParentId = null, Sublabel = null };

        var json = JsonSerializer.Serialize(node, GraphJsonContext.Default.GraphNodeDto);

        Assert.Contains("\"isExpandable\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"metrics\":{\"fanIn\":1,\"fanOut\":2,\"size\":30,\"changeCount\":7}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("parentId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sublabel", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EdgeKinds_SerializeAsObject()
    {
        var json = JsonSerializer.Serialize(Edge, GraphJsonContext.Default.GraphEdgeDto);

        Assert.Contains("\"kinds\":{\"project-reference\":1,\"uses-type\":2}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_SerializeAndParse()
    {
        var json = GraphMessageSerializer.Serialize(GraphMessageTypes.GraphLoad, View);

        Assert.StartsWith("{\"type\":\"graph/load\",\"version\":1,\"payload\":{", json, StringComparison.Ordinal);
        Assert.True(GraphMessageSerializer.TryParse(json, out var message, out _));
        Assert.Equal(GraphMessageTypes.GraphLoad, message.Type);
        Assert.True(GraphMessageSerializer.TryReadPayload<GraphViewDto>(message, out var payload));
        Assert.Equal(JsonSerializer.Serialize(View, GraphJsonContext.Default.GraphViewDto),
            JsonSerializer.Serialize(payload, GraphJsonContext.Default.GraphViewDto));
    }

    [Fact]
    public void InboundMessage_FromWebView_IsParsed()
    {
        const string json = """{"type":"selection/changed","version":1,"payload":{"nodeIds":["a","b"],"edgeIds":[]}}""";

        Assert.True(GraphMessageSerializer.TryParse(json, out var message, out _));
        Assert.True(GraphMessageSerializer.TryReadPayload<SelectionChangedPayload>(message, out var payload));
        Assert.Equal(["a", "b"], payload.NodeIds);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"type":"","version":1,"payload":{}}""")]
    [InlineData("""{"type":"ready","version":2,"payload":{}}""")]
    [InlineData("""{"type":"ready","version":1,"payload":[1,2]}""")]
    [InlineData("""{"type":"ready","version":1}""")]
    public void InvalidMessages_AreRejectedWithoutException(string json)
    {
        Assert.False(GraphMessageSerializer.TryParse(json, out var message, out var error));
        Assert.Null(message);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void MismatchingPayload_IsRejectedWithoutException()
    {
        const string json = """{"type":"selection/changed","version":1,"payload":{"nodeIds":"not-an-array"}}""";
        Assert.True(GraphMessageSerializer.TryParse(json, out var message, out _));

        Assert.False(GraphMessageSerializer.TryReadPayload<SelectionChangedPayload>(message, out _));
    }

    [Fact]
    public void UnregisteredPayloadType_IsNotSupported() =>
        Assert.Throws<NotSupportedException>(() => GraphMessageSerializer.Serialize("x", new Uri("https://example.org")));
}
