# Product Concept — Codebase Explorer

> Status: **accepted** · Last reviewed: 2026-11-17 · Implementation status: **not implemented** (foundation only)

## 1. Target audience

### Primary personas

| Persona | Situation | What they need from Codebase Explorer |
|---|---|---|
| **Onboarding developer** ("Nina") | Joined a team, must become productive in an unknown 300-project .NET/TypeScript repository. | A map of the system: which projects exist, how they depend on each other, where to start reading. |
| **Senior developer / tech lead** ("Tom") | Owns a large codebase, plans refactorings and reviews architectural changes. | Coupling hot spots, impact of changing a component, verification that layer boundaries hold. |
| **Software architect** ("Ana") | Responsible for several systems; documents and communicates architecture. | Up-to-date, evidence-backed architecture views and exportable documentation. |
| **Consultant / auditor** ("Max") | Gets access to a foreign repository for a short engagement (due diligence, modernization assessment). | Fast, offline, private analysis of an unknown codebase without executing it. |

### Secondary personas

- **Open-source contributors** exploring a project before contributing.
- **Engineering managers** who want a high-level picture of a system's structure (read-only, exported docs).

### Environments

Windows, macOS and Linux workstations; repositories from a few files to enterprise monorepos with
tens of thousands of files and thousands of projects/packages; often no permission to upload code
to external services.

## 2. Problem statement

1. **Understanding is slow and manual.** Architecture knowledge lives in people's heads or in
   outdated diagrams. Each developer reconstructs it by reading files.
2. **IDEs show files, not systems.** Solution explorers and "find references" answer local
   questions. They do not show the dependency structure of 200 projects or the module graph of a
   TypeScript monorepo, and they are language-specific.
3. **Polyglot repositories fall between tools.** A repository with a .NET backend, a TypeScript
   frontend and Python tooling has no single tool that shows how the parts fit together.
4. **Existing visualizers either hairball or require builds.** Many dependency tools render every
   node at once, or require compiling/running the code, which is slow, fragile and a security risk
   for untrusted code.
5. **AI code assistants hallucinate structure.** Chat assistants answer architecture questions
   from partial context, without telling the user what the answer is based on.

## 3. Product value

| Value | Description |
|---|---|
| **Time to understanding** | From "clone" to "I know how this is structured" in minutes. |
| **Trustworthy results** | Every edge in the graph can be traced to a file and line range and an analyzer rule, with a certainty level. |
| **One model for many languages** | A single, language-independent architecture model for C#, TypeScript, JavaScript and Python (extensible). |
| **Safe for untrusted code** | Pure static analysis; nothing from the repository is executed. Works offline. |
| **Private** | Local-first storage and analysis; cloud AI only on explicit opt-in. |
| **Shareable** | Exportable architecture documentation (Markdown + Mermaid). |

## 4. Core features

The status column is authoritative for what exists. Features marked *planned* do not work yet.

| # | Feature | Description | Status |
|---|---|---|---|
| F1 | Repository import | Import a local folder or clone a GitHub/GitLab repository via URL. | planned (MVP) |
| F2 | Repository scanner | Enumerate files with ignore rules, detect languages, classify files (source, project, config, test, generated, binary). | planned (MVP) |
| F3 | Project & language detection | Detect .NET solutions/projects, npm/yarn/pnpm packages and workspaces, Python projects. | planned (MVP) |
| F4 | C# analysis | Roslyn-based analysis of namespaces, types and their relationships without building the code. | planned (MVP) |
| F5 | TS/JS/Python analysis | Tree-sitter-based module/import analysis and package-manifest analysis. | planned (MVP, basic depth) |
| F6 | Architecture model | Language-independent model of projects, modules, entities, relationships, external dependencies and evidence. | planned (MVP) |
| F7 | Architecture Explorer | Interactive, hierarchical graph (Cytoscape.js) with drill-down, filters, layouts and search. | project-level graph (M1) and layouts with layout switcher (GRAPH-005) implemented; drill-down, filters and search planned (MVP) |
| F8 | Component Inspector | Details panel for the selected node/edge: metrics, relationships, evidence with source locations. | planned (MVP) |
| F9 | External dependencies | NuGet, npm and PyPI dependencies per project, declared versions. | planned (MVP) |
| F10 | Git metadata | Branch, HEAD commit, remotes, per-file/per-project change activity. | planned (MVP) |
| F11 | Documentation export | Markdown report with Mermaid diagrams of the project graph. | planned (MVP, basic) |
| F12 | Local AI assistant | Optional Ollama-backed Q&A and component explanations with citations. | planned (MVP, optional) |
| F13 | Architecture history | Compare architecture snapshots across commits. | planned (post-MVP) |
| F14 | Impact analysis | "What is affected if I change X?" via transitive dependency traversal. | planned (post-MVP) |
| F15 | Coupling metrics | Fan-in/fan-out, instability, cycles, change coupling from Git. | planned (post-MVP, partial in MVP inspector) |
| F16 | Incremental analysis | Re-analyze only what changed. | long-term core goal (post-MVP) |
| F17 | Cloud AI providers | OpenAI-compatible or other providers, opt-in per configuration. | planned (post-MVP) |

