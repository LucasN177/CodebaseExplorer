# Graph Engine — Codebase Explorer

> Status: **accepted (specification)** · Last reviewed: 2026-11-18
> Implementation status: **project level (L1) projection, DTOs and message envelope implemented**
> (GRAPH-002); **graph web view implemented** (GRAPH-003: `graph/load`, `graph/focus`, `view/theme`,
> `ready`, `selection/changed`, `node/openInspector`, `error`); **host control and message bridge
> implemented** (GRAPH-004, §6.1; verified on Windows/WebView2). The other levels are not, and the
> Architecture Explorer page is still a placeholder (the host is placed on it in UI-003).
> Related: [ADR-006](../decisions/ADR-006-cytoscape-visualization.md).

## 1. Components

| Component | Location | Technology | Responsibility |
|---|---|---|---|
| Graph projection | `src/CodebaseExplorer.Graph` | C# | Turn a model slice into a `GraphViewDto` for a `GraphQuery`: level projection, edge aggregation, filters, neighborhood, node budget. |
| Graph DTOs & messages | `src/CodebaseExplorer.Contracts/Graph` | C# records, System.Text.Json source generation | Wire format between .NET and the web view. |
| Graph host | `src/CodebaseExplorer.Desktop/Controls` (control), `Desktop/Services` (`IGraphViewHost`, `GraphViewBridge`) | Avalonia + Avalonia.Controls.WebView | Hosts the web view, loads bundled assets, validates and relays messages, applies theme (§6.1). |
| Graph web view | `src/CodebaseExplorer.GraphView.Web` | TypeScript, Vite, Cytoscape.js | Rendering, layout, interaction; no business logic. |

## 2. Levels and hierarchy

| Level | Nodes | Edges (aggregated from stored relationships) |
|---|---|---|
| L0 Repository | Ecosystem groups / solution groups / workspace roots | Group-to-group dependency counts |
| L1 Project | `CodeProject`s (+ optional external dependency nodes) | Project→project (`ProjectReference` + aggregated code relationships), project→package |
| L2 Module | `CodeModule`s of one or more expanded projects | Module→module aggregated from entity relationships and imports |
| L3 Entity | `CodeEntity`s of one or more expanded modules | Entity relationships as stored |

**Mixed levels** are allowed: expanding a project replaces its node by a compound (parent) node
containing its modules; edges from the rest of the graph attach to the deepest visible ancestor
of each endpoint.

### Edge aggregation

For each pair of visible nodes `(A, B)`, the projector collects all stored relationships whose
source lies in A's subtree and target in B's subtree (using the denormalized project/module IDs,
DATABASE_SCHEMA.md §4.9) and produces one aggregated edge with:

- `count` (number of underlying relationships) and `occurrences` (evidence count),
- `kinds` histogram (e.g. `{ ProjectReference: 1, UsesType: 37, Calls: 112 }`),
- `maxLevel` / `minLevel` of evidence,
- `isDeclared` (true if at least one manifest-level relationship such as `ProjectReference` exists).

Self-edges inside a collapsed node are dropped from the view but counted in node metrics.

## 3. Query model

```text
GraphQuery
  RepositoryId
  RootLevel            Repository | Project | Module | Entity
  Expanded             set of ElementIds that are shown expanded
  Focus                ElementId?        (neighborhood mode)
  Depth                1..3              (neighborhood hops)
  Direction            Incoming | Outgoing | Both
  Filters              languages, ecosystems, relationship kinds, min evidence level,
                       include tests (default false), include external dependencies (default false),
                       include generated (default false)
  NodeBudget           default 500, max 5,000
```

The projector returns `GraphViewDto` with `nodes`, `edges`, `truncation` information (what was
omitted and why) and `breadcrumbs`.

**Implemented so far (GRAPH-002):** `IGraphProjector.Project(ArchitectureModel, GraphQuery)` returns
`Result<GraphViewDto>` and is a pure function. `GraphQuery` currently has `RepositoryId`, `Level`
(only `project` is accepted, others yield `graph.level-not-supported`) and
`IncludeExternalDependencies`; `Expanded`, `Focus`/`Depth`/`Direction`, filters and `NodeBudget`
are added with GRAPH-006 … GRAPH-008. L1 nodes: one per project (placeholder projects carry the badge
`missing`) and, if requested, one per external dependency; `metrics.size` = files owned by the
project; `childCount` = top-level modules; `isExpandable` stays `false` until expansion exists
(GRAPH-006). Every relationship is attributed to the projects owning its endpoints (file → its
project, module/entity → its project); edges inside one project are dropped. `breadcrumbs` is empty
until navigation exists (GRAPH-009).

## 4. Node budget and large graphs

