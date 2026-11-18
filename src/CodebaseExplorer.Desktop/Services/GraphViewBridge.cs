using System.Text;
using System.Text.Json;
using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Contracts.Graph.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>
/// Implements the .NET side of the graph web view protocol (GRAPH_ENGINE.md §6) without depending on
/// Avalonia or the WebView: the graph host control connects it to a <c>NativeWebView</c> through
/// <see cref="Connect"/>, forwards page messages to <see cref="Receive"/> and reports page unloads
/// through <see cref="PageUnloaded"/>.
/// </summary>
/// <remarks>
/// Messages from the page are untrusted: every one is validated (size, envelope, payload, element ids
/// of the current graph); anything invalid is logged without its content and ignored. The bridge keeps
/// the current graph, theme and layout and re-sends them whenever the page reports <c>ready</c>, so nothing is
/// lost before the page has loaded or after it reloads. All members are used from the UI thread.
/// </remarks>
public sealed partial class GraphViewBridge(ILogger<GraphViewBridge> logger, IApplicationPaths paths) : ObservableObject, IGraphViewHost
{
    /// <summary>Inbound messages longer than this (in characters) are rejected without parsing.</summary>
    public const int MaxInboundMessageLength = 1_000_000;

    private const int MaxLoggedTextLength = 300;

    private Func<string, Task<string?>>? _invokeScript;
    private GraphThemeMode? _theme;
    private GraphLayout? _layout;
    private HashSet<string> _nodeIds = [];
    private HashSet<string> _edgeIds = [];

    /// <inheritdoc />
    [ObservableProperty]
    public partial bool IsReady { get; private set; }

    /// <inheritdoc />
    [ObservableProperty]
    public partial GraphViewDto? CurrentGraph { get; private set; }

    /// <inheritdoc />
    [ObservableProperty]
    public partial GraphSelection Selection { get; private set; } = GraphSelection.Empty;

    /// <inheritdoc />
    public event EventHandler<GraphElementEventArgs>? InspectorRequested;

    /// <summary>
    /// Writable folder for the web engine's profile (WebView2 user data folder). Must be set explicitly:
    /// the default next to the executable may not be writable (ADR-006, spike pitfall 1).
    /// </summary>
    public string WebViewDataDirectory => Path.Combine(paths.DataDirectory, "webview");

    /// <summary>Records a navigation the host cancelled (only the URI scheme is logged).</summary>
    public void NavigationBlocked(Uri? request) =>
        logger.LogWarning("Blocked graph view navigation to a {Scheme} URI", request?.Scheme ?? "(unknown)");

    /// <summary>Records a failure of the web engine that the host cannot recover from.</summary>
    public void WebViewFailed(string reason) => logger.LogError("Graph view unavailable: {Reason}", reason);

    /// <summary>
    /// Connects the page script invoker (<c>NativeWebView.InvokeScript</c>: returns the JSON-encoded
    /// result, or <see langword="null"/> if the script failed). Disposing the result disconnects it.
    /// </summary>
    public IDisposable Connect(Func<string, Task<string?>> invokeScript)
    {
        ArgumentNullException.ThrowIfNull(invokeScript);
        if (_invokeScript is not null)
        {
            throw new InvalidOperationException("The graph view bridge is already connected to a web view.");
        }

        _invokeScript = invokeScript;
        return new Connection(this, invokeScript);
    }

