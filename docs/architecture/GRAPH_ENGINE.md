# Graph Engine — Codebase Explorer

> Status: **accepted (specification)** · Last reviewed: 2026-11-18
> Implementation status: **project level (L1) projection, DTOs and message envelope implemented**
> (GRAPH-002); **graph web view implemented** (GRAPH-003: `graph/load`, `graph/focus`, `view/theme`,
> `ready`, `selection/changed`, `node/openInspector`, `error`); **host control and message bridge
> implemented** (GRAPH-004, §6.1; verified on Windows/WebView2); **module and entity levels with
> expand/collapse implemented** (GRAPH-006: `GraphQuery.Expanded`, compound nodes, `graph/patch`,
> `node/expandRequested`/`collapseRequested`). The Architecture Explorer page shows the graph of the imported
> repository (UI-003) and the evidence of a selected edge (GRAPH-010, §6.2). Focus/neighborhood, filters and
> the node budget (GRAPH-008, §4), filters, neighborhood view, breadcrumbs and search (GRAPH-007, §3) are implemented.
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
| L0 Repository | Ecosystem groups / solution groups / workspace roots (GRAPH-009) | Group-to-group dependency counts |
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

**Implemented (GRAPH-006):** `GraphQuery.Expanded` (element ids). The root level stays the project level;
expanded nodes add children (mixed levels, `parentId` = compound parent, parents before children in
`nodes`):

- an expanded **project** shows its top-level namespaces; an expanded **module** shows its child
  namespaces and the types declared directly in it (`kind` `module` / `entity`; module `sublabel` = full
  qualified name, entity `sublabel` = entity kind). Types and members are not expandable in M3 (members are
  shown by the inspector, UI-006);
- **namespace chains are compressed**: a namespace without types and with exactly one child namespace is
  skipped (`Shop` → `Shop.Domain` → `Shop.Domain.Orders` shows `Shop.Domain.Orders` directly); module labels
  are relative to the parent module (`Orders` under `Shop.Domain`), top-level ones fully qualified;
- `isExpandable` = has children, `childCount` = visible children one level down, `metrics.size` = owned files
  (project), entities in the subtree (module), line count (entity);
- every relationship endpoint is attributed to its **deepest visible ancestor** (member → containing type →
  namespace chain → project); relationships inside one node or between a compound node and its own
  content are dropped, so e.g. a project reference stays on the (compound) project node while code
  relationships attach to the visible namespaces/types;
- ids in `Expanded` that are unknown, hidden (inside a collapsed node) or not expandable are ignored; the
  projection is pure, so collapsing restores exactly the previous view. The explorer keeps hidden
  expansions, so re-expanding a project restores its inner expansions.

The whole model is still read per run (ADR-010; kept for the MVP after the QA-002 benchmarks, ADR-012).

**Implemented (GRAPH-007):** `GraphQuery.Filters` (`GraphFilters`) and the neighborhood mode (`Focus`, `Depth`,
`Direction`), evaluated by `GraphFilter` and `GraphProjector`:

- **Filters** (empty lists = all): `Languages` (projects by primary language, code elements by their language),
  `Ecosystems` (projects; external dependencies by package manager: NuGet/framework → `dotnet`, npm → `node`,
  PyPI → `python`), `RelationshipKinds` and `MinLevel` (only relationships; nodes stay), `IncludeTests` and
  `IncludeGenerated` (**default false**): a project whose code files are all tests is hidden, an entity declared only
  in test/generated files is hidden (with its relationships), and a relationship whose every evidence location lies
  in such files is not aggregated. Edge details use the same filters.
- **Neighborhood:** `Focus` is a node of the view or any model element (shown by its deepest visible node; hidden or
  unknown → `graph.focus-not-found`). The view is built with the maximum budget, reduced to the focus (with its
  content), every node within `Depth` (1–3) hops along `Direction` (`incoming`, `outgoing`, `both`; else
  `graph.neighborhood-invalid`) and their compound parents, and then cut to `NodeBudget` nearest-first
  (`truncation.reason = node-budget`).
