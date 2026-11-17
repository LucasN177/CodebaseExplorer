# ADR-006: Cytoscape.js in an Avalonia WebView for Graph Visualization

- **Status:** Accepted, **conditional on spike GRAPH-001** (cross-platform WebView viability)
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
