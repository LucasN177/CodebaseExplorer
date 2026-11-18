# ADR-006: Cytoscape.js in an Avalonia WebView for Graph Visualization

- **Status:** Accepted for Windows (spike GRAPH-001, 2026-11-17); **macOS and Linux still conditional** on the remaining GRAPH-001 runs
- **Date:** 2026-11-17

## Context

The Architecture Explorer is the product's central feature. It requires interactive graphs with
hundreds to a few thousand visible elements, compound (nested) nodes for hierarchy, multiple
layout algorithms (layered, force-directed), styling by data, selection, and smooth pan/zoom.
Native Avalonia graph controls with these capabilities and comparable maturity were not identified.
Web graph libraries are mature in exactly this area.

## Decision

Render graphs with **Cytoscape.js** (3.35.0, MIT, seen 2026-11-17) in a **TypeScript + Vite**
frontend embedded through an **Avalonia WebView** control.

- Candidate WebView package: `Avalonia.Controls.WebView` 12.1.2 (MIT, net8.0/net10.0), matching
  Avalonia 12. Spike **GRAPH-001** must verify on Windows, macOS and Linux: rendering, local asset
  loading, JS↔.NET messaging, navigation blocking, and runtime prerequisites (e.g. WebView2 on
  Windows, WebKit on macOS/Linux).
- The web frontend contains rendering and interaction only. Data, projection, aggregation and
  filtering live in the C# `Graph` module; communication uses versioned JSON messages with
  Contracts DTOs (GRAPH_ENGINE.md §6).
- Web assets are bundled with the app; no remote content (SECURITY.md).

## Alternatives

| Alternative | Why not (now) |
|---|---|
| Native Avalonia rendering (custom canvas + layout library, e.g. MSAGL) | Large effort for interaction, compound nodes and layouts; would delay the MVP. **Fallback** if GRAPH-001 fails on a platform (OPD-04). |
| D3.js | Lower-level; more custom code for graph interaction and compound nodes. |
| Sigma.js / WebGL renderers | Excellent for very large flat graphs; weaker compound-node support. Could be added for an "overview" mode later. |
| vis-network | Less active and less flexible styling/layout ecosystem than Cytoscape. |
| Opening the graph in the system browser via local HTTP server | Opens a local network surface and breaks the desktop UX; rejected for security. |

## Consequences

**Positive**
- Mature graph features quickly; strong layout ecosystem.
- Clear separation: C# owns data, the web view owns pixels.

**Negative / risks**
- Two technology stacks (Node toolchain for the frontend build) — isolated in
  `src/CodebaseExplorer.GraphView.Web` (documented deviation, SOLUTION_STRUCTURE.md §4).
- WebView platform dependencies and differences; potential Linux packaging constraints.
- Message serialization overhead (mitigated by node budget and patches instead of full reloads).
- Layout extension licenses must be reviewed (elkjs is EPL-2.0 OR GPL-3.0).

## Spike result (2026-11-17, GRAPH-001)

Spike code and raw results: branch `spike/GRAPH-001-webview`, folder `spikes/GRAPH-001-webview`
(self-checking app, `results-windows.json`). Tested: `Avalonia.Controls.WebView` 12.1.2 (MIT, no
license-key check found in the assembly) with Avalonia 12.1.6, Cytoscape.js 3.35.0, .NET 10.0.13.

**Windows 11 (10.0.26200), WebView2 runtime 157.0.4341.46 — all checks pass:**