- **Breadcrumbs:** always start with the repository (`level = repository`); in neighborhood mode they continue with the
  compound parents of the focus and the focus itself.

**Implemented (GRAPH-009):** `GraphQuery.Level = repository` (L0): projects are grouped — npm/yarn/pnpm workspace
(root and members), else the first .NET solution that lists the project (ordinal path), else the ecosystem (".NET
projects", "Node packages", "Python projects", "Other projects"). Groups are nodes of kind `group` (stable id from the
repository and the group key, `sublabel` = project count, expandable into their projects, which expand further as at
the project level); relationships attach to the deepest visible node, so collapsed groups carry group-to-group edges.
Groups count as roots for the node budget; a group expansion that does not fit is refused. A neighborhood focus on a
project inside a collapsed group focuses the group. **Folder fallback:** a model without projects is shown as folders
(`view.level = folder`, kind `folder`): top-level folders plus a `(repository root)` node for the root's own files,
`size` = files below, expandable into subfolders; there are no edges because no language analysis runs without
projects. **Back** is a view-model feature: the explorer remembers up to 50 earlier queries (expansions, filters, level,
neighborhood) and restores them including the filter controls. Known limitation: in the grouped view, *Show in graph*
from the inspector cannot reveal an element inside a collapsed group (the group must be expanded first).

**Search** is an Application use case (`SearchElements`, Contracts `Search/SearchResultsDto`): case-insensitive over
project, module, entity and package names (exact › prefix › contained › qualified name contains; shorter names
first), at most 200 results, each with the graph node and expansions of *Show in graph*; the name index is built once
per loaded model. In the explorer `Ctrl/Cmd+K` focuses the search box (only while the Avalonia part of the window has
the keyboard focus — the embedded web view does not forward keys), and choosing a result opens its neighborhood
(WF-05).

## 4. Node budget and large graphs

1. Compute the candidate node set for the query.
2. If `count > NodeBudget`:
   - in overview modes: keep the top-N nodes by degree (fan-in + fan-out) and collapse the rest
     into an *"N more"* aggregate node per parent;
   - in expansion requests: refuse the expansion and return a `truncation` hint suggesting
     neighborhood view or filters.
3. The UI never receives more than `NodeBudget` nodes; layouts run in the web view on at most that
   many nodes.

**Implemented (GRAPH-008):** `GraphQuery.NodeBudget` (default 500, accepted 2 … 5,000, else
`graph.node-budget-invalid`). Roots (projects and, if requested, external dependencies): if there are more than the
budget, the `budget − 1` roots with the highest root-level degree (fan-in + fan-out over all relationships,
ties in display order) stay and the rest collapse into one node of kind `aggregate` (`"N more"`, stable id derived
from the repository, not expandable, not inspectable) that takes their edges; `truncation.reason = node-budget`.
Expansions are applied in display order only while their children still fit; a refused expansion stays collapsed and
yields `truncation.reason = expansion-refused` with the number of children not shown (only one aggregate per view —
the root level — is needed because deeper levels are refused instead of truncated). `truncation.suggestions` carry
readable hints; the explorer shows them in a banner above the graph. Verified by parameterized tests over random
models (`GraphNodeBudgetTests`: no view exceeds the budget, edges and parents stay consistent, untruncated views equal
the unlimited projection). File and entity counts per node are now precomputed (they were O(n²)).

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

