# Milestones — Codebase Explorer

> Status: **living document** · Last reviewed: 2026-11-17

| Milestone | Name | Status | Exit criteria (summary) |
|---|---|---|---|
| M0 | Foundation | ✅ done (2026-11-17) | Docs, buildable solution, dependency tests, startable shell. |
| M1 | Vertical slice: .NET project graph | 🚧 in progress | Local folder → .NET projects → project graph in the explorer on 3 OSes. |
| M2 | Persistence & analysis runs | ⏳ planned | SQLite persistence, reopen, run history, migrations. |
| M3 | Deep C# & Component Inspector | ⏳ planned | Namespaces/types with evidence levels, inspector, hostile fixtures. |
| M4 | Polyglot & large repositories | ⏳ planned | TS/JS/Python, filters/search/budget, performance targets. |
| M5 | Git integration | ⏳ planned | Metadata, HTTPS clone, activity overlay. |
| M6 | Documentation export | ⏳ planned | Markdown + Mermaid report. |
| M7 | Optional local AI | ⏳ planned | Ollama Q&A with validated citations; off by default. |
| M8 | Hardening & release | ⏳ planned | Packaging, CI on 3 OSes, release gate. |

---

## M0 — Foundation ✅

**Delivered on 2026-11-17**

- Documentation: product (vision, concept, MVP, roadmap, workflows), architecture (9 documents),
  9 ADRs, implementation plan/tasks/tests/DoD, development guides, `CLAUDE.md`.
- Solution: `CodebaseExplorer.slnx`, .NET 10 (`global.json` SDK 10.0.100+), Central Package
  Management, nullable enabled, warnings as errors.
- Projects: Domain, Contracts, Application, Infrastructure, Storage, Analysis, Graph, Git, AI,
  Desktop; tests: Domain/Analysis/Graph/Storage/Integration tests, TestRepositories.
- Implemented code: module DI seams, `IApplicationPaths` + `ApplicationPaths`, composition root,
  Avalonia shell with navigation, Architecture Explorer placeholder, Settings (theme, data dir).
- Tests: 21 passing (project reference rules, composition root, application paths).

**Not delivered (by design):** any analysis, persistence, graph rendering, Git, AI.

## M1 — Vertical slice

- **Entry:** M0 done.
- **Exit criteria:**
  1. `dotnet-layered` fixture imported via UI on Windows, macOS, Linux → correct project graph.
  2. Evidence for each project reference visible.
  3. Cancel and error paths work.
  4. GRAPH-001 result recorded in ADR-006; OPD-04 resolved or explicitly deferred with mitigation.
  5. CI (GitHub Actions) running on three OSes (QA-001).
- **Key risks:** WebView on Linux/macOS; static `.csproj` resolution edge cases.

## M2 — Persistence & analysis runs

- **Entry:** M1 done.
- **Exit criteria:** reopen without re-analysis; migrations with backup; run history; settings persisted; CORE-010 done.
- **Key risks:** bulk insert performance; schema churn (mitigated by re-creatable analysis data).

## M3 — Deep C# & Component Inspector

- **Entry:** M2 done.
- **Exit criteria:** expand to namespaces/types; inspector with evidence + snippets; .NET hostile fixtures green on 3 OSes.
- **Key risks:** Roslyn memory on large solutions; reference pack discovery across OSes.

## M4 — Polyglot & large repositories

- **Entry:** M3 done; ANALYSIS-008 spike done.
- **Exit criteria:** polyglot fixture correct; performance targets met or deviations documented with plan.
- **Key risks:** Tree-sitter native packaging/robustness; graph usability at scale.

## M5 — Git integration

- **Entry:** M2 done (may run in parallel with M3/M4).
- **Exit criteria:** HTTPS clone GitHub/GitLab; metadata and activity overlay; hostile Git config test green.
- **Key risks:** LibGit2Sharp native binaries; history walk performance.

## M6 — Documentation export

- **Entry:** M2 done.
- **Exit criteria:** report generation with snapshot tests; Mermaid validity.

## M7 — Optional local AI

- **Entry:** M4 done (retrieval benefits from all languages), STORE-005 done.
- **Exit criteria:** AI off by default with network-guard test; Ollama Q&A with validated citations.
- **Key risks:** answer quality on small local models; FTS5 availability.

## M8 — Hardening & release

- **Entry:** M1–M7 P0 tasks done (every task that implements an MVP capability is P0).
- **Exit criteria:** MVP release gate (MVP_SCOPE.md §5); open product decisions resolved or deferred explicitly.
