# Feature Roadmap — Codebase Explorer

> Status: **living document** · Last reviewed: 2026-11-17

The roadmap is organized in **milestones** (delivery order) and **horizons** (product direction).
Milestones map 1:1 to [MILESTONES.md](../implementation/MILESTONES.md) and the phases in
[IMPLEMENTATION_PLAN.md](../implementation/IMPLEMENTATION_PLAN.md). Dates are deliberately not
fixed; progress is tracked by task status in [TASK_BREAKDOWN.md](../implementation/TASK_BREAKDOWN.md).

## Legend

| Status | Meaning |
|---|---|
| ✅ done | Implemented, tested, documented. |
| 🚧 in progress | At least one task started. |
| ⏳ planned | Specified, not started. |
| 💡 idea | Direction only; needs specification before planning. |

## Horizon 1 — MVP

| Milestone | Theme | User-visible result | Status |
|---|---|---|---|
| **M0** | Foundation | Repository documentation, architecture, buildable solution, startable app shell with navigation and placeholder explorer. | ✅ done |
| **M1** | Vertical slice: .NET project graph | Import a local folder → detect .NET solutions/projects → project and NuGet package references → interactive graph in the explorer. In-memory only. | 🚧 in progress |
| **M2** | Persistence & analysis runs | Results stored in SQLite; recent repositories; reopen without re-analysis; analysis run history; repository overview page. | ⏳ planned |
| **M3** | Deep C# analysis & Component Inspector | Namespaces/types/relationships via Roslyn (no build); evidence levels; inspector with evidence and source snippets; .NET hostile fixtures. | ⏳ planned |
| **M4** | Polyglot & large repositories | TS/JS/Python via Tree-sitter and manifests; hierarchical aggregation; filters; search; node budget; performance fixtures. | 🚧 implemented (2026-11-18), manual checks open |
| **M5** | Git integration | Git metadata; HTTPS clone of GitHub/GitLab remotes; change-activity overlays. | ⏳ planned |
| **M6** | Documentation export | Markdown + Mermaid architecture report. | ✅ implemented |
| **M7** | Optional local AI | Ollama-based Q&A and explanations with validated citations. | 🟡 implemented (real-Ollama check pending) |
| **M8** | MVP hardening & release | Cross-platform packaging, CI on 3 OSes, performance and security gates, docs. | ⏳ planned |

## Horizon 2 — After MVP (next 6–12 months)

| Feature | Description | Status |
|---|---|---|
| Incremental analysis | Content-hash based invalidation; re-analyze only changed files and dependents (ADR-009). | 💡 idea → spec in ANALYSIS_ENGINE.md §8 |
| Architecture history | Store model snapshots per commit; diff two snapshots; visualize added/removed dependencies. | 💡 idea |
| Impact analysis | Transitive dependents with path explanations; "blast radius" overlay. | 💡 idea |
| Coupling & cycles | Cycle detection, instability/abstractness metrics, Git change coupling. | 💡 idea |
| Cross-language links | Heuristic HTTP route ↔ client and message-contract matching. | 💡 idea |
| Cloud AI providers | Opt-in OpenAI-compatible/Azure/other providers via `Microsoft.Extensions.AI`. | 💡 idea |
| Embedding retrieval | Local embeddings (Ollama) + vector index for better AI context. | 💡 idea |
| More languages | Java, Kotlin, Go, Rust, PHP via Tree-sitter analyzers. | 💡 idea |

## Horizon 3 — Long term

- Architecture rules and conformance overlays (layering, forbidden dependencies).
- C4-style views generated from the model; export to Structurizr DSL / PlantUML.
- Vulnerability and license overlays for external dependencies (offline databases).
- Optional, explicitly consented "trusted mode" with design-time builds for maximum C# fidelity.
- Plugin model for third-party analyzers (requires stable analyzer contract and sandboxing concept).

## Open product decisions

These decisions are intentionally **not** made yet. Each must be resolved (and documented in the
referenced document or a new ADR) before the listed milestone starts.

| ID | Decision | Options | Needed before | Where it will be documented |
|---|---|---|---|---|
| OPD-01 | Licensing model of the product | Proprietary (current `LICENSE`), open source (MIT/Apache-2.0), open core | Public release (M8) | README, LICENSE |
| OPD-02 | SSH support for remote clones | HTTPS only (SSH URLs rejected); SSH through libgit2's built-in transport, which starts the system `ssh` executable (found in GIT-006); Git CLI adapter for SSH; a combination | M5 | GIT_INTEGRATION.md / new ADR |
| OPD-03 | Secret storage for Git tokens and AI API keys | OS credential stores via platform APIs; a cross-platform library (to be evaluated); no persistence (ask per session) | M5 | SECURITY.md / new ADR |
| OPD-04 | Graph rendering fallback if WebView is unsuitable on a platform | Fix/mitigate WebView; native Avalonia graph control; external browser (rejected for security unless sandboxed) | End of M1 (spike GRAPH-001; Windows verified 2026-11-17, no fallback needed there; macOS/Linux pending) | ADR-006 spike amendment, or a superseding ADR if the decision changes |
| OPD-05 | Distribution channels | Direct download, winget, Homebrew cask, Flatpak/AppImage, stores | M8 | DEVELOPMENT_GUIDE.md |
| OPD-06 | Telemetry | None; opt-in anonymous usage metrics without repository content | M8 | SECURITY.md |
| OPD-07 | Architecture history storage strategy | Full snapshots per run; delta storage; snapshots only for tagged commits | Post-MVP (history feature) | New ADR referenced from ADR-009 |
| OPD-08 | Trusted mode with design-time builds | Never; opt-in per repository with explicit warning | Post-MVP | New ADR |
| OPD-09 | Minimum supported OS versions | Derived from Avalonia 12 / WebView platform requirements | M8 | README |
