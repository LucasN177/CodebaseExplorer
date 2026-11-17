// Spike GRAPH-001 (not production code): self-checking Avalonia app that loads a Cytoscape.js page into
// NativeWebView in two ways (inline NavigateToString, local file:// files) and records what works.
// Run: dotnet run -- [inline|file|both]; results are printed and written to results.json.
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

internal static class Program
{
    public static string[] Args { get; private set; } = [];

    [STAThread]
    public static int Main(string[] args)
    {
        Args = args;
        return AppBuilder.Configure<SpikeApp>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class SpikeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new SpikeWindow();
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                var results = await window.RunAsync(Program.Args.FirstOrDefault() ?? "both");
                var json = results.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "results.json"), json);
                desktop.Shutdown(0);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class SpikeWindow : Window
{
    private readonly Channel<JsonObject> _messages = Channel.CreateUnbounded<JsonObject>();
    private readonly List<string> _navigations = [];
    private readonly List<string> _cancelledNavigations = [];
    private readonly List<string> _newWindows = [];
    private NativeWebView _web = null!;
    private Uri? _allowedUri;

    public SpikeWindow()
    {
        Title = "GRAPH-001 spike";
        Width = 1000;
        Height = 700;
    }

    public async Task<JsonObject> RunAsync(string mode)
    {
        var results = new JsonObject
        {
            ["os"] = Environment.OSVersion.ToString(),
            ["framework"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        };

        foreach (var scenario in mode == "both" ? new[] { "inline", "file" } : [mode])
        {
            results[scenario] = await RunScenarioAsync(scenario);
        }

        return results;
    }

    private TaskCompletionSource _adapterCreated = new();

    private void CreateWebView()
    {
        _adapterCreated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _web = new NativeWebView();
        if (Environment.GetEnvironmentVariable("SPIKE_ADAPTER") is { Length: > 0 } preferred)
        {
            _web.AdapterPreference = [Enum.Parse<Avalonia.Platform.WebViewAdapterType>(preferred)];
        }

        _web.AdapterCreated += (_, _) => _adapterCreated.TrySetResult();
        _web.EnvironmentRequested += (_, e) =>
        {
            e.EnableDevTools = false;
            if (e is Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs webView2
                && Environment.GetEnvironmentVariable("SPIKE_USER_DATA") is { Length: > 0 } userData)
            {
                // WebView2 needs a writable user data folder; the default lies next to the executable.
                Directory.CreateDirectory(userData);
                webView2.UserDataFolder = userData;
            }
        };
        _web.WebMessageReceived += (_, e) =>
        {
            try
            {
                if (JsonNode.Parse(e.Body ?? "null") is JsonObject message)
                {
                    _messages.Writer.TryWrite(message);
                }
            }
            catch (JsonException)
            {
                _messages.Writer.TryWrite(new JsonObject { ["type"] = "invalid", ["raw"] = e.Body });
            }
        };
        _web.NavigationStarted += (_, e) =>
        {
            var uri = e.Request?.ToString() ?? "(null)";
            _navigations.Add(uri);
            var allowed = e.Request is null || e.Request.Scheme is "about" or "data"
                || (_allowedUri is not null && e.Request.Scheme == _allowedUri.Scheme && e.Request.AbsolutePath == _allowedUri.AbsolutePath);
            if (!allowed)
            {
                e.Cancel = true;
                _cancelledNavigations.Add(uri);
            }
        };
        _web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            _newWindows.Add(e.Request?.ToString() ?? "(null)");
        };
        Content = _web;
    }

    private async Task<JsonObject> RunScenarioAsync(string scenario)
    {
        var result = new JsonObject();
        _navigations.Clear();
        _cancelledNavigations.Clear();
        _newWindows.Clear();
        while (_messages.Reader.TryRead(out _)) { }

        CreateWebView();
        var adapterWait = Stopwatch.StartNew();
        var created = await Task.WhenAny(_adapterCreated.Task, Task.Delay(TimeSpan.FromSeconds(20))) == _adapterCreated.Task;
        result["adapterCreated"] = created;
        result["adapterCreatedAfterMs"] = adapterWait.ElapsedMilliseconds;
        result["adapterPreference"] = Environment.GetEnvironmentVariable("SPIKE_ADAPTER") ?? "(default)";

        var info = _web.AdapterInfo;
        result["adapter"] = info is null ? null : $"{info.Type} / {info.Engine} / {info.Version}";
        if (info is Avalonia.Platform.DetailedWebViewAdapterInfo detailed)
        {
            result["adapterSupported"] = detailed.IsSupported;
            result["adapterInstalled"] = detailed.IsInstalled;
            result["scenarios"] = detailed.SupportedScenarios.ToString();
            result["unavailableReason"] = detailed.UnavailableReason;
        }

        var webDir = Path.Combine(AppContext.BaseDirectory, "web");
        var template = await File.ReadAllTextAsync(Path.Combine(webDir, "index.html"));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (scenario == "inline")
            {
                // Everything inline: CSP must allow inline scripts; no file or network access needed.
                var cytoscape = await File.ReadAllTextAsync(Path.Combine(webDir, "cytoscape.min.js"));
                var app = await File.ReadAllTextAsync(Path.Combine(webDir, "app.js"));
                var html = template
                    .Replace("__CSP__", "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; connect-src 'none'")
                    .Replace("__SCRIPTS__", $"<script>{cytoscape}</script><script>{app}</script>");
                result["htmlBytes"] = html.Length;
                _allowedUri = null;
                _web.NavigateToString(html, null!);
            }
            else
            {
                // Local files: scripts from the same folder, CSP 'self'.
                var html = template
                    .Replace("__CSP__", "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; img-src 'self' data:; connect-src 'none'")
                    .Replace("__SCRIPTS__", "<script src=\"cytoscape.min.js\"></script><script src=\"app.js\"></script>");
                var page = Path.Combine(webDir, "page-file.html");
                await File.WriteAllTextAsync(page, html);
                _allowedUri = new Uri(page);
                _web.Navigate(_allowedUri);
            }

            var ready = await WaitForAsync("ready", TimeSpan.FromSeconds(20));
            result["readyAfterMs"] = stopwatch.ElapsedMilliseconds;
            result["ready"] = ready?["payload"]?.DeepClone();
            if (ready is null)
            {
                result["error"] = "no ready message (page or bridge not working)";
                return Finish(result);
            }

            // .NET -> JS: graph/load with 500 nodes / 2000 edges through InvokeScript.
            var graph = BuildGraph(500, 2000);
            var envelope = JsonSerializer.Serialize(new { type = "graph/load", version = 1, payload = graph });
            result["graphMessageBytes"] = envelope.Length;
            stopwatch.Restart();
            var ack = await _web.InvokeScript($"window.receive({JsonSerializer.Serialize(envelope)})");
            result["invokeScriptReturn"] = ack;
            var rendered = await WaitForAsync("graph/rendered", TimeSpan.FromSeconds(20));
            result["graphLoadRoundTripMs"] = stopwatch.ElapsedMilliseconds;
            result["rendered"] = rendered?["payload"]?.DeepClone();

            // Layout timing (force-directed, built into Cytoscape).
            await _web.InvokeScript("window.receive(" + JsonSerializer.Serialize("{\"type\":\"view/layout\",\"version\":1,\"payload\":{\"name\":\"cose\"}}") + ")");
            result["coseLayout"] = (await WaitForAsync("layout/done", TimeSpan.FromSeconds(60)))?["payload"]?.DeepClone();

            // JS -> .NET: selection event.
            result["selectReturn"] = await _web.InvokeScript("(function(){ var n = window.cyInstance.$('#n7'); n.select(); return JSON.stringify({ found: n.length, selected: n.selected(), selectable: n.selectable(), total: window.cyInstance.nodes().length }); })()");
            result["selection"] = (await WaitForAsync("selection/changed", TimeSpan.FromSeconds(5)))?["payload"]?.DeepClone();

            // CSP probes: network requests from the page must be blocked.
            await _web.InvokeScript("window.runSecurityProbes()");
            result["probeFetch"] = (await WaitForAsync("probe/fetch", TimeSpan.FromSeconds(10)))?["payload"]?.DeepClone();
            result["probeCsp"] = (await WaitForAsync("probe/csp", TimeSpan.FromSeconds(10)))?["payload"]?.DeepClone();

            // New window and navigation away must be blocked by the host.
            await _web.InvokeScript("window.tryOpen()");
            await Task.Delay(1000);
            await _web.InvokeScript("window.tryNavigate()");
            await Task.Delay(1500);
            result["titleAfterNavigationAttempt"] = await _web.InvokeScript("document.title");
            result["stillOnPage"] = await _web.InvokeScript("window.cyInstance && typeof window.cyInstance.nodes === 'function' && window.cyInstance.nodes().length === 500 ? 'yes' : 'no'");
        }
        catch (Exception exception)
        {
            result["exception"] = exception.GetType().Name + ": " + exception.Message;
        }

        return Finish(result);
    }

    private JsonObject Finish(JsonObject result)
    {
        result["navigations"] = new JsonArray([.. _navigations.Select(n => (JsonNode?)JsonValue.Create(Shorten(n)))]);
        result["cancelledNavigations"] = new JsonArray([.. _cancelledNavigations.Select(n => (JsonNode?)JsonValue.Create(Shorten(n)))]);
        result["blockedNewWindows"] = new JsonArray([.. _newWindows.Select(n => (JsonNode?)JsonValue.Create(Shorten(n)))]);
        return result;
    }

    private static string Shorten(string value) => value.Length > 120 ? value[..120] + "…" : value;

    private async Task<JsonObject?> WaitForAsync(string type, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                var message = await _messages.Reader.ReadAsync(cancellation.Token);
                if ((string?)message["type"] == type)
                {
                    return message;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static object BuildGraph(int nodeCount, int edgeCount)
    {
        var random = new Random(42);
        var nodes = Enumerable.Range(0, nodeCount).Select(i => new { id = $"n{i}", label = $"Project.{i}" }).ToArray();
        var edges = Enumerable.Range(0, edgeCount)
            .Select(i => new { id = $"e{i}", source = $"n{random.Next(nodeCount)}", target = $"n{random.Next(nodeCount)}" })
            .ToArray();
        return new { nodes, edges };
    }
}
