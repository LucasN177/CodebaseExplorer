# CLAUDE.md — Rules for AI coding sessions on Codebase Explorer

These rules are **binding** for every session. They are short on purpose; the reasoning and
templates are in [docs/development/AI_AGENT_GUIDELINES.md](docs/development/AI_AGENT_GUIDELINES.md).

## Project in one paragraph

Codebase Explorer is a cross-platform Avalonia desktop app (.NET 10) that statically analyzes
repositories (C#, TypeScript, JavaScript, Python) into a language-independent, evidence-backed
architecture model and visualizes it as an interactive, hierarchical graph (Cytoscape.js in a
WebView). Local-first, never executes repository code, optional local AI with citations.
**Current state:** M0 done; M1 in progress — the language-independent domain model is implemented
(CORE-001 … CORE-004: stable IDs, all model elements, `ArchitectureModel` validation), plus the
application `Result`/`ApplicationError`/`AnalysisProgress` conventions (CORE-005), safe file access,
the repository scanner and language/marker detection (REPO-001 … REPO-003) and the
`dotnet-layered` fixture (REPO-004), and the analysis pipeline with the static .NET project-system
analyzer (ANALYSIS-001 … ANALYSIS-004), and the project-level graph projection plus graph DTOs and
message envelope (GRAPH-002), and the use cases `ImportLocalRepository` / `GetGraphView` with an
model store (CORE-006, persisted since STORE-004), the graph web view bundle (GRAPH-003; after changing
`src/CodebaseExplorer.GraphView.Web` run `npm ci && npm run build` and commit `Desktop/GraphView`), and the
graph host control with its message bridge (`IGraphViewHost`, GRAPH-004), and the Start page import flow
(folder picker, progress, cancel, `RepositorySession`; UI-002), and
the Architecture Explorer with the project graph and a selection panel (UI-003; manually verified on Windows only),
the shell status bar and notifications (`ShellStatus`, UI-001), edge evidence in the side panel
(`GetGraphEdgeDetails`, GRAPH-010), and CI on Windows/macOS/Linux (QA-001).
M2 in progress: the EF Core SQLite context with all model tables and Domain mapping exists (STORE-001/002), the app
creates, backs up and migrates the database at startup (STORE-003; `dotnet tool restore` + `dotnet ef`, see
DEVELOPMENT_GUIDE §6), and saves every run and the current model in SQLite (`IArchitectureModelStore`, STORE-004;
the whole model is read and cached per run, slice queries follow in M4), and stores user settings such as the theme
(`IUserSettingsStore`, STORE-005); test projects without tests fail the run (CORE-010). No language analyzer, Git or AI
functionality exists yet. Next: the remaining M2
tasks in [TASK_BREAKDOWN.md](docs/implementation/TASK_BREAKDOWN.md).

## Commands

```bash
dotnet build CodebaseExplorer.slnx                       # must end with 0 warnings, 0 errors
dotnet test --solution CodebaseExplorer.slnx             # all tests must pass
dotnet test --project tests/<Project> --filter-class "*<Class>"   # focused run
dotnet run --project src/CodebaseExplorer.Desktop        # start the app
```

## Before you change anything

1. **Read the relevant documentation first** for anything beyond a trivial fix: the task in
   `docs/implementation/TASK_BREAKDOWN.md`, the architecture doc of each affected module
   (`docs/architecture/*.md`), related ADRs (`docs/decisions/`), and the existing code.
2. **Make a plan before larger implementations** (more than a few files or any new type/port):
   steps, files, tests, docs. Present it before implementing when the task is non-trivial.
3. **When something is unclear, investigate the existing architecture and code first**; then ask.
   Do not resolve open decisions (FEATURE_ROADMAP.md "Open product decisions", "verification
   items" in architecture docs) implicitly in code.

## Architecture rules (do not violate)

- **Respect the existing architecture.** No fundamental restructuring on your own initiative —
  propose an ADR (status `Proposed`) instead.
- Dependency rules (project references enforced by `ProjectReferenceRulesTests`, the rest by review):
  - `Domain` and `Contracts` reference nothing.
  - `Application` → `Domain`, `Contracts` only.
  - `Infrastructure`, `Storage`, `Analysis`, `Graph`, `Git`, `AI` → `Application`, `Domain`
    (+ `Contracts` for Graph/AI). **Modules never reference each other.**
  - `Desktop` is the composition root; only `Composition/` touches module types.
- **Domain stays independent of infrastructure**: no UI, EF Core, Roslyn, Tree-sitter, Git or AI
  types or attributes in `Domain`.
- **No business logic in Avalonia views** (XAML or code-behind). **Use MVVM consistently**:
  ViewModels (CommunityToolkit.Mvvm) call Application use cases/ports and use Contracts DTOs.
- **Interfaces at meaningful module boundaries** (Application ports, external systems).
  **No unnecessary abstractions** — no interface per class, no speculative generality.
- EF Core entities never leave `Storage`; the graph uses its own DTOs (`Contracts`).
- Analyzers are independent of each other and emit the common model; every relationship has
  evidence (analyzer, rule, level, location).
- Each module registers its services in its own `DependencyInjection` class.

## Coding rules

- **Async/await for all I/O**; never block on async code.
- **`CancellationToken` on every long-running or I/O operation**, passed through; `IProgress<T>`
  for long operations.
- **Nullable reference types are enabled** — keep it that way; fix warnings, don't suppress them.
- Warnings are errors. Follow `.editorconfig` and [CODING_STANDARDS.md](docs/development/CODING_STANDARDS.md).
- New packages: version in `Directory.Packages.props` only, verified on nuget.org/npm (never
  invented), recorded in SOLUTION_STRUCTURE.md §6, referenced only by the owning module.

## Security rules (release blockers)

- Repository content is **untrusted**. **Never execute repository code**: no build/restore/scripts,
  no `MSBuildWorkspace`/`Microsoft.Build.Locator`, no loading of analyzers/generators/assemblies
  from repositories, no `Process.Start` in Analysis/Git/AI/Storage.
- Read repository files only through the containment-checking file reader; hardened XML/JSON parsing.
- No outbound network unless the user started a clone or explicitly configured an AI provider;
  cloud AI only with explicit configuration and consent. AI never modifies code.
- Never log source content or secrets. Details: [SECURITY.md](docs/architecture/SECURITY.md).

## Scope and honesty

- **Implement only the current task.** Do not add features outside it — list them as follow-ups.
- **No fake implementations or simulated results** (hard-coded graphs, invented metrics, pretend
  analysis, mocked AI answers in production code). Test doubles belong in tests only.
- **Small, verifiable steps**; each step compiles and keeps tests green.
- Report honestly: failing tests, skipped steps, unverified assumptions.

## Definition of done for every change

1. **Tests for new functionality** at the right level ([TEST_STRATEGY.md](docs/implementation/TEST_STRATEGY.md)).
2. **Build the entire solution** — 0 warnings, 0 errors.
3. **Run the relevant tests**, and the full suite before finishing.
4. **Keep documentation current** — update every doc the change makes outdated; README must never
   present planned features as available.
5. **Record architecture decisions in ADRs** (`docs/decisions/`, next number, template in its README).
6. **Update the task status** in TASK_BREAKDOWN.md.
Full checklist: [DEFINITION_OF_DONE.md](docs/implementation/DEFINITION_OF_DONE.md).

## Feature workflow (summary)

Select task → read docs → plan → identify modules → implement → write tests → build → test →
update docs → update task status. Details: [DEVELOPMENT_GUIDE.md §3](docs/development/DEVELOPMENT_GUIDE.md#3-feature-workflow).

## Where things are

| Need | Look at |
|---|---|
| What to build next | `docs/implementation/TASK_BREAKDOWN.md`, `IMPLEMENTATION_PLAN.md` |
| Product scope | `docs/product/MVP_SCOPE.md` |
| Module responsibilities & dependency rules | `docs/architecture/SYSTEM_ARCHITECTURE.md` |
| Project layout, packages | `docs/architecture/SOLUTION_STRUCTURE.md` |
| Model, IDs, evidence levels | `docs/architecture/DOMAIN_MODEL.md` |
| Tables | `docs/architecture/DATABASE_SCHEMA.md` |
| Analyzer pipeline | `docs/architecture/ANALYSIS_ENGINE.md` |
| Graph projection & web view protocol | `docs/architecture/GRAPH_ENGINE.md` |
| Composition root | `src/CodebaseExplorer.Desktop/Composition/CompositionRoot.cs` |
| Architecture tests | `tests/CodebaseExplorer.IntegrationTests/Architecture/` |
| Fixture repositories | `tests/CodebaseExplorer.TestRepositories/Repositories/` |
