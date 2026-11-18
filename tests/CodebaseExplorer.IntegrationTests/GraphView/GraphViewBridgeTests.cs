using System.Text.Json;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Contracts.Graph.Messages;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.IntegrationTests.GraphView;

/// <summary>
/// GRAPH-004: the .NET side of the web view protocol, tested without a WebView. The fake page records
/// every script the bridge runs and answers like <c>window.ceReceive</c>.
/// </summary>
public sealed class GraphViewBridgeTests
{
    private const string NodeA = "0000000000000000000000000000000a";
    private const string NodeB = "0000000000000000000000000000000b";
    private const string EdgeAb = NodeA + "|" + NodeB;

    private readonly ListLogger _logger = new();
    private readonly GraphViewBridge _bridge;
    private readonly List<string> _scripts = [];
    private string? _pageAnswer = "\"ok\"";

    public GraphViewBridgeTests()
    {
        _bridge = new GraphViewBridge(_logger, new ApplicationPaths(Path.Combine(Path.GetTempPath(), "ce-bridge-tests")));
        _bridge.Connect(script =>
        {
            _scripts.Add(script);
            return Task.FromResult<string?>(_pageAnswer);
        });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShowGraph_BeforeReady_IsDeliveredWhenThePageIsReady()
    {
        Assert.True(await _bridge.ShowGraphAsync(Graph(), Token));
        Assert.Empty(_scripts);
        Assert.False(_bridge.IsReady);

        await _bridge.Receive(Ready());

        Assert.True(_bridge.IsReady);
        Assert.Equal([GraphMessageTypes.GraphLoad], _scripts.Select(MessageType));
    }

    [Fact]
    public async Task Ready_SendsThemeBeforeGraph_AndAgainAfterReload()
    {
        await _bridge.SetThemeAsync(GraphThemeMode.Dark, Token);
        await _bridge.ShowGraphAsync(Graph(), Token);

        await _bridge.Receive(Ready());
        _bridge.PageUnloaded();
        Assert.False(_bridge.IsReady);
        await _bridge.Receive(Ready());

        Assert.Equal(
            [GraphMessageTypes.ViewTheme, GraphMessageTypes.GraphLoad, GraphMessageTypes.ViewTheme, GraphMessageTypes.GraphLoad],
            _scripts.Select(MessageType));
        Assert.Equal("dark", Envelope(_scripts[0]).GetProperty("payload").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Ready_SendsThemeThenLayoutThenGraph()
    {
        await _bridge.ShowGraphAsync(Graph(), Token);
        await _bridge.SetLayoutAsync(GraphLayout.Hierarchical, Token);
        await _bridge.SetThemeAsync(GraphThemeMode.Light, Token);
        Assert.Empty(_scripts);

        await _bridge.Receive(Ready());

        Assert.Equal([GraphMessageTypes.ViewTheme, GraphMessageTypes.ViewLayout, GraphMessageTypes.GraphLoad], _scripts.Select(MessageType));
        Assert.Equal("hierarchical", Envelope(_scripts[1]).GetProperty("payload").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(GraphLayout.Force, "force")]
    [InlineData(GraphLayout.Hierarchical, "hierarchical")]
    [InlineData(GraphLayout.Concentric, "concentric")]
    public async Task SetLayout_WhenReady_SendsViewLayout(GraphLayout layout, string token)
    {
        await _bridge.Receive(Ready());

        Assert.True(await _bridge.SetLayoutAsync(layout, Token));

        var envelope = Envelope(Assert.Single(_scripts));
        Assert.Equal(GraphMessageTypes.ViewLayout, envelope.GetProperty("type").GetString());
        Assert.Equal(token, envelope.GetProperty("payload").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ShowGraph_WhenReady_SendsGraphLoadWithTheDto()
    {
        await _bridge.Receive(Ready());

        Assert.True(await _bridge.ShowGraphAsync(Graph(), Token));

        var payload = Envelope(Assert.Single(_scripts)).GetProperty("payload");
        Assert.Equal("repo", payload.GetProperty("repositoryId").GetString());
        Assert.Equal([NodeA, NodeB], payload.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("id").GetString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"error: invalid graph/load: duplicate node id\"")]
    [InlineData("42")]
    public async Task Send_PageRejectsOrScriptFails_ReturnsFalseAndLogs(string? answer)
    {
        await _bridge.Receive(Ready());
        _pageAnswer = answer;

        Assert.False(await _bridge.ShowGraphAsync(Graph(), Token));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("rejected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Send_ScriptThrows_ReturnsFalseAndLogs()
    {
        var bridge = new GraphViewBridge(_logger, new ApplicationPaths(Path.GetTempPath()));
        bridge.Connect(_ => throw new InvalidOperationException("adapter gone"));
        await bridge.Receive(Ready());

        Assert.False(await bridge.SetThemeAsync(GraphThemeMode.Light, Token));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("could not be delivered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Focus_UnknownElement_IsNotSent()
    {
        await _bridge.ShowGraphAsync(Graph(), Token);
        await _bridge.Receive(Ready());
        _scripts.Clear();

        Assert.False(await _bridge.FocusAsync("ffffffffffffffffffffffffffffffff", Token));
        Assert.True(await _bridge.FocusAsync(NodeB, Token));

        var envelope = Envelope(Assert.Single(_scripts));

        Assert.Equal(GraphMessageTypes.GraphFocus, envelope.GetProperty("type").GetString());
        Assert.Equal(NodeB, envelope.GetProperty("payload").GetProperty("elementId").GetString());
    }

    [Fact]
    public async Task SelectionChanged_UpdatesBindableSelection()
    {
        await LoadedAsync();
        var changed = new List<string?>();
        _bridge.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await _bridge.Receive(Message(GraphMessageTypes.SelectionChanged, new SelectionChangedPayload([NodeA, NodeA], [EdgeAb])));

        Assert.Equal([NodeA], _bridge.Selection.NodeIds);
        Assert.Equal([EdgeAb], _bridge.Selection.EdgeIds);
        Assert.Contains(nameof(IGraphViewHost.Selection), changed);
    }

    [Fact]
    public async Task ShowGraph_ClearsSelection()
    {
        await LoadedAsync();
        await _bridge.Receive(Message(GraphMessageTypes.SelectionChanged, new SelectionChangedPayload([NodeA], [])));

        await _bridge.ShowGraphAsync(Graph(), Token);

        Assert.True(_bridge.Selection.IsEmpty);
    }

    [Fact]
    public async Task OpenInspector_KnownNode_RaisesEvent()
    {
        await LoadedAsync();
        string? requested = null;
        _bridge.InspectorRequested += (_, e) => requested = e.ElementId;

        await _bridge.Receive(Message(GraphMessageTypes.NodeOpenInspector, new ElementPayload(NodeB)));

        Assert.Equal(NodeB, requested);
    }

    public static TheoryData<string> InvalidMessages => new()
    {
        "",
        "not json",
        "[1,2]",
        """{"version":1,"payload":{}}""",
        """{"type":"ready","version":2,"payload":{"protocolVersion":1}}""",
        """{"type":"ready","version":1,"payload":"x"}""",
        """{"type":"selection/changed","version":1,"payload":{}}""",
        """{"type":"selection/changed","version":1,"payload":{"nodeIds":"a","edgeIds":[]}}""",
        "{\"type\":\"selection/changed\",\"version\":1,\"payload\":{\"nodeIds\":[\"" + NodeA + "\", null],\"edgeIds\":[]}}",
        """{"type":"selection/changed","version":1,"payload":{"nodeIds":["ffffffffffffffffffffffffffffffff"],"edgeIds":[]}}""",
        "{\"type\":\"selection/changed\",\"version\":1,\"payload\":{\"nodeIds\":[],\"edgeIds\":[\"" + NodeA + "\"]}}",
        """{"type":"node/openInspector","version":1,"payload":{}}""",
        "{\"type\":\"node/openInspector\",\"version\":1,\"payload\":{\"elementId\":\"" + EdgeAb + "\"}}",
        """{"type":"error","version":1,"payload":{"message":7}}""",
        """{"type":"graph/load","version":1,"payload":{}}""",
        """{"type":"something/else","version":1,"payload":{}}""",
    };

    [Theory]
    [MemberData(nameof(InvalidMessages))]
    public async Task InvalidInboundMessage_IsLoggedAndIgnored(string message)
    {
        await LoadedAsync();
        await _bridge.Receive(Message(GraphMessageTypes.SelectionChanged, new SelectionChangedPayload([NodeA], [])));
        var inspected = false;
        _bridge.InspectorRequested += (_, _) => inspected = true;
        _logger.Entries.Clear();

        await _bridge.Receive(message);

        Assert.Equal([NodeA], _bridge.Selection.NodeIds);
        Assert.False(inspected);
        Assert.True(_bridge.IsReady);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("Ignored invalid graph view message", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullInboundMessage_IsLoggedAndIgnored()
    {
        await _bridge.Receive(null);

        Assert.Contains(_logger.Entries, e => e.Message.Contains("empty message", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OversizedInboundMessage_IsIgnoredWithoutParsing()
    {
        await LoadedAsync();

        await _bridge.Receive(new string(' ', GraphViewBridge.MaxInboundMessageLength + 1));

        Assert.Contains(_logger.Entries, e => e.Message.Contains("exceeds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidReady_DoesNotMarkThePageReady()
    {
        await _bridge.ShowGraphAsync(Graph(), Token);

        await _bridge.Receive(Message(GraphMessageTypes.Ready, new ReadyPayload(2)));

        Assert.False(_bridge.IsReady);
        Assert.Empty(_scripts);
    }

    [Fact]
    public async Task PageError_IsLogged()
    {
        await _bridge.Receive(Message(GraphMessageTypes.Error, new ErrorPayload("invalid graph/load: edge x references an unknown node")));

        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("unknown node", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disconnect_MarksPageNotReady_AndAllowsReconnect()
    {
        var bridge = new GraphViewBridge(_logger, new ApplicationPaths(Path.GetTempPath()));
        var connection = bridge.Connect(_ => Task.FromResult<string?>("\"ok\""));
        await bridge.Receive(Ready());

        Assert.Throws<InvalidOperationException>(() => bridge.Connect(_ => Task.FromResult<string?>(null)));
        connection.Dispose();

        Assert.False(bridge.IsReady);
        Assert.False(await bridge.FocusAsync(NodeA, Token));
        bridge.Connect(_ => Task.FromResult<string?>("\"ok\"")).Dispose();
    }

    [Fact]
    public void BuildReceiveScript_EmbedsTheEnvelopeAsAsciiStringLiteral()
    {
        var envelope = "{\"type\":\"graph/load\",\"payload\":{\"label\":\"</script><b>'" + char.ConvertFromUtf32(0xC4) + "'" + char.ConvertFromUtf32(0x2028) + "\"}}";

        var script = GraphViewBridge.BuildReceiveScript(envelope);

        Assert.StartsWith("window.ceReceive(\"", script, StringComparison.Ordinal);
        Assert.EndsWith("\")", script, StringComparison.Ordinal);
        Assert.All(script, c => Assert.True(c < 128, $"non-ASCII character U+{(int)c:X4}"));
        Assert.DoesNotContain("<", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'", script, StringComparison.Ordinal);
        Assert.Equal(envelope, JsonSerializer.Deserialize<string>(script["window.ceReceive(".Length..^1]));
    }

    [Fact]
    public void WebViewDataDirectory_IsUnderTheApplicationDataDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ce-data");
        var bridge = new GraphViewBridge(_logger, new ApplicationPaths(root));

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "webview"), bridge.WebViewDataDirectory);
    }

    private async Task LoadedAsync()
    {
        await _bridge.ShowGraphAsync(Graph(), Token);
        await _bridge.Receive(Ready());
    }

    private static GraphViewDto Graph() => new()
    {
        RepositoryId = "repo",
        Level = GraphLevels.Project,
        Nodes = [Node(NodeA, "A"), Node(NodeB, "B")],
        Edges =
        [
            new GraphEdgeDto
            {
                Id = EdgeAb,
                Source = NodeA,
                Target = NodeB,
                Count = 1,
                Occurrences = 1,
                MaxLevel = GraphEvidenceLevels.Confirmed,
                MinLevel = GraphEvidenceLevels.Confirmed,
                IsDeclared = true,
            },
        ],
    };

    private static GraphNodeDto Node(string id, string label) => new()
    {
        Id = id,
        Kind = GraphNodeKinds.Project,
        Label = label,
        Metrics = new GraphNodeMetricsDto(0, 1, 3),
    };

    private static string Ready() => Message(GraphMessageTypes.Ready, new ReadyPayload(GraphMessageTypes.ProtocolVersion));

    private static string Message<T>(string type, T payload) => GraphMessageSerializer.Serialize(type, payload);

    private static JsonElement Envelope(string script)
    {
        var literal = script["window.ceReceive(".Length..^1];
        using var document = JsonDocument.Parse(JsonSerializer.Deserialize<string>(literal)!);
        return document.RootElement.Clone();
    }

    private static string? MessageType(string script) => Envelope(script).GetProperty("type").GetString();

    private sealed class ListLogger : ILogger<GraphViewBridge>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