1. Compute the candidate node set for the query.
2. If `count > NodeBudget`:
   - in overview modes: keep the top-N nodes by degree (fan-in + fan-out) and collapse the rest
     into an *"N more"* aggregate node per parent;
   - in expansion requests: refuse the expansion and return a `truncation` hint suggesting
     neighborhood view or filters.
3. The UI never receives more than `NodeBudget` nodes; layouts run in the web view on at most that
   many nodes.

## 5. DTOs (Contracts)

Implemented in `Contracts/Graph` (records, camelCase JSON via the source-generated
`GraphJsonContext`, nulls omitted). Kinds, levels and badges are lowercase string tokens
(`GraphLevels`, `GraphNodeKinds`, `GraphEvidenceLevels`, `GraphBadges`; relationship kinds use the
Domain key tokens such as `project-reference`), so the wire format does not depend on Domain enums.

```text
GraphViewDto     { schemaVersion, repositoryId, level, nodes[], edges[], truncation?, breadcrumbs[] }
GraphNodeDto     { id, parentId?, kind, label, sublabel?, language?, ecosystem?,
                   isExpandable, isExpanded, childCount, metrics{fanIn, fanOut, size, changeCount?},
                   badges[] }
GraphEdgeDto     { id, source, target, count, occurrences, kinds{}, maxLevel, minLevel, isDeclared }
TruncationDto    { omittedNodeCount, reason, suggestions[] }
```

IDs are `ElementId` hex strings; aggregate edge IDs are deterministic (`source|target`).
DTOs contain **no source code** — only names, paths and metrics.

## 6. .NET ↔ web view message protocol

All messages are JSON envelopes `{ "type": string, "version": 1, "payload": object }`, validated on
both sides (unknown types ignored and logged; schema mismatches rejected). Implemented in
`Contracts/Graph/Messages`: `GraphMessage`, `GraphMessageTypes`, the payload records, and
`GraphMessageSerializer` (`Serialize`, `TryParse` — rejects invalid JSON, missing type, other protocol
versions, non-object payloads — and `TryReadPayload`). The `graph/patch` payload follows with
GRAPH-006 and the theme tokens with GRAPH-003.

| Direction | Type | Payload |
|---|---|---|
| .NET → web | `graph/load` | `GraphViewDto` (replace) |
| .NET → web | `graph/patch` | added/removed nodes and edges (expand/collapse) |
| .NET → web | `graph/focus` | `{ elementId }` |
| .NET → web | `view/theme` | `{ mode: "light" \| "dark", tokens }` |
| .NET → web | `view/layout` | `{ name: "hierarchical" \| "force" \| "concentric" }` |
| web → .NET | `ready` | `{ protocolVersion }` |
| web → .NET | `selection/changed` | `{ nodeIds[], edgeIds[] }` |
| web → .NET | `node/expandRequested` / `node/collapseRequested` | `{ elementId }` |
| web → .NET | `node/openInspector` | `{ elementId }` |
| web → .NET | `error` | `{ message }` |

**Concrete interop (spike GRAPH-001, verified on Windows/WebView2; ADR-006 "Spike result"):**

| Direction | Mechanism |
|---|---|
| .NET → web | `NativeWebView.InvokeScript("window.<entry>(" + JSON string literal of the envelope + ")")` → `Task<string?>` (JSON-encoded return value, `null` if the script threw — treat as failure). 110 KB payloads take ~120 ms round trip. |
| web → .NET | injected global `invokeCSharpAction(envelopeJson)` → `NativeWebView.WebMessageReceived` (`e.Body`); the host validates with `GraphMessageSerializer.TryParse`. |
| Loading | `Navigate(file://…/index.html)` with scripts as separate local files; CSP `default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; img-src 'self' data:; connect-src 'none'`. |
| Navigation | `NavigationStarted`: `Cancel = true` for everything except the page file; `NewWindowRequested`: `Handled = true`. |
| Engine setup | Windows: `AdapterPreference = [WebView2]`, `UserDataFolder` under the app data directory via `EnvironmentRequested`, wait for `AdapterCreated`, check `AdapterInfo` and show an error instead of falling back to WebView1. DevTools off in release builds. |

The page must not use implicit globals for element ids (an element `id="cy"` is `window.cy`).
The page entry point is `window.ceReceive(json)` (returns `"ok"` or `"error: …"`); invalid messages
are rejected as a whole and reported with an `error` message (`src/CodebaseExplorer.GraphView.Web`).
`view/layout` and `graph/patch` are not handled yet (GRAPH-005/006); the page currently lays out with
Cytoscape's `breadthfirst` until GRAPH-005 decides the layouts.
macOS and Linux behavior is not verified yet (GRAPH-001 remainder).

### 6.1 Graph host (GRAPH-004)

