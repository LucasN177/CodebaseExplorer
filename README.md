# Codebase Explorer

**See how any codebase is built — visually, locally, with evidence.**

Codebase Explorer is a cross-platform desktop application (Windows, macOS, Linux) that analyzes
software repositories and turns their structure, architecture, dependencies and Git history into an
interactive architecture model. It helps developers understand unfamiliar or complex codebases
quickly — without uploading code and without executing it.

> **Project status: Foundation (milestone M0 complete).**
> The repository contains the complete product and architecture documentation, a buildable .NET 10
> solution and a startable application shell. **No analysis, graph, Git or AI features are
> implemented yet.** See [Project status](#project-status).

---

## Vision

Developers spend much of their time reconstructing how a system fits together. Codebase Explorer
answers the architectural questions directly:

- How is this repository organized? Which projects and modules exist?
- Which languages and frameworks are used?
- How do components, classes and services depend on each other? Where are the boundaries?
- Which external packages does it rely on? What is tightly coupled?
- What is affected if I change a component? How has the architecture evolved?

Every relationship shown is backed by **evidence** (file, line range, analyzer rule, certainty
level). AI is an optional assistant grounded in these facts — never a replacement for deterministic
analysis. Full vision: [docs/product/VISION.md](docs/product/VISION.md).

## Main features

| Feature | Status |
|---|---|
| App shell: navigation, Start page, Settings (theme System/Light/Dark, local data directory) | ✅ available |
| Architecture Explorer page | 🧩 placeholder only (no graph yet) |
| Import local repositories and clone GitHub/GitLab remotes | ⏳ planned (M1 local, M5 remote) |
| Repository scanner, language and project detection | ⏳ planned (M1/M4) |
| C# analysis with Roslyn (no build required) | ⏳ planned (M1 projects, M3 types) |
| TypeScript, JavaScript and Python analysis (Tree-sitter, manifests) | ⏳ planned (M4) |
| Interactive, hierarchical architecture graph (Cytoscape.js) | ⏳ planned (M1–M4) |
| Component Inspector with evidence and source snippets | ⏳ planned (M3) |
| Local persistence (SQLite) and analysis history | ⏳ planned (M2) |
| Git metadata and change-activity overlays | ⏳ planned (M5) |
| Markdown/Mermaid architecture export | ⏳ planned (M6) |
| Optional local AI (Ollama) with cited answers | ⏳ planned (M7) |

Scope details: [docs/product/MVP_SCOPE.md](docs/product/MVP_SCOPE.md).

## Screenshots

> Screenshots will be added once the Architecture Explorer renders real analysis results (M1).
> Until then no screenshots are published, to avoid showing mock-ups as product features.

| Placeholder | Planned content |
|---|---|
| `docs/assets/screenshot-explorer.png` | Project-level dependency graph |
| `docs/assets/screenshot-inspector.png` | Component Inspector with evidence |

## Principles

- **Visual-first** — the graph is the primary interface.
- **Evidence over assertion** — confirmed, syntactic and heuristic relationships are distinguished.
- **Local-first** — data stays on your machine; cloud AI only after explicit configuration.
- **Never executes repository code** — no builds, restores, scripts or MSBuild evaluation.
- **Universal** — one language-independent model; C#, TypeScript, JavaScript, Python first.
- **Scales** — hierarchical aggregation for monorepos; incremental analysis as a long-term goal.

## Tech stack

| Area | Technology |
|---|---|
| Core | C#, .NET 10, Clean Architecture, modular monolith, DI, async/await |
| Desktop | Avalonia UI 12 (Fluent, light/dark), CommunityToolkit.Mvvm |
| Static analysis | Roslyn (C#, without MSBuild evaluation), Tree-sitter (TS/JS/Python, pending spike), manifest parsers |
| Graph | Cytoscape.js, TypeScript, Vite, Avalonia WebView (pending spike) |
| Storage | SQLite, Entity Framework Core 10, migrations |
| Git | LibGit2Sharp (initial adapter behind an abstraction) |
| AI (optional) | Microsoft.Extensions.AI, Ollama; cloud providers opt-in (post-MVP) |
| Testing | xunit.v3 on Microsoft.Testing.Platform, NSubstitute, fixture repositories |

Only Avalonia, CommunityToolkit.Mvvm, Microsoft.Extensions.* (DI/logging), xunit.v3 and
NSubstitute are referenced today. All other technologies are planned and listed with verification
status in [SOLUTION_STRUCTURE.md §6](docs/architecture/SOLUTION_STRUCTURE.md#6-dependencies).

## Architecture overview

```mermaid
flowchart TB
    Desktop["Desktop (Avalonia, MVVM, composition root)"] --> Application
    Desktop --> Contracts
    Application["Application (use cases, ports)"] --> Domain["Domain (architecture model)"]
    Application --> Contracts["Contracts (DTOs)"]
    Analysis --> Application
    Graph --> Application
    Git --> Application
    AI --> Application
    Storage --> Application
    Infrastructure --> Application
```

Technical modules implement Application ports and never reference each other; the rules are
enforced by architecture tests. Details: [docs/architecture/SYSTEM_ARCHITECTURE.md](docs/architecture/SYSTEM_ARCHITECTURE.md).

## Project status

| Milestone | Status |
|---|---|
| M0 Foundation — docs, solution, app shell | ✅ done |
| M1 Vertical slice — local repo → .NET projects → project graph | 🚧 in progress |
| M2 Persistence · M3 Deep C# & Inspector · M4 Polyglot & scale | ⏳ planned |
| M5 Git · M6 Export · M7 Local AI · M8 Release | ⏳ planned |

What exists today: 10 source projects and 6 test projects, central package management, module
registration seams, a composition root, the Avalonia shell, and the language-independent domain
model (stable IDs, projects, modules, entities, evidence-backed relationships, analysis runs and
model validation; tasks CORE-001 … CORE-004), the application result, error and progress types
(CORE-005), and the first analysis building blocks: safe repository file access, a repository
scanner with `.gitignore` support, and language/project-marker detection (REPO-001 … REPO-004,
including the `dotnet-layered` test fixture), and the analysis pipeline with a static .NET
project-system analyzer that builds the model of .NET repositories — projects, solution folders,
project/package/framework references with evidence — without running MSBuild (ANALYSIS-001 …
ANALYSIS-004), the project-level graph projection with its JSON wire format (GRAPH-002), and the
use cases that tie them together — import a local folder, then query its project graph (CORE-006;
models are kept in memory for the session until persistence arrives in M2). None of this is
reachable from the UI yet. The graph web view (TypeScript + Cytoscape.js, GRAPH-003) is built and
shipped with the app, but the host control that displays it inside the window comes next (GRAPH-004).

## Roadmap

See [docs/product/FEATURE_ROADMAP.md](docs/product/FEATURE_ROADMAP.md) and
[docs/implementation/IMPLEMENTATION_PLAN.md](docs/implementation/IMPLEMENTATION_PLAN.md).
Open product decisions (licensing, SSH, secret storage, …) are tracked in the roadmap.

## Getting started

Prerequisites: .NET SDK 10.0.100 or later 10.0.x (see `global.json`).

```bash
dotnet build CodebaseExplorer.slnx
```

```bash
dotnet test --solution CodebaseExplorer.slnx
```

```bash
dotnet run --project src/CodebaseExplorer.Desktop
```

More: [docs/development/DEVELOPMENT_GUIDE.md](docs/development/DEVELOPMENT_GUIDE.md).

## Documentation

| Area | Documents |
|---|---|
| Product | [Vision](docs/product/VISION.md) · [Concept](docs/product/PRODUCT_CONCEPT.md) · [MVP scope](docs/product/MVP_SCOPE.md) · [Roadmap](docs/product/FEATURE_ROADMAP.md) · [User workflows](docs/product/USER_WORKFLOWS.md) |
| Architecture | [System](docs/architecture/SYSTEM_ARCHITECTURE.md) · [Solution structure](docs/architecture/SOLUTION_STRUCTURE.md) · [Domain model](docs/architecture/DOMAIN_MODEL.md) · [Database](docs/architecture/DATABASE_SCHEMA.md) · [Analysis engine](docs/architecture/ANALYSIS_ENGINE.md) · [Graph engine](docs/architecture/GRAPH_ENGINE.md) · [Git](docs/architecture/GIT_INTEGRATION.md) · [AI](docs/architecture/AI_ARCHITECTURE.md) · [Security](docs/architecture/SECURITY.md) |
| Decisions | [ADR index](docs/decisions/README.md) (ADR-001 … ADR-009) |
| Implementation | [Plan](docs/implementation/IMPLEMENTATION_PLAN.md) · [Milestones](docs/implementation/MILESTONES.md) · [Tasks](docs/implementation/TASK_BREAKDOWN.md) · [Test strategy](docs/implementation/TEST_STRATEGY.md) · [Definition of Done](docs/implementation/DEFINITION_OF_DONE.md) |
| Development | [Guide](docs/development/DEVELOPMENT_GUIDE.md) · [Coding standards](docs/development/CODING_STANDARDS.md) · [Git workflow](docs/development/GIT_WORKFLOW.md) · [AI agent guidelines](docs/development/AI_AGENT_GUIDELINES.md) · [CLAUDE.md](CLAUDE.md) |

## License

Proprietary — all rights reserved (see [LICENSE](LICENSE)). The final licensing model is an open
product decision (OPD-01).