GraphEdgeDetailsDto  { edgeId, sourceLabel, targetLabel, relationships[] }             (GRAPH-010)
GraphRelationshipDto { kind, level, sourceLabel, targetLabel, evidence[] }
GraphEvidenceDto     { analyzer, rule, level, path?, startLine?, endLine? }
```

IDs are `ElementId` hex strings; aggregate edge IDs are deterministic (`source|target`).
DTOs contain **no source code** — only names, paths and metrics. The edge details are not part of
the web view protocol; they go from the Application layer to the explorer ViewModel only.

## 6. .NET ↔ web view message protocol

All messages are JSON envelopes `{ "type": string, "version": 1, "payload": object }`, validated on
both sides (unknown types ignored and logged; schema mismatches rejected). Implemented in
`Contracts/Graph/Messages`: `GraphMessage`, `GraphMessageTypes`, the payload records, and
`GraphMessageSerializer` (`Serialize`, `TryParse` — rejects invalid JSON, missing type, other protocol
versions, non-object payloads — and `TryReadPayload`). `GraphPatchPayload` (GRAPH-006)
carries `removedNodeIds`, `removedEdgeIds`, `addedNodes` (parents first), `updatedNodes` (same id and parent) and
`addedEdges`; `GraphViewBridge.PatchGraphAsync` computes it as the difference between the displayed and the next view
(re-parented nodes and changed edges are removed and added again). The web view validates a patch against its copy
of the displayed graph (`applyPatch`) and applies it completely or not at all, then re-runs the layout. A double click
on an expandable node sends `node/expandRequested` or `node/collapseRequested`, on any other node
`node/openInspector`; the bridge accepts a request only for a node of the current graph in the matching state.

| Direction | Type | Payload |
|---|---|---|
| .NET → web | `graph/load` | `GraphViewDto` (replace) |
| .NET → web | `graph/patch` | `{ removedNodeIds, removedEdgeIds, addedNodes, updatedNodes, addedEdges }` (expand/collapse, GRAPH-006) |
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
`view/layout` is handled since GRAPH-005 (§8): the page remembers the layout for later loads and
re-arranges the current graph when it changes; unknown names are rejected. `graph/patch` follows with GRAPH-006.
macOS and Linux behavior is not verified yet (GRAPH-001 remainder).

### 6.1 Graph host (GRAPH-004)

| Type | Responsibility |
|---|---|
| `IGraphViewHost` (`Desktop/Services`) | What ViewModels use: `IsReady`, `CurrentGraph`, `Selection` (bindable, `INotifyPropertyChanged`), event `InspectorRequested`, `ShowGraphAsync(GraphViewDto)`, `FocusAsync(elementId)`, `SetThemeAsync(mode)`, `SetLayoutAsync(GraphLayout)` (GRAPH-005). ViewModels never touch the WebView. |
| `GraphViewBridge` (`Desktop/Services`) | Implements `IGraphViewHost` without Avalonia types (testable). Serializes outbound messages with `GraphMessageSerializer` and runs `window.ceReceive("<ASCII-only JSON string literal>")`; a `null` result, an exception or anything other than `"ok"` counts as failure and is logged. Keeps the current graph, theme and layout and re-sends them (theme, then layout, then graph — so the graph is laid out once) on every `ready`, so calls before the page is loaded or after a reload are not lost. Validates every inbound message: length ≤ 1,000,000 characters, envelope, payload type, `ready.protocolVersion`, and element ids that must belong to the current graph (selection, inspector). Invalid messages are logged as warnings (type and reason only, never content) and ignored. `node/expandRequested`/`collapseRequested` are ignored until GRAPH-006. Registered once as singleton (as itself and as `IGraphViewHost`). |
| `GraphHostControl` (`Desktop/Controls`) | `ContentControl` bound to the bridge (`Host="{Binding …}"`; must be the `GraphViewBridge`). Creates the `NativeWebView`: `AdapterPreference = [WebView2]` on Windows and rejects any other adapter, `UserDataFolder` = `<data directory>/webview`, DevTools only in debug builds. Navigates to `GraphView/index.html` after `AdapterCreated`; cancels every other navigation and every new window; relays `WebMessageReceived` to the bridge; marks the page unloaded on navigation, adapter destruction and process termination (reloads up to 3 times after a crash). Shows an error text instead of the graph when the engine is unavailable (no adapter within 20 s, unsupported adapter). Sends the theme from `ActualThemeVariant`. |
| `GraphViewNavigationPolicy` (`Desktop/Controls`) | Allows only the local bundled page file (no query; case-insensitive path on Windows). |

The Desktop project needs an **`app.manifest` with the Windows 10/11 `supportedOS` entry**: without
it Avalonia cannot create the child window for native control hosting and the app crashes as soon
as the host is shown (found in GRAPH-004).

**Runtime check (Windows 11, WebView2, 2026-11-18, temporary harness not committed):** graph sent
before `ready` was delivered on `ready`; `graph/focus` produced a `selection/changed` that updated
`Selection`; `view/theme` accepted; a scripted `location.href = https://…` and `window.open` were
blocked and the page stayed loaded.