| Type | Responsibility |
|---|---|
| `IGraphViewHost` (`Desktop/Services`) | What ViewModels use: `IsReady`, `CurrentGraph`, `Selection` (bindable, `INotifyPropertyChanged`), event `InspectorRequested`, `ShowGraphAsync(GraphViewDto)`, `FocusAsync(elementId)`, `SetThemeAsync(mode)`. ViewModels never touch the WebView. |
| `GraphViewBridge` (`Desktop/Services`) | Implements `IGraphViewHost` without Avalonia types (testable). Serializes outbound messages with `GraphMessageSerializer` and runs `window.ceReceive("<ASCII-only JSON string literal>")`; a `null` result, an exception or anything other than `"ok"` counts as failure and is logged. Keeps the current graph and theme and re-sends them (theme first) on every `ready`, so calls before the page is loaded or after a reload are not lost. Validates every inbound message: length ≤ 1,000,000 characters, envelope, payload type, `ready.protocolVersion`, and element ids that must belong to the current graph (selection, inspector). Invalid messages are logged as warnings (type and reason only, never content) and ignored. `node/expandRequested`/`collapseRequested` are ignored until GRAPH-006. Registered once as singleton (as itself and as `IGraphViewHost`). |
| `GraphHostControl` (`Desktop/Controls`) | `ContentControl` bound to the bridge (`Host="{Binding …}"`; must be the `GraphViewBridge`). Creates the `NativeWebView`: `AdapterPreference = [WebView2]` on Windows and rejects any other adapter, `UserDataFolder` = `<data directory>/webview`, DevTools only in debug builds. Navigates to `GraphView/index.html` after `AdapterCreated`; cancels every other navigation and every new window; relays `WebMessageReceived` to the bridge; marks the page unloaded on navigation, adapter destruction and process termination (reloads up to 3 times after a crash). Shows an error text instead of the graph when the engine is unavailable (no adapter within 20 s, unsupported adapter). Sends the theme from `ActualThemeVariant`. |
| `GraphViewNavigationPolicy` (`Desktop/Controls`) | Allows only the local bundled page file (no query; case-insensitive path on Windows). |

The Desktop project needs an **`app.manifest` with the Windows 10/11 `supportedOS` entry**: without
it Avalonia cannot create the child window for native control hosting and the app crashes as soon
as the host is shown (found in GRAPH-004).

**Runtime check (Windows 11, WebView2, 2026-11-18, temporary harness not committed):** graph sent
before `ready` was delivered on `ready`; `graph/focus` produced a `selection/changed` that updated
`Selection`; `view/theme` accepted; a scripted `location.href = https://…` and `window.open` were
blocked and the page stayed loaded.

## 7. Web view rules

- Assets are bundled into the application and loaded locally; **no remote URLs**, no CDN.
- Content Security Policy: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'`
  (inline styles needed by Cytoscape; to be verified in GRAPH-003); `connect-src 'none'`.
- Navigation away from the bundled page is blocked by the host.
- All labels are inserted as text (Cytoscape labels / `textContent`), never as HTML, because names
  come from untrusted repositories.
- The web view holds only view state (positions, selection). Expand/collapse, filters and data
  always go through .NET, so the C# projector remains the single source of truth.

## 8. Layouts

| Layout | Use | Candidate implementation |
|---|---|---|
| Hierarchical (layered) | Project/module dependency graphs | cytoscape-dagre (MIT) — or ELK via cytoscape-elk **after license review of elkjs (EPL-2.0 OR GPL-3.0)** |
| Force-directed | Exploration, neighborhood views | Cytoscape built-in `cose` measured **12.5–13.9 s** for 500 nodes / 2,000 edges (GRAPH-001) — too slow as default; fcose (MIT, last release 2023) or a web-worker layout to be measured in GRAPH-005 |
| Concentric | Neighborhood around a focus node | Cytoscape built-in |
| Grid | Fallback for huge flat sets | Cytoscape built-in |

Layout choice per view is persisted in user settings. Node positions are not persisted in the MVP.

## 9. Visual encoding

| Property | Encoding |
|---|---|
| Node kind | Shape (project: rounded rectangle, module: rectangle, type: ellipse, external package: diamond) |
| Ecosystem/language | Color (accessible palette, distinct in light and dark theme) |
| Size / Git activity | Node size or heat overlay (optional overlay) |
| Edge evidence level | Solid (Confirmed), dashed (Syntactic), dotted (Heuristic) |
| Edge weight | Width ∝ log(count) |
| Declared dependency | Arrow head style |
| Selection / focus | Highlight + dimming of non-neighbors |

## 10. Performance targets

| Scenario | Target |
|---|---|
| Projection for L1 with 2,000 projects (budget 500) | < 300 ms after data is loaded |
| Initial render of 500 nodes / 2,000 edges incl. layout | < 2 s |
| Expand/collapse round-trip | < 500 ms |