Detailed scope boundaries are defined in [MVP_SCOPE.md](MVP_SCOPE.md).

## 5. Key user workflows (summary)

Full step-by-step descriptions are in [USER_WORKFLOWS.md](USER_WORKFLOWS.md).

1. **First look** — import repository → overview (languages, projects, external dependencies) → project graph.
2. **Drill down** — expand a project into modules and types; follow dependencies; inspect evidence.
3. **Find the way in** — search for a component; show its neighborhood (incoming/outgoing, n hops).
4. **Assess coupling / impact** — select a component; see who depends on it, directly and transitively.
5. **Understand change activity** — overlay Git activity on the graph; find hot spots.
6. **Document** — export the current architecture view as Markdown + Mermaid.
7. **Ask** — ask the local AI a question; follow citations into the graph and source.
8. **Re-analyze** — refresh after pulling new commits (MVP); compare with the previous run (post-MVP).

## 6. Differentiation from classic IDEs

| Aspect | Classic IDE (VS, Rider, VS Code) | Codebase Explorer |
|---|---|---|
| Primary unit | File and symbol | System, project, module, relationship |
| Primary task | Edit, build, debug | Understand, explore, document |
| Language scope | Per-language tooling, plugins | One language-independent model across C#, TS, JS, Python |
| Scale | Optimized for the open solution | Hierarchical aggregation for monorepos |
| Requirements | Usually needs restore/build for full fidelity | No build, no restore, no execution |
| Evidence | "Find references" per symbol | Every relationship with source evidence and certainty level |
| History | Git blame/log per file | Change activity aggregated on architecture elements; history comparison (post-MVP) |
| AI | Code completion and chat on open files | Optional Q&A grounded in the architecture model with mandatory citations |

Codebase Explorer complements the IDE: it answers "where and why", the IDE answers "how exactly".
A planned "open in editor" action bridges the two.

## 7. Differentiators

1. **Evidence-based model.** Confirmed vs. syntactic vs. heuristic relationships are first-class
   (see [ADR-007](../decisions/ADR-007-evidence-based-analysis.md)).
2. **Hierarchical exploration by default.** Large repositories are never rendered at once
   (see [GRAPH_ENGINE.md](../architecture/GRAPH_ENGINE.md)).
3. **Polyglot in one graph.** A .NET backend and a TypeScript frontend appear in the same model.
4. **Zero-execution guarantee.** Suitable for untrusted or confidential code
   (see [SECURITY.md](../architecture/SECURITY.md)).
5. **Local-first AI with citations.** AI is optional, local by default, and grounded
   (see [ADR-008](../decisions/ADR-008-hybrid-ai.md)).
6. **Desktop, cross-platform, offline.** No server, no account, no upload.

## 8. Success metrics (product level)

| Metric | Target for MVP |
|---|---|
| Time from import to first project graph (repository with 100 .NET projects, local SSD) | < 30 s |
| Time to project graph for 2,000 projects/packages | < 3 min (first analysis) |
| Share of displayed relationships with at least one evidence record | 100 % |
| Repository code executed during analysis | none (verified by hostile fixtures) |
| Crash-free analysis on the fixture corpus | 100 % |

These targets are validated by the performance and security fixtures described in
[TEST_STRATEGY.md](../implementation/TEST_STRATEGY.md).

## 9. Open product questions

Tracked centrally in [FEATURE_ROADMAP.md — Open product decisions](FEATURE_ROADMAP.md#open-product-decisions).
