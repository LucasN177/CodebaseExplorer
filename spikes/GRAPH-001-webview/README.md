# Spike GRAPH-001 — Avalonia NativeWebView + Cytoscape.js

Time-boxed spike, **not production code** and not part of `CodebaseExplorer.slnx`. Results are
recorded in `docs/decisions/ADR-006-cytoscape-visualization.md` ("Spike result") and
`docs/architecture/GRAPH_ENGINE.md` §6 on `main`.

The app loads a page with Cytoscape.js 3.35.0 (`web/cytoscape.min.js`, MIT, see
`web/cytoscape.LICENSE`) into `Avalonia.Controls.WebView` 12.1.2 in two ways — inline via
`NavigateToString` and as local files via `Navigate(file://…)` — and checks itself automatically:
JS → .NET and .NET → JS messages, `InvokeScript` return values, rendering of 500 nodes / 2,000 edges,
`cose` layout time, selection events, CSP blocking of network requests, and host-side blocking of
navigation and new windows. It prints the results and writes `results.json`.

## Run

```bash
dotnet build
SPIKE_ADAPTER=WebView2 SPIKE_USER_DATA=/tmp/ce-spike-userdata dotnet bin/Debug/net10.0/WebViewSpike.dll both
```

- `SPIKE_ADAPTER` (optional): forces an adapter (`WebView2`, `WkWebView`, `WebKitGtk`, …). Without it
  the package chooses — on Windows it silently fell back to WebView1/EdgeHTML when WebView2 failed.
- `SPIKE_USER_DATA` (Windows): writable WebView2 user data folder. Without it WebView2 failed with
  `E_ACCESSDENIED` on the test machine.
- Argument: `inline`, `file` or `both`.

## Results

- Windows 11 (10.0.26200), WebView2 157.0.4341.46: `results-windows.json` — all checks pass.
- macOS, Linux: **not yet run** (no machine available in the spike session).