### 6.2 Edge details (GRAPH-010)

`IGraphProjector.DescribeEdge(model, query, edgeId)` returns the stored relationships behind one
aggregated edge of the view that `Project(model, query)` returns: the same attribution (endpoint →
owning visible node) is used, so the details always match the selected edge. Relationships are
ordered by kind, then endpoint labels; endpoints are named by project name, file path, qualified
module/entity name or package display name. Evidence carries analyzer, rule, level and the
repository-relative path with lines — no excerpt (snippets: UI-007). Errors: the projection's
validation errors, or `graph.edge-not-found` (`NotFound`). The use case `GetGraphEdgeDetails` reads
the model from the model store like `GetGraphView` (STORE-004). The explorer side panel lists up to 50 evidence
records of a selected edge (`kind · level`, `path:line`, `analyzer · rule`).

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

Implemented in GRAPH-005 (`src/CodebaseExplorer.GraphView.Web/src/layouts.ts`):

| Layout (`view/layout` name) | Use | Implementation |
|---|---|---|
| Force-directed (`force`) — **default** | Exploration, any graph size | cytoscape-fcose 2.2.0 (MIT). Up to 800 elements (nodes + edges, `FORCE_REFINEMENT_LIMIT`) quality `default` (spectral start + force refinement); above that only the spectral `draft` step, which keeps 500 nodes / 2,000 edges well below 2 s but lets nodes overlap. Random start, so positions differ between loads. |
| Hierarchical (`hierarchical`) | Project/module dependency graphs | cytoscape-dagre 4.1.0 (MIT, bundles @dagrejs/dagre), top-down, `tight-tree` ranker. Above the 2 s target on very large graphs (see §8.1); the user's explicit choice is still honored. |
| Concentric (`concentric`) | Hubs: most connected nodes in the center | Cytoscape built-in, rings by degree. |

Not adopted: Cytoscape's built-in `cose` (too slow), ELK via cytoscape-elk (license: elkjs is EPL-2.0 OR GPL-3.0-or-later, see SOLUTION_STRUCTURE.md §6.2; it was not faster than dagre either), euler (fast, but no component packing; isolated nodes drift far away), a **grid** layout (no use case at the project level yet), and **web-worker layouts**: the page is loaded from `file://` (opaque origin), where Chromium refuses `new Worker(file://…)`, and a `blob:` worker would need a `worker-src blob:` exception in the strict CSP (§7); the layouts also need the element positions inside Cytoscape on the main thread. Revisit if the default layout no longer meets the targets.

The layout choice is one global user setting (`graph.layout`, DATABASE_SCHEMA.md §4.17), restored at startup and changed through the switcher above the graph in the Architecture Explorer; per-view choices can follow when there are several graph views. Node positions are not persisted in the MVP.

### 8.1 Layout measurements (GRAPH-005, 2026-11-18)

