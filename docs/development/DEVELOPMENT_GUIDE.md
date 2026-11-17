# Development Guide — Codebase Explorer

> Status: **binding** · Last reviewed: 2026-11-17

## 1. Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.100 or later 10.0.x feature band (`global.json`, `rollForward: latestFeature`) | Verified with 10.0.402. |
| IDE | JetBrains Rider, Visual Studio 2026, or VS Code + C# Dev Kit | Must support `.slnx` and Microsoft.Testing.Platform. |
| Git | any recent version | |
| Node.js + npm | required from task GRAPH-003 on, only for the graph web view | Version pinned in GRAPH-003. |
| Platform runtimes | WebView runtime per OS (determined in GRAPH-001) | e.g. WebView2 on Windows. |

## 2. Getting started

```bash
git clone <repository-url>
cd CodebaseExplorer
dotnet build CodebaseExplorer.slnx
dotnet test --solution CodebaseExplorer.slnx
dotnet run --project src/CodebaseExplorer.Desktop
```

Local application data (database, clones, logs) is stored under the per-user data directory
shown on the Settings page (`%LOCALAPPDATA%\CodebaseExplorer`, `~/Library/Application Support/CodebaseExplorer`,
or `$XDG_DATA_HOME/CodebaseExplorer` / `~/.local/share/CodebaseExplorer`). Delete it to reset the app.

## 3. Feature workflow

Every feature follows these ten steps. AI agent sessions follow the same workflow (see
[AI_AGENT_GUIDELINES.md](AI_AGENT_GUIDELINES.md)).

1. **Select a task** from [TASK_BREAKDOWN.md](../implementation/TASK_BREAKDOWN.md). Check that its
   prerequisites are `done`. Set its status to `in progress`.
2. **Read the relevant documentation**: the task's referenced docs, the architecture document of
   the affected module(s), related ADRs, `CLAUDE.md`, and the existing code of the affected modules.
3. **Write an implementation plan**: steps, files to create/change, ports/DTOs involved, tests to
   write, docs to update, open questions. For larger tasks, put the plan in the PR description
   before coding.
4. **Identify affected modules** and check the plan against the dependency rules
   (SYSTEM_ARCHITECTURE.md §3). If the plan needs a rule change, stop and write/update an ADR first.
5. **Implement** in small, reviewable steps (each step compiles and keeps tests green).
6. **Write tests** for the new behavior at the right level (TEST_STRATEGY.md §5) — ideally alongside
   step 5, not afterwards.
7. **Build the whole solution**: `dotnet build CodebaseExplorer.slnx` (0 warnings, 0 errors).
8. **Run tests**: at least the affected test projects during development, the full suite before
   finishing: `dotnet test --solution CodebaseExplorer.slnx`.
9. **Update documentation**: architecture docs, schema, README status, ADRs — whatever the change
   made outdated.
10. **Update task status** in TASK_BREAKDOWN.md (and milestone status if applicable), then open
    the PR according to [GIT_WORKFLOW.md](GIT_WORKFLOW.md) and check the
    [Definition of Done](../implementation/DEFINITION_OF_DONE.md).

## 4. Dependency policy

1. Prefer the BCL and already-referenced packages. Every new package needs a reason.
2. Before adding a package, verify on nuget.org (or npm): latest stable version, target framework
   compatibility with `net10.0`, license, maintenance activity, native assets for `win-x64`,
   `osx-arm64`, `linux-x64` where relevant. **Never invent versions.**
3. Add the version to `Directory.Packages.props` (never in a `.csproj`), reference the package only
   in the module that owns it (SYSTEM_ARCHITECTURE.md §4), and record it in
   SOLUTION_STRUCTURE.md §6 with the verification date.
4. Runtime-aligned packages (Microsoft.Extensions.DependencyInjection/Logging/…, EF Core) stay on the
   **10.0.x** line while the solution targets net10.0. `Microsoft.Extensions.AI*` is versioned
   independently (10.x minor releases such as 10.11.0) and is exempt, as long as its dependencies
   resolve to 10.0.x runtime-aligned packages.
5. Packages that execute or evaluate repository content (e.g. MSBuild workspace/locator) are
   forbidden (SECURITY.md).

## 5. Working with the architecture

- **Where does new code go?** Domain concept → Domain. Orchestration of modules → Application use
  case. Technology-specific implementation → the owning module behind an Application port.
  Presentation → Desktop ViewModel/View. Wire format → Contracts.
- **New module?** Requires an ADR (it changes the architecture) and an update of
  `ProjectReferenceRulesTests`.
- **Registration:** add services in the module's `DependencyInjection` class; the composition root
  only calls `Add<Module>Module()`.

## 6. Database migrations (from STORE-003)

```bash
dotnet tool restore            # once a tool manifest exists (added in STORE-003)
dotnet ef migrations add <Name> --project src/CodebaseExplorer.Storage
```

Rules: one migration per schema change; never edit applied migrations; update
DATABASE_SCHEMA.md in the same change; add a migration test.

## 7. Graph web view (from GRAPH-003)

The TypeScript project lives in `src/CodebaseExplorer.GraphView.Web` (Node.js with npm; developed
with Node 26). Only needed when changing the graph web view:

```bash
cd src/CodebaseExplorer.GraphView.Web
npm ci
npm test
npm run build
```

`npm run build` writes the bundle to `src/CodebaseExplorer.Desktop/GraphView/`; commit it together
with the source change. The .NET test `GraphViewAssetsTests` fails if the committed bundle does not
match the sources. Details: SOLUTION_STRUCTURE.md §4.

## 8. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `dotnet test` reports "Testing with VSTest target is no longer supported" | Run from the repository root so `global.json` (`test.runner = Microsoft.Testing.Platform`) applies. |
| Test project exits with code 8 | No tests in that project; allowed until CORE-010. |
| Build fails with a style warning as error | Fix the code; do not lower severities without updating CODING_STANDARDS.md. |
| Wrong SDK picked | Check `dotnet --list-sdks`; a 10.0.x SDK ≥ 10.0.100 is required. |
