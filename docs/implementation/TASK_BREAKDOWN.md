# Task Breakdown — Codebase Explorer

> Status: **living document** · Last reviewed: 2026-11-17

Tasks are small enough to be handed to a single Claude Code session. Work through them using the
workflow in [DEVELOPMENT_GUIDE.md](../development/DEVELOPMENT_GUIDE.md#3-feature-workflow) and finish
each with the [Definition of Done](DEFINITION_OF_DONE.md).

## Conventions

| Field | Meaning |
|---|---|
| **ID** | `<AREA>-<NNN>`; never reused, gaps are allowed (numbers of dropped or merged tasks stay unused). Areas: `CORE` (domain/application foundation), `REPO` (repository import & scanning), `ANALYSIS`, `STORE` (storage), `GRAPH`, `UI`, `GIT`, `EXPORT`, `AI`, `QA` (quality, CI, packaging). |
| **Priority** | `P0` required for its milestone · `P1` should be in the milestone · `P2` nice to have · `P3` post-MVP |
| **Status** | `todo` · `in progress` · `done` · `blocked` (with reason). Update in the same commit as the work. |
| **Milestone** | See [MILESTONES.md](MILESTONES.md). |
| **Spike** | Time-boxed investigation; output is a written result (doc/ADR update) plus minimal proof code, not production code. |

Status overview (keep in sync):

| Milestone | Tasks | Done |
|---|---|---|
| M0 Foundation | (initialization) | ✅ |
| M1 Vertical slice | 22 | 10 |
| M2 Persistence | 10 | 0 |
| M3 Deep C# & Inspector | 9 | 0 |
| M4 Polyglot & scale | 11 | 0 |
| M5 Git | 7 | 0 |
| M6 Export | 2 | 0 |
| M7 Local AI | 7 | 0 |
| M8 Hardening & release | 4 | 0 |
| Post-MVP | 1 | 0 |

---

## M1 — Vertical slice: local repository → .NET projects → project graph

### CORE-001 — Stable identifier types and natural-key hashing
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** —
- **Projects:** Domain, Domain.Tests
- **Description:** Implement `RepositoryId`, `AnalysisRunId` (Guid v7) and `ElementId` (first 16 bytes of SHA-256 of the UTF-8 natural key) as `readonly record struct`s, plus `NaturalKey` builders for files, projects, modules, entities, external dependencies, relationships and evidence (DOMAIN_MODEL.md §3).
- **Acceptance criteria:**
  - `ElementId.FromNaturalKey(key)` is deterministic across processes and platforms; hex rendering is 32 lowercase chars.
  - Natural-key builders apply the normalization rules (path separators, NuGet/PyPI lower-casing, PEP 503).
  - IDs of different kinds cannot be assigned to each other without explicit conversion.
  - Unit tests cover determinism, normalization and known test vectors.

### CORE-002 — Domain: repository, files, projects, modules
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** CORE-001
- **Projects:** Domain, Domain.Tests
- **Description:** Implement `Repository`, `RepositoryLocation`, `RepositoryFile`, `CodeProject`, `SolutionMembership`, `CodeModule` and value objects `RelativePath`, `ContentHash`, `LanguageId`, `SourceLocation`, with the enums from DOMAIN_MODEL.md §4–5.
- **Acceptance criteria:**
  - Invariants enforced in constructors/factories (e.g. `RelativePath` rejects rooted paths and `..`; `SourceLocation` start ≤ end, 1-based).
  - No references to infrastructure packages; no serialization/EF attributes.
  - Unit tests for every invariant (first tests in `Domain.Tests`).

### CORE-003 — Domain: entities, relationships, evidence, external dependencies, runs
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** CORE-002
- **Projects:** Domain, Domain.Tests
- **Description:** Implement `CodeEntity`, `CodeRelationship`, `AnalysisEvidence`, `ExternalDependency`, `AnalysisRun` (incl. status state machine), `AnalysisDiagnostic`, `EvidenceLevel`, `RelationshipKind`, `ElementReference`.
- **Acceptance criteria:**
  - A relationship cannot exist without ≥ 1 evidence; `Level` is derived as max of evidence levels; `Occurrences` equals evidence count.
  - `AnalysisRun` rejects invalid state transitions.
  - Unit tests for invariants and state machine.

### CORE-004 — Domain: ArchitectureModel snapshot and validation
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** CORE-003
- **Projects:** Domain, Domain.Tests
- **Description:** Read-only `ArchitectureModel` aggregating all elements of one repository/run with lookup by ID and cross-element validation (endpoints exist, containment resolves, natural-key collisions).
- **Acceptance criteria:**
  - `ArchitectureModel.Validate()` returns all violations (not only the first).
  - Construction from inconsistent data reports specific violation codes.
  - Tests cover each violation type.

### CORE-005 — Application: results, errors, progress, cancellation conventions
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** —
- **Projects:** Application, IntegrationTests
- **Description:** Introduce `Result<T>`/`ApplicationError` (code, message, details) for expected failures, `AnalysisProgress` (stage, processed, total, current item), and document the conventions in CODING_STANDARDS.md if they deviate.
- **Acceptance criteria:**
  - Types are small, immutable and used by at least the M1 use case signatures.
  - No exceptions used for expected validation failures in new use cases.
- **Note (done 2026-11-17):** types and conventions are implemented and tested; no use case or port
  existed yet, so "used by the M1 use case signatures" is enforced through CODING_STANDARDS.md §4
  and checked in CORE-006 and ANALYSIS-001.

### CORE-006 — Use cases: ImportLocalRepository and GetGraphView (session model cache)
- **Priority:** P0 · **Milestone:** M1 · **Status:** todo
- **Prerequisites:** CORE-004, CORE-005, ANALYSIS-001, GRAPH-002
- **Projects:** Application, IntegrationTests
- **Description:** `ImportLocalRepository` validates the path, runs `IRepositoryAnalyzer`, and keeps the resulting `ArchitectureModel` in a session-scoped `IArchitectureModelCache` (Application, in memory, replaced by the SQLite-backed store in STORE-004). `GetGraphView` reads from the cache and calls `IGraphProjector`. One run per repository at a time.
- **Acceptance criteria:**
  - End-to-end test: fixture `dotnet-layered` → import → project-level `GraphViewDto` matching `EXPECTED.md`.
  - Cancellation stops the run and leaves no model in the cache.
  - Concurrent import of the same repository is rejected with a typed error.

### REPO-001 — Repository location validation and safe file reader
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** CORE-002
- **Projects:** Application (port), Analysis, Analysis.Tests
- **Description:** Validate a local repository root (exists, directory, not a FS root, not inside the app data directory). Implement `IRepositoryFileReader` enforcing real-path containment, symlink policy and size limits (SECURITY.md §3).
- **Acceptance criteria:**
  - Reading a path that resolves outside the root (via `..` or symlink) fails with a typed error.
  - Files above the configured limit are not read; a diagnostic is produced.
  - Tests for traversal, symlink escape (skipped on platforms/CI agents without symlink permission, with reason), size limit.
- **Note:** Symbolic-link tests skip with a reason where the account may not create links (Windows without developer mode); Windows junction tests cover the same link logic without privileges.

### REPO-002 — Repository scanner
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** REPO-001
- **Projects:** Analysis, Analysis.Tests
- **Description:** Enumerate files with built-in excludes and `.gitignore` semantics (in-process), classify files, detect binaries, compute SHA-256 and line counts, report progress and honor cancellation (ANALYSIS_ENGINE.md Stage 1).
- **Acceptance criteria:**
  - Ignored directories are not descended into; counts appear in statistics.
  - `.gitignore` negation (`!`) and directory patterns behave as in Git for the fixture cases.
  - Output is sorted and deterministic.
  - Scanning 10,000 synthetic files completes in < 5 s on a developer machine (smoke benchmark, not a CI gate).
- **Note:** Two-phase scan (sequential enumeration, parallel inspection); benchmark 2.7 s for 10,000 files (Debug, Windows). Language and marker-based classification follows in REPO-003.

### REPO-003 — Language and project-marker detection
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** REPO-002
- **Projects:** Analysis, Analysis.Tests
- **Description:** Map extensions/file names/shebangs to `LanguageId`; detect project markers (`*.sln`, `*.slnx`, `*.csproj`, `*.fsproj`, `*.vbproj`, `package.json`, `pyproject.toml`, `requirements*.txt`, `setup.cfg`, `setup.py`).
- **Acceptance criteria:**
  - Table-driven tests for all supported extensions and markers.
  - Unknown files are `unknown`, never guessed.

### REPO-004 — Fixture repository `dotnet-layered`
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** —
- **Projects:** TestRepositories
- **Description:** Create the first fixture: `.slnx` + `.sln`, 5 projects (Domain, Application, Infrastructure, Web, Tests) with `ProjectReference`s, central package management, one `Directory.Build.props`, one simple `'$(Prop)' == 'literal'` condition, one `ProjectReference` to a non-existent project file (negative case for ANALYSIS-004), a few types per project. Add `EXPECTED.md` listing projects, project references (with file/line) and package references.
- **Acceptance criteria:**
  - Fixture files are excluded from compilation (verified by a clean solution build).
  - `EXPECTED.md` is complete and is the oracle for ANALYSIS-002…004 tests.
- **Note:** EXPECTED.md positions were taken from the files with grep; a test asserts the fixture contains no CR bytes. How ANALYSIS-004 represents the missing Shop.Legacy target is left to that task (the observable outcome is fixed in EXPECTED.md §4).

### ANALYSIS-001 — Analyzer contracts, pipeline orchestrator and linker skeleton
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** CORE-004, REPO-002
- **Projects:** Application (port `IRepositoryAnalyzer`), Analysis, Analysis.Tests
- **Description:** Implement `IProjectSystemAnalyzer`, `ILanguageAnalyzer`, `AnalyzerFragment`, the stage orchestrator (prepare → scan → project systems → languages → link → validate), failure isolation, progress and DI registration via `AddAnalysisModule()`.
- **Acceptance criteria:**
  - An analyzer exception becomes an `Error` diagnostic and a `PartiallySucceeded` run; other analyzers' results survive.
  - Linker merges fragments by natural key and deduplicates relationships, merging evidence.
  - Tests use small test analyzers (test doubles) — no fake production analyzers.
- **Note:** Fragments carry key-derived Domain objects instead of a separate draft model; ANALYSIS_ENGINE.md §3 rule 3 was corrected accordingly. No production analyzer is registered yet (first: ANALYSIS-004).

### ANALYSIS-002 — Solution parser (.sln, .slnx)
- **Priority:** P0 · **Milestone:** M1 · **Status:** todo
- **Prerequisites:** ANALYSIS-001, REPO-004
- **Projects:** Analysis, Analysis.Tests
- **Description:** Static parsers for the text `.sln` format and XML `.slnx` format: project paths, solution folders → `SolutionMembership`.
- **Acceptance criteria:**
  - Both formats of the fixture produce identical memberships.
  - Malformed files produce diagnostics, not exceptions.
  - XML parsing uses the hardened reader settings (SECURITY.md).

### ANALYSIS-003 — Static .csproj parser with props resolution
- **Priority:** P0 · **Milestone:** M1 · **Status:** todo
- **Prerequisites:** ANALYSIS-001
- **Projects:** Analysis, Analysis.Tests
- **Description:** Parse SDK-style project files and resolve `Directory.Build.props`/`Directory.Packages.props` statically (ANALYSIS_ENGINE.md Stage 2): properties, `ProjectReference`, `PackageReference` + central versions, `FrameworkReference`, simple conditions.
- **Acceptance criteria:**
  - Never invokes MSBuild APIs; no `Microsoft.Build*` package referenced.
  - Unsupported conditions/property functions are reported as diagnostics and treated as unknown.
  - Each extracted reference has file + line/column of its XML element.
  - Tests include an XML bomb / DTD file that is rejected safely.

### ANALYSIS-004 — .NET project dependency extraction with evidence
- **Priority:** P0 · **Milestone:** M1 · **Status:** todo
- **Prerequisites:** ANALYSIS-002, ANALYSIS-003
- **Projects:** Analysis, Analysis.Tests
- **Description:** `DotNetProjectSystemAnalyzer` produces `CodeProject`s, `ProjectReference` and `PackageReference`/`FrameworkReference` relationships with evidence (`dotnet.msbuild-static`, rules `csproj.ProjectReference` etc.).
- **Acceptance criteria:**
  - Output for `dotnet-layered` equals `EXPECTED.md` (projects, edges, evidence locations).
  - Missing referenced project file → `Syntactic` relationship + warning.
  - Analysis twice yields identical models (determinism test).

### GRAPH-001 — Spike: Avalonia WebView + Cytoscape.js on Windows, macOS, Linux
- **Priority:** P0 · **Milestone:** M1 · **Status:** todo · **Spike (time-box 2 days)**
- **Prerequisites:** —
- **Projects:** Desktop (spike branch), docs
- **Description:** Verify `Avalonia.Controls.WebView` (12.1.x) with a bundled static page running Cytoscape.js: local asset loading, .NET→JS and JS→.NET messages, navigation blocking, CSP, runtime prerequisites per OS.
- **Acceptance criteria:**
  - Written result in ADR-006 (update status) and GRAPH_ENGINE.md §6 (concrete interop API).
  - If a platform fails: documented mitigation or fallback proposal for OPD-04.
  - Package added to `Directory.Packages.props` only if adopted.

### GRAPH-002 — Graph DTOs and project-level projection
- **Priority:** P0 · **Milestone:** M1 · **Status:** todo
- **Prerequisites:** CORE-004
- **Projects:** Contracts, Application (port `IGraphProjector`), Graph, Graph.Tests
- **Description:** Implement `GraphViewDto`, `GraphNodeDto`, `GraphEdgeDto`, `TruncationDto`, message envelope types, System.Text.Json source-generated context; implement L1 projection (projects + aggregated project edges).
- **Acceptance criteria:**
  - Projection is a pure function of model slice + query; tested with hand-built models.
  - Aggregated edge contains count, kinds histogram, max/min level, `isDeclared`.
  - JSON round-trip tests for all DTOs.

### GRAPH-003 — Graph web view project scaffold
- **Priority:** P0 · **Milestone:** M1 · **Status:** todo
- **Prerequisites:** GRAPH-001
- **Projects:** `src/CodebaseExplorer.GraphView.Web` (new, non-.NET), Desktop (build integration), docs
- **Description:** Vite + TypeScript + Cytoscape.js app implementing `graph/load`, `selection/changed`, `ready`, theme messages; build output embedded into Desktop. Decide and document how the .NET build works without Node installed.
- **Acceptance criteria:**
  - `npm ci && npm run build && npm test` succeed; Vitest tests for message validation.
  - Labels rendered as text only; CSP meta tag present; no network requests.
  - SOLUTION_STRUCTURE.md §4 updated with the final build integration.

### GRAPH-004 — Graph host control and message bridge
- **Priority:** P0 · **Milestone:** M1 · **Status:** todo
- **Prerequisites:** GRAPH-001, GRAPH-002, GRAPH-003
- **Projects:** Desktop, IntegrationTests
- **Description:** Avalonia control hosting the WebView, loading bundled assets, serializing DTOs, validating inbound messages, exposing selection as bindable state for ViewModels.
- **Acceptance criteria:**
  - ViewModels interact with an `IGraphViewHost` abstraction, not with the WebView.
  - Inbound messages failing validation are logged and ignored.
  - Navigation to any non-bundled URL is blocked.

### UI-001 — Shell polish and Avalonia 12 developer tooling
- **Priority:** P1 · **Milestone:** M1 · **Status:** todo
- **Prerequisites:** —
- **Projects:** Desktop
- **Description:** Status bar, busy indicator, notification area for errors; evaluate developer tooling for Avalonia 12 (diagnostics/devtools) and document the result in SOLUTION_STRUCTURE.md §6.
- **Acceptance criteria:** Shell shows background-operation state; no business logic in views.

### UI-002 — Import local repository flow
- **Priority:** P0 · **Milestone:** M1 · **Status:** todo
- **Prerequisites:** CORE-006
- **Projects:** Desktop, IntegrationTests
- **Description:** Start page action "Open local folder" using Avalonia's storage provider folder picker; progress panel with stages and cancel; navigate to the explorer on success; errors shown inline.
- **Acceptance criteria:**
  - ViewModel tests (no UI) cover success, cancellation and error paths with substituted use cases.
  - UI thread is never blocked by analysis work.

### UI-003 — Architecture Explorer page with project graph
- **Priority:** P0 · **Milestone:** M1 · **Status:** todo
- **Prerequisites:** GRAPH-004, UI-002
- **Projects:** Desktop
- **Description:** Replace the placeholder with the graph host; load the L1 graph for the current repository; show selected node name and kind in a simple side panel (full inspector in UI-006).
- **Acceptance criteria:**
  - Manual test on all three OSes with `dotnet-layered`: 5 project nodes, correct edges, selection works.
  - Theme switch updates the graph.

### QA-001 — Continuous integration
- **Priority:** P1 · **Milestone:** M1 · **Status:** todo
- **Prerequisites:** —
- **Projects:** repository root (`.github/workflows`), docs
- **Description:** GitHub Actions workflow building and testing on Windows, macOS and Linux; evaluate code coverage with a Microsoft.Testing.Platform extension.
- **Acceptance criteria:** Pull requests show build + test status for 3 OSes; coverage decision documented in TEST_STRATEGY.md.

---

## M2 — Persistence and analysis runs

### STORE-001 — DbContext and persistence for core tables
- **Priority:** P0 · **Milestone:** M2 · **Status:** todo
- **Prerequisites:** CORE-004
- **Projects:** Storage, Storage.Tests, Directory.Packages.props
- **Description:** Add EF Core SQLite (10.0.x, re-verify), `CodebaseExplorerDbContext`, persistence entities and configurations for `Repositories`, `AnalysisRuns`, `AnalysisDiagnostics`, `Files`, `Projects`, `Modules`; BLOB Guid converters; connection pragmas; `IDbContextFactory` registration.
- **Acceptance criteria:**
  - Mapping tests (Domain → record → Domain round-trip) for each table.
  - EF entities are `internal`.

### STORE-002 — Persistence for entities, relationships, evidence, dependencies
- **Priority:** P0 · **Milestone:** M2 · **Status:** todo
- **Prerequisites:** STORE-001
- **Projects:** Storage, Storage.Tests
- **Description:** Tables `CodeEntities`, `CodeEntityDeclarations`, `CodeRelationships` (incl. denormalized container IDs), `AnalysisEvidence`, `ExternalDependencies`; decide on evidence sampling for very high-occurrence relationships (ADR-007).
- **Acceptance criteria:** Round-trip tests; indexes from DATABASE_SCHEMA.md exist (verified via migration SQL test).

### STORE-003 — Migrations, startup migration and backup
- **Priority:** P0 · **Milestone:** M2 · **Status:** todo
- **Prerequisites:** STORE-002
- **Projects:** Storage, Desktop, Storage.Tests
- **Description:** Initial migration, design-time factory, startup `MigrateAsync` with backup of existing DB (keep 3), error page on failure.
- **Acceptance criteria:** Test migrates an empty DB to latest; test verifies backup creation; app starts with an existing DB; database file and data directory are created with user-only permissions where the OS supports it (Unix modes 0700/0600; on Windows the per-user profile ACL applies).

### STORE-004 — Architecture model store (save run, read slices)
- **Priority:** P0 · **Milestone:** M2 · **Status:** todo
- **Prerequisites:** STORE-003, CORE-006
- **Projects:** Application (port `IArchitectureModelStore`), Storage, Storage.Tests, IntegrationTests
- **Description:** Transactional replace of a repository's model per successful run (batched); read slices for graph projection (projects, aggregated relationships by project/module) and inspector queries. Replace the M1 session cache.
- **Acceptance criteria:**
  - Failed/cancelled runs never modify the stored model.
  - Saving the `dotnet-layered` model and reading the L1 slice yields the same graph as M1.
  - Saving 1,000,000 synthetic relationships completes without exceeding 1.5 GB process memory (benchmark, documented result).

### STORE-005 — User settings store and theme persistence
- **Priority:** P0 · **Milestone:** M2 · **Status:** todo
- **Prerequisites:** STORE-003
- **Projects:** Application (port), Storage, Desktop
- **Description:** `UserSettings` table access via `IUserSettingsStore`; persist theme choice and restore at startup.
- **Acceptance criteria:** Theme survives restart; settings keys documented in DATABASE_SCHEMA.md.

### REPO-005 — Recent repositories and reopen without re-analysis
- **Priority:** P0 · **Milestone:** M2 · **Status:** todo
- **Prerequisites:** STORE-004
- **Projects:** Application, Desktop
- **Description:** Start page lists recent repositories (name, path, last run status/date); opening loads the stored model; "Re-analyze" action; "Remove" deletes data (cascade).
- **Acceptance criteria:** Reopen shows the graph without running analysis; remove deletes all rows of the repository and, for app-managed clones (from M5), the clone directory after confirmation.

### UI-004 — Repository overview page
- **Priority:** P1 · **Milestone:** M2 · **Status:** todo
- **Prerequisites:** STORE-004
- **Projects:** Application (query), Contracts, Desktop
- **Description:** Languages (file counts/lines), projects by ecosystem, external dependency count, last run info, warnings count; link to explorer.
- **Acceptance criteria:** Values match fixture expectations; no EF types in ViewModels.

### UI-005 — Analysis run history and diagnostics
- **Priority:** P0 · **Milestone:** M2 · **Status:** todo
- **Prerequisites:** STORE-004
- **Projects:** Application, Desktop
- **Description:** List of runs with status, duration, commit; diagnostics list filterable by severity.
- **Acceptance criteria:** Partially succeeded runs show their errors with analyzer and location.

### CORE-007 — File logging and global exception handling
- **Priority:** P1 · **Milestone:** M2 · **Status:** todo
- **Prerequisites:** —
- **Projects:** Infrastructure, Desktop
- **Description:** Rolling file logs in `<data>/logs` (implementation choice documented; prefer a minimal in-house provider over new dependencies), unhandled exception handlers (AppDomain, TaskScheduler, Avalonia dispatcher), redaction rules.
- **Acceptance criteria:** Crash produces a log entry and a user-facing message; logs never contain file contents.

### CORE-010 — Remove the zero-test exit-code exception
- **Priority:** P1 · **Milestone:** M2 · **Status:** todo
- **Prerequisites:** CORE-002, ANALYSIS-001, GRAPH-002, STORE-001
- **Projects:** tests/Directory.Build.props
- **Description:** Every test project contains tests by now; remove `--ignore-exit-code 8`.
- **Acceptance criteria:** `dotnet test --solution CodebaseExplorer.slnx` passes without the flag.

---

## M3 — Deep C# analysis and Component Inspector

### ANALYSIS-005 — Roslyn syntax analysis (modules and entities)
- **Priority:** P0 · **Milestone:** M3 · **Status:** todo
- **Prerequisites:** ANALYSIS-004
- **Projects:** Analysis, Analysis.Tests, Directory.Packages.props
- **Description:** Add `Microsoft.CodeAnalysis.CSharp` (re-verify 5.x version; **not** `Workspaces.MSBuild`). Parse project `.cs` files; namespaces → `CodeModule` (hierarchy), types and members → `CodeEntity` with declarations (partial types merged).
- **Acceptance criteria:** Fixture expectations for modules/entities met; symbol keys are documentation-comment IDs; no assemblies loaded from the repository.

### ANALYSIS-006 — Roslyn semantic relationships without build
- **Priority:** P0 · **Milestone:** M3 · **Status:** todo
- **Prerequisites:** ANALYSIS-005
- **Projects:** Analysis, Analysis.Tests
- **Description:** Build `CSharpCompilation`s in topological project order with metadata references (SDK reference packs if found, in-repo `CompilationReference`s, cached NuGet assemblies only via existing `project.assets.json`). Extract `Inherits`, `Implements`, `UsesType`, `Instantiates`, `Calls` with evidence levels.
- **Acceptance criteria:**
  - Without SDK packs present, analysis still succeeds with more `Syntactic` evidence (test with packs disabled).
  - Memory released per project (compilations not retained after linking).
  - Fixture expectations met, including cross-project relationships.

### ANALYSIS-007 — DI registration heuristics (C#)
- **Priority:** P2 · **Milestone:** M3 · **Status:** todo
- **Prerequisites:** ANALYSIS-006
- **Projects:** Analysis, Analysis.Tests
- **Description:** Detect `services.Add{Singleton|Scoped|Transient}<TService, TImpl>()` and common overloads → `RegistersService` with `Heuristic` evidence.
- **Acceptance criteria:** All detections carry rule ids; no false positives on the fixture's negative cases.

### REPO-006 — Fixture `dotnet-advanced`
- **Priority:** P1 · **Milestone:** M3 · **Status:** todo
- **Prerequisites:** REPO-004
- **Projects:** TestRepositories
- **Description:** Partial classes, nested types, generics, multi-targeting, conditional references, missing project reference, central package management with `VersionOverride`.
- **Acceptance criteria:** `EXPECTED.md` complete; used by ANALYSIS-005/006 tests and as additional regression input for the M1 parsers (ANALYSIS-003/004).

### QA-003 — Hostile .NET fixtures proving zero execution
- **Priority:** P0 · **Milestone:** M3 · **Status:** todo
- **Prerequisites:** ANALYSIS-006
- **Projects:** TestRepositories, IntegrationTests
- **Description:** .NET hostile fixtures: MSBuild `Exec` in targets and `Directory.Build.props`, property functions, analyzer/source-generator package references, all writing marker files if executed. Integration tests run the full pipeline and assert that no marker file exists and no child process was started. Provides a reusable harness (marker check + process-spawn guard) for the ecosystem-specific hostile tests in ANALYSIS-009 (npm), ANALYSIS-011 (Python) and GIT-001 (Git hooks/fsmonitor).
- **Acceptance criteria:** Tests run in CI on all OSes; SECURITY.md §6 references them; harness documented in TEST_STRATEGY.md.

### GRAPH-005 — Layouts and visual encoding
- **Priority:** P0 · **Milestone:** M3 · **Status:** todo
- **Prerequisites:** GRAPH-004
- **Projects:** GraphView.Web, Desktop
- **Description:** Hierarchical and force layouts, layout switcher, visual encoding from GRAPH_ENGINE.md §9; license review for layout extensions (elkjs EPL-2.0).
- **Acceptance criteria:** Layout choice persisted; license review documented in SOLUTION_STRUCTURE.md §6.2.

### GRAPH-006 — Module and entity levels with expand/collapse
- **Priority:** P0 · **Milestone:** M3 · **Status:** todo
- **Prerequisites:** GRAPH-002, STORE-004, ANALYSIS-005
- **Projects:** Graph, Graph.Tests, Storage (slice queries), Desktop, GraphView.Web
- **Description:** L2/L3 projection, compound nodes, `graph/patch` messages, deepest-visible-ancestor edge attachment.
- **Acceptance criteria:** Expanding and collapsing is reversible and yields identical views; projection tests for mixed levels.

### UI-006 — Component Inspector
- **Priority:** P0 · **Milestone:** M3 · **Status:** todo
- **Prerequisites:** STORE-004, GRAPH-006
- **Projects:** Application (query), Contracts, Desktop
- **Description:** Panel with element details, metrics (fan-in/out, size), incoming/outgoing relationships grouped by kind with level badges, "show in graph".
- **Acceptance criteria:** Works for projects, modules, entities, external dependencies and aggregated edges.

### UI-007 — Evidence list and source snippet view
- **Priority:** P0 · **Milestone:** M3 · **Status:** todo
- **Prerequisites:** UI-006
- **Projects:** Application, Desktop
- **Description:** Evidence records with analyzer, rule, level, location; read-only snippet (± 5 lines) read through `IRepositoryFileReader`; copy path; plain-text rendering.
- **Acceptance criteria:** Snippet shows the exact line range; missing/changed files are reported, not crashed.

---

## M4 — Polyglot analysis and large repositories

### ANALYSIS-008 — Spike: Tree-sitter binding and grammars
- **Priority:** P0 · **Milestone:** M4 · **Status:** todo · **Spike (time-box 3 days)**
- **Prerequisites:** —
- **Projects:** Analysis (spike branch), docs
- **Description:** Evaluate `TreeSitter.DotNet` 1.3.0 and alternatives: bundled grammars (TS, TSX, JS, Python), native binaries for win-x64/osx-arm64/linux-x64, parse speed, cancellation/timeouts, behavior on malformed/huge input, maintenance and license.
- **Acceptance criteria:** ADR-004 updated with the decision; crash-containment approach documented (in-process with limits vs. parser host process).

### ANALYSIS-009 — Node project-system analyzer
- **Priority:** P0 · **Milestone:** M4 · **Status:** todo
- **Prerequisites:** ANALYSIS-001, REPO-007
- **Projects:** Analysis, Analysis.Tests
- **Description:** `package.json`, npm/yarn workspaces, `pnpm-workspace.yaml`, `tsconfig.json` (JSONC) incl. `paths`/`references`; workspace dependencies → `ProjectReference`, others → npm `PackageReference`.
- **Acceptance criteria:** Fixture expectations met; scripts are never run (only listed), proven by a hostile fixture with `preinstall`/`postinstall` scripts using the QA-003 harness; deep/huge JSON rejected safely.

### ANALYSIS-010 — TypeScript/JavaScript import analysis
- **Priority:** P0 · **Milestone:** M4 · **Status:** todo
- **Prerequisites:** ANALYSIS-008, ANALYSIS-009
- **Projects:** Analysis, Analysis.Tests
- **Description:** Tree-sitter extraction of imports/exports/requires and top-level declarations; resolution per ANALYSIS_ENGINE.md; modules = directories.
- **Acceptance criteria:** Fixture expectations met incl. path aliases; unresolved specifiers produce diagnostics.

### ANALYSIS-011 — Python project-system analyzer
- **Priority:** P0 · **Milestone:** M4 · **Status:** todo
- **Prerequisites:** ANALYSIS-001, REPO-007
- **Projects:** Analysis, Analysis.Tests, Directory.Packages.props (TOML parser if needed)
- **Description:** `pyproject.toml` (PEP 621, Poetry), `requirements*.txt`, `setup.cfg`; package roots and `src/` layout; `setup.py` detected only.
- **Acceptance criteria:** Fixture expectations met; a hostile `setup.py` fixture proves nothing is executed (QA-003 harness); TOML library choice verified and documented (or minimal in-house parser justified).

### ANALYSIS-012 — Python import analysis
- **Priority:** P0 · **Milestone:** M4 · **Status:** todo
- **Prerequisites:** ANALYSIS-008, ANALYSIS-011
- **Projects:** Analysis, Analysis.Tests
- **Description:** Tree-sitter extraction of `import`/`from … import`, relative imports, top-level classes/functions; resolution against package roots and declared dependencies (heuristic name mapping).
- **Acceptance criteria:** Fixture expectations met; heuristic mappings carry `Heuristic` level.

### ANALYSIS-013 — Determinism and limits hardening
- **Priority:** P1 · **Milestone:** M4 · **Status:** todo
- **Prerequisites:** ANALYSIS-010, ANALYSIS-012
- **Projects:** Analysis, Analysis.Tests
- **Description:** Determinism test across all fixtures; limit enforcement (file count, depth, parse timeouts) with diagnostics; statistics per stage.
- **Acceptance criteria:** All fixtures analyzed twice produce byte-identical serialized models.

### REPO-007 — Polyglot fixtures
- **Priority:** P0 · **Milestone:** M4 · **Status:** todo
- **Prerequisites:** —
- **Projects:** TestRepositories
- **Description:** `ts-monorepo-workspaces`, `js-commonjs`, `python-src-layout`, `mixed-polyglot` (dotnet + ts + python) with `EXPECTED.md`.
- **Acceptance criteria:** Each fixture's expectations reviewed and complete.

### GRAPH-007 — Filters, search and neighborhood view
- **Priority:** P0 · **Milestone:** M4 · **Status:** todo
- **Prerequisites:** GRAPH-006
- **Projects:** Application, Storage (search query), Graph, Desktop, GraphView.Web
- **Description:** Filters (language, ecosystem, relationship kind, min level, tests/generated/external toggles), `Ctrl/Cmd+K` search, focus + n-hop neighborhood.
- **Acceptance criteria:** Filter combinations covered by projection tests; search returns results < 200 ms on the performance fixture.

### GRAPH-008 — Node budget and truncation
- **Priority:** P0 · **Milestone:** M4 · **Status:** todo
- **Prerequisites:** GRAPH-006
- **Projects:** Graph, Graph.Tests, Desktop
- **Description:** Enforce node budget; top-N by degree + "N more" aggregate nodes; truncation hints in the UI.
- **Acceptance criteria:** No `GraphViewDto` exceeds the budget (property-based/parameterized tests).

### QA-002 — Performance fixture generator and benchmarks
- **Priority:** P0 · **Milestone:** M4 · **Status:** todo
- **Prerequisites:** ANALYSIS-006, STORE-004
- **Projects:** tools/development, TestRepositories, docs
- **Description:** Generator for synthetic repositories (e.g. 2,000 .NET projects, 50k TS files) into a temp folder (not committed); benchmark script measuring analysis time, memory, projection time.
- **Acceptance criteria:** Results recorded in TEST_STRATEGY.md; MVP targets (PRODUCT_CONCEPT.md §8) evaluated.

### GRAPH-009 — Repository level, folder fallback and navigation history
- **Priority:** P0 · **Milestone:** M4 · **Status:** todo
- **Prerequisites:** GRAPH-006
- **Projects:** Graph, Graph.Tests, Desktop, GraphView.Web
- **Description:** L0 repository level (ecosystem/solution/workspace groups), folder-level graph for repositories without recognized projects (USER_WORKFLOWS.md WF-01), breadcrumbs and *Back* navigation restoring the previous view state (WF-03).
- **Acceptance criteria:** L0 projection tests; a fixture without projects shows a folder graph; Back restores expansion state and filters.

---

## M5 — Git integration

### GIT-006 — Spike: LibGit2Sharp verification
- **Priority:** P0 · **Milestone:** M5 · **Status:** todo · **Spike (time-box 1 day)**
- **Prerequisites:** —
- **Projects:** Git (spike), docs
- **Description:** Verify LibGit2Sharp 0.32.x: native binaries on all RIDs, no hook/fsmonitor execution, HTTPS clone with token, shallow clone, performance of history walk; SSH status (OPD-02).
- **Acceptance criteria:** GIT_INTEGRATION.md §3 table updated with verified facts.

### GIT-001 — Git ports and LibGit2Sharp adapter (detect, snapshot)
- **Priority:** P0 · **Milestone:** M5 · **Status:** todo
- **Prerequisites:** GIT-006
- **Projects:** Application (ports), Git, IntegrationTests, Directory.Packages.props
- **Description:** `IGitRepositoryReader.TryOpenAsync/ReadSnapshotAsync`; worktrees and `.git` files supported; sanitized remotes.
- **Acceptance criteria:** Tests with `GitRepositoryBuilder`; hostile `.git/config` executes nothing.

### GIT-002 — GitRepositoryBuilder test helper
- **Priority:** P0 · **Milestone:** M5 · **Status:** todo
- **Prerequisites:** GIT-006
- **Projects:** TestRepositories
- **Description:** Fluent builder creating temporary Git repositories with commits, authors, branches, renames; cleans up reliably (read-only files on Windows).
- **Acceptance criteria:** Used by GIT-001/003/004 tests; no nested `.git` committed.

### GIT-003 — HTTPS clone of GitHub/GitLab remotes
- **Priority:** P0 · **Milestone:** M5 · **Status:** todo
- **Prerequisites:** GIT-001
- **Projects:** Application, Git, IntegrationTests
- **Description:** URL validation/sanitization, clone into `<data>/clones/<host>/<owner>/<repo>`, progress and cancellation, token via credentials callback (in-memory only until OPD-03), update (fetch + checkout) for app-managed clones, no submodules.
- **Acceptance criteria:** Rejected URL schemes tested; cancellation leaves no half-cloned directory registered.

### GIT-004 — File activity statistics
- **Priority:** P0 · **Milestone:** M5 · **Status:** todo
- **Prerequisites:** GIT-001, GIT-002
- **Projects:** Git, IntegrationTests
- **Description:** Single-pass history walk within `GitHistoryWindow`: per-file commit count, distinct authors (count only), first/last change, lines added/deleted.
- **Acceptance criteria:** Correct numbers on builder-created histories; bounded runtime on the performance fixture.

### GIT-005 — Persist Git data and activity overlay
- **Priority:** P0 · **Milestone:** M5 · **Status:** todo
- **Prerequisites:** GIT-004, STORE-004
- **Projects:** Storage, Application, Graph, Desktop
- **Description:** `GitSnapshots`/`GitChanges` tables and migration; aggregation to projects/modules; activity overlay in graph and inspector.
- **Acceptance criteria:** Overlay matches persisted numbers; repositories without Git work unchanged.

### UI-008 — Clone dialog
- **Priority:** P0 · **Milestone:** M5 · **Status:** todo
- **Prerequisites:** GIT-003
- **Projects:** Desktop
- **Description:** URL, optional branch and token (masked), progress, cancel, existing-clone handling.
- **Acceptance criteria:** Token never displayed after entry and never logged (log test).

---

## M6 — Documentation export

### EXPORT-001 — Markdown/Mermaid architecture report
- **Priority:** P0 · **Milestone:** M6 · **Status:** todo
- **Prerequisites:** STORE-004
- **Projects:** Application (use case and `Export/` folder with the Markdown/Mermaid renderer: pure text generation, no I/O), Contracts (export DTOs), Desktop (file writing via the save dialog)
- **Description:** Report with overview, languages, projects, Mermaid project graph (budgeted), external dependencies, analysis metadata and evidence statistics.
- **Acceptance criteria:** Snapshot test of the report for `dotnet-layered`; Mermaid output is valid (escaping of names tested).

### EXPORT-002 — Export dialog
- **Priority:** P0 · **Milestone:** M6 · **Status:** todo
- **Prerequisites:** EXPORT-001
- **Projects:** Desktop
- **Description:** Scope and section selection, target folder (default outside repository), open folder after export.
- **Acceptance criteria:** Writing into the analyzed repository requires explicit confirmation.

---

## M7 — Optional local AI

### AI-001 — AI module foundation, settings and provider gate
- **Priority:** P0 · **Milestone:** M7 · **Status:** todo
- **Prerequisites:** STORE-005
- **Projects:** Application (port `IAiAssistant`), AI, Desktop (settings UI), IntegrationTests, Directory.Packages.props
- **Description:** Add `Microsoft.Extensions.AI` (10.x, re-verify); AI disabled by default; provider registry; gate enforcing enabled/configured/consent; network guard test proving no outbound calls when disabled.
- **Acceptance criteria:** With default settings no `IChatClient` is constructed and no network handler is invoked (test).

### AI-002 — Ollama provider
- **Priority:** P0 · **Milestone:** M7 · **Status:** todo
- **Prerequisites:** AI-001
- **Projects:** AI, Desktop
- **Description:** `OllamaSharp` (re-verify) as `IChatClient`; endpoint health check; model listing; non-loopback endpoint treated as remote.
- **Acceptance criteria:** Integration test against a stub HTTP server; manual test with a real Ollama instance documented.

### AI-003 — Search chunk index
- **Priority:** P0 · **Milestone:** M7 · **Status:** todo
- **Prerequisites:** STORE-004
- **Projects:** Storage, Analysis (chunking during analysis or post-step — decided in task), Storage.Tests
- **Description:** Verify FTS5 availability in the bundled SQLite; create `SearchChunks`; chunk source files (secret-file exclusions); query API.
- **Acceptance criteria:** Verification result documented in DATABASE_SCHEMA.md §4.18; search tests pass.

### AI-004 — Retriever and prompt builder
- **Priority:** P0 · **Milestone:** M7 · **Status:** todo
- **Prerequisites:** AI-003
- **Projects:** AI, Application
- **Description:** Facts-first retrieval (model queries + FTS + evidence snippets), citation keys, token budgeting, system prompt rules.
- **Acceptance criteria:** Deterministic context for a given question and model (snapshot tests); context manifest produced.

### AI-005 — Citation validator
- **Priority:** P0 · **Milestone:** M7 · **Status:** todo
- **Prerequisites:** AI-004
- **Projects:** AI, Application
- **Description:** Parse `[F1]`/`[S1]` keys and `path:start-end` citations; validate against model; mark verified/unverified.
- **Acceptance criteria:** Tests with valid, invalid, out-of-range and fabricated citations.

### AI-006 — AI panel and conversation persistence
- **Priority:** P0 · **Milestone:** M7 · **Status:** todo
- **Prerequisites:** AI-002, AI-005
- **Projects:** Storage (tables + migration), Application, Desktop
- **Description:** Streaming answers, sanitized Markdown, clickable citations (focus graph / open snippet), "AI-generated" label, context manifest view, conversation history.
- **Acceptance criteria:** Raw HTML in model output is not rendered; cancelling a streaming answer works.

### AI-008 — Explain component
- **Priority:** P0 · **Milestone:** M7 · **Status:** todo
- **Prerequisites:** AI-006
- **Projects:** AI, Application, Desktop
- **Description:** Inspector action generating an explanation for the selected element; cached as `AIArtifact`, invalidated by new runs.
- **Acceptance criteria:** Cached explanation reused until the next run; citations validated.

---

## M8 — MVP hardening and release

### QA-004 — Cross-platform packaging
- **Priority:** P0 · **Milestone:** M8 · **Status:** todo
- **Prerequisites:** all P0 tasks of M1–M7
- **Projects:** Desktop, tools/development, docs
- **Description:** Self-contained publish for `win-x64`, `osx-arm64`, `linux-x64`; verify native assets (WebView, SQLite, Tree-sitter, libgit2); define minimum OS versions (OPD-09); signing/notarization requirements documented.
- **Acceptance criteria:** Packages start on clean VMs of each OS and analyze `dotnet-layered`.

### QA-005 — Accessibility and theming review
- **Priority:** P1 · **Milestone:** M8 · **Status:** todo
- **Prerequisites:** UI-006, GRAPH-005
- **Projects:** Desktop, GraphView.Web
- **Description:** Keyboard navigation, focus visuals, contrast in both themes, graph color palette check.
- **Acceptance criteria:** Checklist in TEST_STRATEGY.md completed.

### QA-006 — Release readiness review
- **Priority:** P0 · **Milestone:** M8 · **Status:** todo
- **Prerequisites:** QA-004
- **Projects:** docs
- **Description:** Verify MVP release gate (MVP_SCOPE.md §5), README feature status, open decisions resolved or deferred explicitly.
- **Acceptance criteria:** All gate items checked with evidence links.

### UI-009 — Settings completion and open-in-editor
- **Priority:** P0 · **Milestone:** M8 · **Status:** todo
- **Prerequisites:** STORE-005
- **Projects:** Application, Desktop
- **Description:** Analysis limits/excludes, history window, external editor command template (path as single argument, no shell), cache cleanup.
- **Acceptance criteria:** Editor launch passes arguments without shell interpolation (test with special characters in paths).

---

## Post-MVP backlog (selection)

### AI-007 — Cloud provider opt-in
- **Priority:** P3 · **Milestone:** post-MVP · **Status:** todo
- **Prerequisites:** AI-006, OPD-03 decided
- **Projects:** AI, Desktop, docs (new ADR)
- **Description:** OpenAI-compatible provider via `Microsoft.Extensions.AI` adapter; API key in secret storage; per-repository consent; indicator; context manifest preview before sending.
- **Acceptance criteria:** Without consent no request is sent (test); consent revocable.

Further post-MVP items (incremental analysis, history comparison, impact analysis, coupling
metrics, cross-language links, more languages) get task IDs when they are specified
(see [FEATURE_ROADMAP.md](../product/FEATURE_ROADMAP.md)).