| Check | Result |
|---|---|
| Adapter | WebView2 / Blink, embedded (`NativeControlHost`), created in ~0.1–0.4 s |
| Load page — inline (`NavigateToString`, 446 KB HTML) | works; page reports ready after ~160 ms |
| Load page — local files (`Navigate(file://…)`, scripts via `<script src>`) | works; ready after ~55 ms; CSP `script-src 'self'` is honored for `file://` |
| JS → .NET | injected global `invokeCSharpAction(string)` → `NativeWebView.WebMessageReceived` (`Body`) |
| .NET → JS | `NativeWebView.InvokeScript(string)` → `Task<string?>` with the JSON-encoded return value; `null` if the script throws |
| `graph/load` with 500 nodes / 2,000 edges (110 KB JSON through `InvokeScript`) | round trip 120–140 ms; Cytoscape render (grid layout) 110–130 ms |
| `cose` (force-directed) layout on the same graph | **12.5–13.9 s — too slow** (target: < 2 s incl. layout) |
| Selection event JS → .NET | works |
| CSP `connect-src 'none'` / `img-src` | `fetch` and remote images blocked; `securitypolicyviolation` reported |
| Navigation away (`location.href = https://…`) | `NavigationStarted` with `Cancel = true` blocks it; page stays intact |
| `window.open` | `NewWindowRequested` with `Handled = true` blocks it |

**Pitfalls found:**

1. **Silent fallback to WebView1/EdgeHTML.** With default settings the control picked the legacy
   WebView1 adapter, which loaded nothing, because WebView2 failed with `E_ACCESSDENIED`. Setting an
   explicit, writable user data folder fixed it; the likely root cause is that the default folder
   (next to the executable) was not writable in the test environment (not proven further — it may be
   specific to that environment). Both must be handled in production: set `AdapterPreference = [WebView2]` on Windows (never accept WebView1) and set
   `WindowsWebView2EnvironmentRequestedEventArgs.UserDataFolder` in `EnvironmentRequested` to a
   folder under the app data directory.
2. **Element ids become JavaScript globals** (`<div id="cy">` is `window.cy`); page code must not
   rely on implicit globals.
3. `InvokeScript` returns `null` on script exceptions — the host must treat `null` as failure.
4. **Application manifest required (found in GRAPH-004).** Inside the real app (unlike the spike), the
   host crashed with "Unable to create child window for native control host" until the Desktop
   project got an `app.manifest` declaring Windows 10/11 in `supportedOS`.

**Decisions taken from the spike (binding for GRAPH-003/GRAPH-004):**

- Assets are loaded as **local files** (`file://`, written to/shipped in a folder of the app), not
  via `NavigateToString`: this allows a strict CSP without `'unsafe-inline'` for scripts and avoids
  size limits of `NavigateToString` (WebView2's documented limit was not tested; 446 KB worked).
  CSP: `default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; img-src 'self' data:; connect-src 'none'`.
- The host allows exactly one navigation target (the page file) and cancels everything else; new
  windows are always handled (blocked). DevTools are disabled (`EnableDevTools = false`) outside
  debug builds.
- The package is adopted; its `PackageVersion` is added when the Desktop project references it
  (GRAPH-004).
- The `cose` layout is not acceptable as default for large graphs; GRAPH-005 must choose and measure
  a faster layout (e.g. `cytoscape-dagre`, `fcose`, layout in a web worker, or a node budget that
  keeps force layouts small). **Resolved in GRAPH-005:** default `force` = fcose (spectral-only above
  800 elements, ≤ 0.4 s at 500 / 2,000 in headless Edge), `hierarchical` = dagre, `concentric` built in;
  no web worker (file:// origin and CSP); elkjs not adopted (license). Measurements: GRAPH_ENGINE.md §8.1.

**Still open (GRAPH-001 remains in progress):**

- **macOS** (WKWebView) and **Linux** (WebKitGTK): not run — no machine in the spike session. Run
  the spike app on both (`spikes/GRAPH-001-webview/README.md`). The package README states that on
  Linux embedded WebViews "might not be available" and suggests `NativeWebDialog`; the API also
  exposes an `OffscreenRenderer` scenario and `GtkWebViewEnvironmentRequestedEventArgs.ExperimentalOffscreen`.
  If embedding fails on Linux, OPD-04 must be decided (options: offscreen mode, separate graph window
  via `NativeWebDialog`, native Avalonia rendering).
