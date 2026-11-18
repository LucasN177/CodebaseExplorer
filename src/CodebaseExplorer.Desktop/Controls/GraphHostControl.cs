using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using CodebaseExplorer.Desktop.Services;

namespace CodebaseExplorer.Desktop.Controls;

/// <summary>
/// Hosts the graph web view (GRAPH_ENGINE.md §1, §6–§7): a <see cref="NativeWebView"/> that shows only
/// the bundled page, connected to the <see cref="GraphViewBridge"/> bound to <see cref="Host"/>.
/// </summary>
/// <remarks>
/// Contains no business logic: it creates and configures the web engine (WebView2 only on Windows,
/// explicit user data folder, DevTools only in debug builds), blocks every navigation except the
/// bundled page and every new window, relays page messages to the bridge and applies the theme.
/// Protocol handling and message validation live in <see cref="GraphViewBridge"/>.
/// </remarks>
public sealed class GraphHostControl : ContentControl
{
    public static readonly StyledProperty<IGraphViewHost?> HostProperty =
        AvaloniaProperty.Register<GraphHostControl, IGraphViewHost?>(nameof(Host));

    private const int MaxProcessRestarts = 3;
    private static readonly TimeSpan AdapterTimeout = TimeSpan.FromSeconds(20);

    private readonly Uri _page = GraphViewNavigationPolicy.PageUri;
    private NativeWebView? _webView;
    private GraphViewBridge? _bridge;
    private IDisposable? _connection;
    private bool _adapterCreated;
    private bool _failed;
    private int _processRestarts;
    private bool _attached;

    public GraphHostControl() => ActualThemeVariantChanged += (_, _) => ApplyTheme();

    /// <summary>The graph host; must be the application's <see cref="GraphViewBridge"/>.</summary>
    public IGraphViewHost? Host
    {
        get => GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Connect();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        Disconnect();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HostProperty && _attached)
        {
            Disconnect();
            Connect();
        }
    }

    private void Connect()
    {
        if (Host is null || _failed)
        {
            return;
        }

        if (Host is not GraphViewBridge bridge)
        {
            throw new InvalidOperationException($"{nameof(GraphHostControl)} requires the {nameof(GraphViewBridge)} as {nameof(Host)}.");
        }

        _bridge = bridge;
        _webView ??= CreateWebView(bridge);
        _connection = bridge.Connect(script => _webView.InvokeScript(script));
        Content = _webView;
        ApplyTheme();

        if (_adapterCreated)
        {
            _webView.Navigate(_page);
        }
        else
        {
            DispatcherTimer.RunOnce(
                () =>
                {
                    if (!_adapterCreated && _connection is not null)
                    {
                        Fail("the web engine did not start. On Windows, the Microsoft Edge WebView2 Runtime is required.");
                    }
                },
                AdapterTimeout);
        }
    }

    private void Disconnect()
    {
        _connection?.Dispose();
        _connection = null;
        _bridge = null;
    }

    private NativeWebView CreateWebView(GraphViewBridge bridge)
    {
        var webView = new NativeWebView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        if (OperatingSystem.IsWindows())
        {
            // Never accept the legacy WebView1/EdgeHTML adapter (ADR-006, spike pitfall 1).
            webView.AdapterPreference = [WebViewAdapterType.WebView2];
        }

        webView.EnvironmentRequested += (_, e) =>
        {
#if DEBUG
            e.EnableDevTools = true;
#else
            e.EnableDevTools = false;
#endif
            if (e is WindowsWebView2EnvironmentRequestedEventArgs webView2)
            {
                Directory.CreateDirectory(bridge.WebViewDataDirectory);
                webView2.UserDataFolder = bridge.WebViewDataDirectory;
            }
        };

        webView.AdapterCreated += (_, _) => OnAdapterCreated(webView);
        webView.AdapterDestroyed += (_, _) =>
        {
            _adapterCreated = false;
            _bridge?.PageUnloaded();
        };

        webView.NavigationStarted += (_, e) =>
        {
            if (GraphViewNavigationPolicy.IsAllowed(e.Request, _page))
            {
                _bridge?.PageUnloaded(); // the page (re)loads and reports ready again
            }
            else
            {
                e.Cancel = true;
                _bridge?.NavigationBlocked(e.Request);
            }
        };

        webView.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            _bridge?.NavigationBlocked(e.Request);
        };

        webView.WebMessageReceived += (_, e) => _bridge?.Receive(e.Body);

        webView.ProcessTerminated += (_, _) =>
        {
            _bridge?.PageUnloaded();
            if (++_processRestarts > MaxProcessRestarts)
            {
                Fail("the web engine process terminated repeatedly.");
                return;
            }

            webView.Navigate(_page);
        };

        return webView;
    }

    private void OnAdapterCreated(NativeWebView webView)
    {
        _adapterCreated = true;

        var info = webView.AdapterInfo;
        if (info is DetailedWebViewAdapterInfo { IsSupported: false } detailed)
        {
            Fail($"the web engine is not supported ({detailed.UnavailableReason ?? "no reason given"}).");
            return;
        }

        if (OperatingSystem.IsWindows() && info?.Type != WebViewAdapterType.WebView2)
        {
            Fail("Microsoft Edge WebView2 is not available.");
            return;
        }

        if (_connection is not null)
        {
            webView.Navigate(_page);
        }
    }

    private void ApplyTheme()
    {
        if (_bridge is { } bridge)
        {
            _ = bridge.SetThemeAsync(ActualThemeVariant == ThemeVariant.Dark ? GraphThemeMode.Dark : GraphThemeMode.Light);
        }
    }

    private void Fail(string reason)
    {
        _failed = true;
        _bridge?.WebViewFailed(reason);
        Disconnect();
        Content = new TextBlock
        {
            Text = "The architecture graph cannot be shown: " + reason,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
        };
    }
}