    /// <summary>The page is gone (navigation, reload or crashed web view process): wait for the next <c>ready</c>.</summary>
    public void PageUnloaded()
    {
        IsReady = false;
        Selection = GraphSelection.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> ShowGraphAsync(GraphViewDto view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        cancellationToken.ThrowIfCancellationRequested();

        CurrentGraph = view;
        _nodeIds = view.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        _edgeIds = view.Edges.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        Selection = GraphSelection.Empty; // the page replaces all elements, which drops its selection

        return !IsReady || await SendAsync(GraphMessageTypes.GraphLoad, GraphMessageSerializer.Serialize(GraphMessageTypes.GraphLoad, view), cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> FocusAsync(string elementId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_nodeIds.Contains(elementId) && !_edgeIds.Contains(elementId))
        {
            logger.LogWarning("Graph focus ignored: the element is not part of the current graph");
            return Task.FromResult(false);
        }

        return IsReady
            ? SendAsync(GraphMessageTypes.GraphFocus, GraphMessageSerializer.Serialize(GraphMessageTypes.GraphFocus, new ElementPayload(elementId)), cancellationToken)
            : Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<bool> SetThemeAsync(GraphThemeMode mode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _theme = mode;
        return !IsReady || await SendAsync(GraphMessageTypes.ViewTheme, ThemeMessage(mode), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SetLayoutAsync(GraphLayout layout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _layout = layout;
        return !IsReady || await SendAsync(GraphMessageTypes.ViewLayout, LayoutMessage(layout), cancellationToken);
    }

    /// <summary>
    /// Handles a message posted by the page (<c>invokeCSharpAction</c> → <c>WebMessageReceived</c>).
    /// Invalid messages are logged and ignored. The returned task completes once any resulting
    /// messages (state re-sent after <c>ready</c>) have been delivered.
    /// </summary>
    public Task Receive(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            Ignore("(none)", "empty message");
            return Task.CompletedTask;
        }

        if (body.Length > MaxInboundMessageLength)
        {
            Ignore("(unknown)", $"message exceeds {MaxInboundMessageLength} characters");
            return Task.CompletedTask;
        }

        if (!GraphMessageSerializer.TryParse(body, out var message, out var error))
        {
            Ignore("(unknown)", error);
            return Task.CompletedTask;
        }

        switch (message.Type)
        {
            case GraphMessageTypes.Ready:
                return OnReadyAsync(message);
            case GraphMessageTypes.SelectionChanged:
                OnSelectionChanged(message);
                break;
            case GraphMessageTypes.NodeOpenInspector:
                OnOpenInspector(message);
                break;
            case GraphMessageTypes.Error:
                OnPageError(message);
                break;
            case GraphMessageTypes.NodeExpandRequested or GraphMessageTypes.NodeCollapseRequested:
                // Expand/collapse arrives with GRAPH-006; until then the request has no effect.
                logger.LogDebug("Graph view message {MessageType} is not supported yet and was ignored", message.Type);
                break;
            default:
                Ignore(Truncate(message.Type), "unknown message type");
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>Builds the script that passes an envelope to the page entry point <c>window.ceReceive</c>.</summary>
    /// <remarks>The envelope is embedded as an ASCII-only JSON string literal, which is also a valid JavaScript string literal.</remarks>
    public static string BuildReceiveScript(string envelopeJson)
    {
        ArgumentNullException.ThrowIfNull(envelopeJson);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStringValue(envelopeJson);
        }

        return $"window.ceReceive({Encoding.UTF8.GetString(buffer.ToArray())})";
    }

    private async Task OnReadyAsync(GraphMessage message)
    {
        if (!GraphMessageSerializer.TryReadPayload<ReadyPayload>(message, out var ready)
            || ready.ProtocolVersion != GraphMessageTypes.ProtocolVersion)
        {
            Ignore(message.Type, "missing or unsupported protocol version");
            return;
        }

        IsReady = true;
        Selection = GraphSelection.Empty;
        logger.LogInformation("Graph view ready");

        if (_theme is { } theme)
        {
            await SendAsync(GraphMessageTypes.ViewTheme, ThemeMessage(theme), CancellationToken.None);
        }

        // Before the graph, so the graph is laid out only once, with the chosen layout.
        if (_layout is { } layout)
        {
            await SendAsync(GraphMessageTypes.ViewLayout, LayoutMessage(layout), CancellationToken.None);
        }

        if (CurrentGraph is { } graph)
        {
            await SendAsync(GraphMessageTypes.GraphLoad, GraphMessageSerializer.Serialize(GraphMessageTypes.GraphLoad, graph), CancellationToken.None);
        }
    }

    private void OnSelectionChanged(GraphMessage message)
    {
        if (!GraphMessageSerializer.TryReadPayload<SelectionChangedPayload>(message, out var payload)
            || payload.NodeIds is null || payload.EdgeIds is null)
        {
            Ignore(message.Type, "nodeIds and edgeIds are required");
            return;
        }

        if (!payload.NodeIds.All(id => id is not null && _nodeIds.Contains(id))
            || !payload.EdgeIds.All(id => id is not null && _edgeIds.Contains(id)))
        {
            Ignore(message.Type, "selection contains ids that are not part of the current graph");
            return;
        }

        Selection = new GraphSelection(
            [.. payload.NodeIds.Distinct(StringComparer.Ordinal)],
            [.. payload.EdgeIds.Distinct(StringComparer.Ordinal)]);
    }

    private void OnOpenInspector(GraphMessage message)
    {
        if (!GraphMessageSerializer.TryReadPayload<ElementPayload>(message, out var payload)
            || payload.ElementId is null || !_nodeIds.Contains(payload.ElementId))
        {
            Ignore(message.Type, "elementId must be a node of the current graph");
            return;
        }

        InspectorRequested?.Invoke(this, new GraphElementEventArgs(payload.ElementId));
    }

    private void OnPageError(GraphMessage message)
    {
        if (!GraphMessageSerializer.TryReadPayload<ErrorPayload>(message, out var payload) || payload.Message is null)
        {
            Ignore(message.Type, "message is required");
            return;
        }

        // The page reports protocol problems in its own words (no repository content beyond element ids).
        logger.LogWarning("Graph view reported an error: {PageError}", Truncate(payload.Message));
    }

    private async Task<bool> SendAsync(string type, string envelopeJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_invokeScript is not { } invokeScript)
        {
            logger.LogWarning("Graph view message {MessageType} not sent: no web view is connected", type);
            return false;
        }

        string? result;
        try
        {
            result = await invokeScript(BuildReceiveScript(envelopeJson));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The web view can fail at any time (adapter destroyed, process gone); the page reloads and
            // reports ready again, which re-sends the current state.
            logger.LogWarning(exception, "Graph view message {MessageType} could not be delivered", type);
            return false;
        }

        if (ReadScriptResult(result) is { } failure)
        {
            logger.LogWarning("Graph view rejected message {MessageType}: {Reason}", type, Truncate(failure));
            return false;
        }

        return true;
    }

    /// <summary>Returns <see langword="null"/> if the page answered <c>"ok"</c>, otherwise the failure reason.</summary>
    private static string? ReadScriptResult(string? result)
    {
        if (result is null)
        {
            return "the script failed (page not loaded or entry point missing)";
        }

        try
        {
            using var document = JsonDocument.Parse(result);
            return document.RootElement.ValueKind == JsonValueKind.String
                ? document.RootElement.GetString() is "ok" ? null : document.RootElement.GetString()
                : "unexpected result";
        }
        catch (JsonException)
        {
            return "unexpected result";
        }
    }

    private static string ThemeMessage(GraphThemeMode mode) =>
        GraphMessageSerializer.Serialize(GraphMessageTypes.ViewTheme, new ViewThemePayload(mode == GraphThemeMode.Dark ? "dark" : "light"));

    private static string LayoutMessage(GraphLayout layout) =>
        GraphMessageSerializer.Serialize(GraphMessageTypes.ViewLayout, new ViewLayoutPayload(layout.ToToken()));

    private void Ignore(string type, string reason) =>
        logger.LogWarning("Ignored invalid graph view message {MessageType}: {Reason}", type, reason);

    private static string Truncate(string text) =>
        text.Length <= MaxLoggedTextLength ? text : string.Concat(text.AsSpan(0, MaxLoggedTextLength), "…");

    private sealed class Connection(GraphViewBridge bridge, Func<string, Task<string?>> invokeScript) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(bridge._invokeScript, invokeScript))
            {
                bridge._invokeScript = null;
                bridge.PageUnloaded();
            }
        }
    }
}