Random graphs with seeded edges (`dag`: edges only from lower to higher node index, like a dependency graph), node size 28 × 18 px, three runs each, time = layout + fit + one render tick, in **headless Microsoft Edge 157** (Chromium, the engine behind WebView2) on the Windows 11 development machine. Calibration: `cose` took **5.0 s** there for 500 / 2,000, against 12.5–13.9 s in the visible WebView2 of spike GRAPH-001 (and 11.3–12.7 s in another Chromium window) — so numbers in the app can be up to ~2.8× higher than below.

| Graph (nodes / edges) | cose | fcose `default` | fcose `draft` | dagre network-simplex | dagre tight-tree | ELK layered | euler | concentric |
|---|---|---|---|---|---|---|---|---|
| 500 / 2,000 (random) | 4,959–4,965 ms | 3,237–5,645 ms | 358–397 ms | — | — | — | 1,528–1,639 ms | 246–297 ms |
| 500 / 2,000 dag | — | 3,423–5,881 ms | 364–383 ms | 3,728–5,921 ms | 2,248–2,779 ms | 2,455–3,195 ms | 1,482–1,613 ms | — |
| 500 / 1,000 dag | — | — | — | — | 453–553 ms | — | — | — |
| 300 / 1,200 dag | — | — | — | — | 941–1,057 ms | — | — | — |
| 300 / 900 dag | — | 1,181–1,692 ms | — | — | 465–554 ms | — | 772–783 ms | — |
| 250 / 750 dag | — | 605–1,335 ms | — | — | — | — | — | — |
| 200 / 800 dag | — | 627–1,259 ms | — | — | — | — | — | — |
| 200 / 600 dag | — | 355–598 ms | 71–76 ms | 475–599 ms | 279–376 ms | — | — | — |
| 150 / 450 dag | — | 246–415 ms | — | — | — | — | — | — |
| 100 / 300 dag | — | 138–157 ms | 49–56 ms | — | 141–155 ms | — | 99–112 ms | — |

Conclusions: the default `force` layout meets the < 2 s target (§10) at 500 / 2,000 even with the 2.8× calibration factor (`draft`: ≤ 0.4 s → ~1.1 s), and up to the 800-element limit the refined layout stays at ≤ 0.6 s (≤ ~1.7 s calibrated). Hierarchical is fine for typical repositories (≤ 300 projects / 900 edges: ≤ 0.6 s) but not for the largest budget-sized graphs. fcose's `numIter` did not change its run time noticeably (measured in Node.js), and dagre's `disableOptimalOrderHeuristic` did not help at 500 / 2,000. The benchmark harness was a temporary page outside the repository (not committed); visual quality was compared on screenshots of the same graphs.

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

Implemented in GRAPH-005 (`styles.ts`, `elements.ts`, `highlight.ts` in the web view):

- **Kind:** project = rounded rectangle, external package = diamond. Module (rectangle) and type (ellipse) shapes are added with those node kinds in GRAPH-006.
- **Ecosystem color** (Okabe–Ito based, each color ≥ 3:1 against its theme background, checked by `test/styles.test.ts`): .NET (`dotnet`, `nuget`, `dotnet-framework`) blue, Node (`node`, `npm`) green, Python (`python`, `pypi`) purple, everything else gray. A missing project keeps its own color (vermillion, dashed outline, translucent).
- **Size:** node width/height × `min(2, 1 + log10(1 + size) / 3)` (size = owned files for projects). The Git-activity overlay follows in M5.
- **Edges:** line style by `maxLevel`; width `min(7, 1.5 + log2(count))` px; triangle arrow for declared, `vee` for undeclared dependencies.
- **Selection:** selected node gets a thick border, selected edge the selection color; everything that is neither selected nor directly connected to the selection is dimmed to 15 % opacity (pure view state, not reported to .NET).

## 10. Performance targets

| Scenario | Target |
|---|---|
| Projection for L1 with 2,000 projects (budget 500) | < 300 ms after data is loaded |
| Initial render of 500 nodes / 2,000 edges incl. layout | < 2 s |
| Expand/collapse round-trip | < 500 ms |
