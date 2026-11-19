# Task Breakdown — Codebase Explorer

> Status: **living document** · Last reviewed: 2026-11-18

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
| M1 Vertical slice | 23 | 21 |
| M2 Persistence | 10 | 10 |
| M3 Deep C# & Inspector | 9 | 9 |
| M4 Polyglot & scale | 11 | 11 |
| M5 Git | 7 | 7 |
| M6 Export | 2 | 2 |
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
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** CORE-004, CORE-005, ANALYSIS-001, GRAPH-002
- **Projects:** Application, IntegrationTests
- **Description:** `ImportLocalRepository` validates the path, runs `IRepositoryAnalyzer`, and keeps the resulting `ArchitectureModel` in a session-scoped `IArchitectureModelCache` (Application, in memory, replaced by the SQLite-backed store in STORE-004). `GetGraphView` reads from the cache and calls `IGraphProjector`. One run per repository at a time.
- **Acceptance criteria:**
  - End-to-end test: fixture `dotnet-layered` → import → project-level `GraphViewDto` matching `EXPECTED.md`.
  - Cancellation stops the run and leaves no model in the cache.
  - Concurrent import of the same repository is rejected with a typed error.
- **Note:** Use cases return Contracts DTOs (RepositoryImportSummaryDto, GraphViewDto). The session cache is replaced by the SQLite store in STORE-004; GetGraphView is already async for that.

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
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** ANALYSIS-001, REPO-004
- **Projects:** Analysis, Analysis.Tests
- **Description:** Static parsers for the text `.sln` format and XML `.slnx` format: project paths, solution folders → `SolutionMembership`.
- **Acceptance criteria:**
  - Both formats of the fixture produce identical memberships.
  - Malformed files produce diagnostics, not exceptions.
  - XML parsing uses the hardened reader settings (SECURITY.md).

### ANALYSIS-003 — Static .csproj parser with props resolution
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** ANALYSIS-001
- **Projects:** Analysis, Analysis.Tests
- **Description:** Parse SDK-style project files and resolve `Directory.Build.props`/`Directory.Packages.props` statically (ANALYSIS_ENGINE.md Stage 2): properties, `ProjectReference`, `PackageReference` + central versions, `FrameworkReference`, simple conditions.
- **Acceptance criteria:**
  - Never invokes MSBuild APIs; no `Microsoft.Build*` package referenced.
  - Unsupported conditions/property functions are reported as diagnostics and treated as unknown.
  - Each extracted reference has file + line/column of its XML element.
  - Tests include an XML bomb / DTD file that is rejected safely.
- **Note:** Undefined properties are treated as unknown (not empty, unlike MSBuild); only the nearest Directory.*.props are applied. Both documented in ANALYSIS_ENGINE.md Stage 2.

### ANALYSIS-004 — .NET project dependency extraction with evidence
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** ANALYSIS-002, ANALYSIS-003
- **Projects:** Analysis, Analysis.Tests
- **Description:** `DotNetProjectSystemAnalyzer` produces `CodeProject`s, `ProjectReference` and `PackageReference`/`FrameworkReference` relationships with evidence (`dotnet.msbuild-static`, rules `csproj.ProjectReference` etc.).
- **Acceptance criteria:**
  - Output for `dotnet-layered` equals `EXPECTED.md` (projects, edges, evidence locations).
  - Missing referenced project file → `Syntactic` relationship + warning.
  - Analysis twice yields identical models (determinism test).
- **Note:** Missing project files become placeholder projects (kind Unknown, resolution=missing) so the Syntactic reference stays visible; documented in EXPECTED.md §4, ANALYSIS_ENGINE.md and DOMAIN_MODEL.md.

### GRAPH-001 — Spike: Avalonia WebView + Cytoscape.js on Windows, macOS, Linux
- **Priority:** P0 · **Milestone:** M1 · **Status:** in progress · **Spike (time-box 2 days)**
- **Prerequisites:** —
- **Projects:** Desktop (spike branch), docs
- **Description:** Verify `Avalonia.Controls.WebView` (12.1.x) with a bundled static page running Cytoscape.js: local asset loading, .NET→JS and JS→.NET messages, navigation blocking, CSP, runtime prerequisites per OS.
- **Acceptance criteria:**
  - Written result in ADR-006 (update status) and GRAPH_ENGINE.md §6 (concrete interop API).
  - If a platform fails: documented mitigation or fallback proposal for OPD-04.
  - Package added to `Directory.Packages.props` only if adopted.
- **Note (2026-11-17):** Windows verified — all checks pass after setting `AdapterPreference` and `UserDataFolder` (ADR-006 "Spike result"; code on branch `spike/GRAPH-001-webview`). **Remaining:** run the spike app on macOS and Linux and record the results; decide OPD-04 if Linux embedding fails. GRAPH-003/004 may start on Windows meanwhile.

### GRAPH-002 — Graph DTOs and project-level projection
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** CORE-004
- **Projects:** Contracts, Application (port `IGraphProjector`), Graph, Graph.Tests
- **Description:** Implement `GraphViewDto`, `GraphNodeDto`, `GraphEdgeDto`, `TruncationDto`, message envelope types, System.Text.Json source-generated context; implement L1 projection (projects + aggregated project edges).
- **Acceptance criteria:**
  - Projection is a pure function of model slice + query; tested with hand-built models.
  - Aggregated edge contains count, kinds histogram, max/min level, `isDeclared`.
  - JSON round-trip tests for all DTOs.
- **Note:** GraphQuery holds only the fields L1 uses; expansion, focus, filters and node budget arrive with GRAPH-006…008. The placeholder convention moved to Domain ProjectPropertyKeys so Analysis and Graph share it without referencing each other.

### GRAPH-003 — Graph web view project scaffold
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** GRAPH-001
- **Projects:** `src/CodebaseExplorer.GraphView.Web` (new, non-.NET), Desktop (build integration), docs
- **Description:** Vite + TypeScript + Cytoscape.js app implementing `graph/load`, `selection/changed`, `ready`, theme messages; build output embedded into Desktop. Decide and document how the .NET build works without Node installed.
- **Acceptance criteria:**
  - `npm ci && npm run build && npm test` succeed; Vitest tests for message validation.
  - Labels rendered as text only; CSP meta tag present; no network requests.
  - SOLUTION_STRUCTURE.md §4 updated with the final build integration.
- **Note:** Bundle is a classic IIFE script loaded from file:// and committed to Desktop/GraphView; a source hash in build-info.json is checked by a .NET test, so the .NET build needs no Node. Runtime check inside the WebView happens with the host control (GRAPH-004). npm audit: picomatch ReDoS in build tooling accepted (SOLUTION_STRUCTURE §6.2).

### GRAPH-004 — Graph host control and message bridge
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** GRAPH-001, GRAPH-002, GRAPH-003
- **Projects:** Desktop, IntegrationTests
- **Description:** Avalonia control hosting the WebView, loading bundled assets, serializing DTOs, validating inbound messages, exposing selection as bindable state for ViewModels.
- **Acceptance criteria:**
  - ViewModels interact with an `IGraphViewHost` abstraction, not with the WebView.
  - Inbound messages failing validation are logged and ignored.
  - Navigation to any non-bundled URL is blocked.
- **Note:** `IGraphViewHost` and `GraphViewBridge` live in Desktop/Services (CODING_STANDARDS §7), the control and navigation policy in Desktop/Controls. The bridge re-sends graph and theme on every `ready`. Added `app.manifest` (supportedOS), without which native control hosting crashes on Windows. Runtime verified on Windows only (GRAPH_ENGINE.md §6.1); macOS/Linux stay with GRAPH-001. The page is wired in UI-003.

### GRAPH-010 — Edge evidence in the explorer side panel
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** GRAPH-002, UI-003
- **Projects:** Contracts, Application, Graph, Graph.Tests, Desktop, IntegrationTests
- **Description:** Closes the M1 acceptance criterion "edges carry evidence (file + line) visible at least in a basic side panel" (IMPLEMENTATION_PLAN.md, M1), which no other M1 task covered. `IGraphProjector.DescribeEdge` returns the relationships behind one aggregated edge (same attribution as the projection) as `GraphEdgeDetailsDto` with evidence (analyzer, rule, level, path, lines); use case `GetGraphEdgeDetails`; the explorer side panel lists the evidence of a selected edge.
- **Acceptance criteria:**
  - Every project edge of `dotnet-layered` has evidence whose location matches EXPECTED.md §3.
  - Unknown edges and unanalyzed repositories yield `NotFound` errors, not exceptions.
- **Note:** Added on 2026-11-18 to close the gap. No source snippets and no node relationships — those stay with UI-006/UI-007 (M3), which will build on this query. At most 50 evidence records are listed per edge.

### UI-001 — Shell polish and Avalonia 12 developer tooling
- **Priority:** P1 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** —
- **Projects:** Desktop
- **Description:** Status bar, busy indicator, notification area for errors; evaluate developer tooling for Avalonia 12 (diagnostics/devtools) and document the result in SOLUTION_STRUCTURE.md §6.
- **Acceptance criteria:** Shell shows background-operation state; no business logic in views.
- **Note:** `ShellStatus` (Desktop/Services) holds running operations (status bar text, busy indicator) and notifications (newest first, max. 5, dismissible); the import and the graph load report into it, failures also become notifications. The notification area is docked above the page because native controls (graph WebView) would cover an overlay. Developer tooling: not adopted, see SOLUTION_STRUCTURE.md §6.

### UI-002 — Import local repository flow
- **Priority:** P0 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** CORE-006
- **Projects:** Desktop, IntegrationTests
- **Description:** Start page action "Open local folder" using Avalonia's storage provider folder picker; progress panel with stages and cancel; navigate to the explorer on success; errors shown inline.
- **Acceptance criteria:**
  - ViewModel tests (no UI) cover success, cancellation and error paths with substituted use cases.
  - UI thread is never blocked by analysis work.
- **Note:** The tests use the real `ImportLocalRepository` with a substituted `IRepositoryAnalyzer` port (the use case is a sealed class; no extra interface). The use case runs via `Task.Run`; a test asserts the command returns while the analyzer blocks. The current repository is held by `RepositorySession` (Desktop/Services); the shell navigates to the explorer when it changes. Git detection (WF-01 step 3) is not part of M1.

### UI-003 — Architecture Explorer page with project graph
- **Priority:** P0 · **Milestone:** M1 · **Status:** in progress
- **Prerequisites:** GRAPH-004, UI-002
- **Projects:** Desktop
- **Description:** Replace the placeholder with the graph host; load the L1 graph for the current repository; show selected node name and kind in a simple side panel (full inspector in UI-006).
- **Acceptance criteria:**
  - Manual test on all three OSes with `dotnet-layered`: 5 project nodes, correct edges, selection works.
  - Theme switch updates the graph.
- **Note (2026-11-18):** Implemented; ViewModel tests cover loading, errors and the selection panel. **Manual test on Windows passed:** `dotnet-layered` shows the 5 projects plus the placeholder `Shop.Legacy` (missing project file, see EXPECTED.md §4) with 8 edges, selection updates the panel, and switching the app theme recolors the graph. **Remaining:** the same manual test on macOS and Linux (together with GRAPH-001).

### QA-001 — Continuous integration
- **Priority:** P1 · **Milestone:** M1 · **Status:** done
- **Prerequisites:** —
- **Projects:** repository root (`.github/workflows`), docs
- **Description:** GitHub Actions workflow building and testing on Windows, macOS and Linux; evaluate code coverage with a Microsoft.Testing.Platform extension.
- **Acceptance criteria:** Pull requests show build + test status for 3 OSes; coverage decision documented in TEST_STRATEGY.md.
- **Note:** `.github/workflows/ci.yml` (push to main, pull requests): .NET build + test on Windows, macOS, Linux; graph web view tests and bundle check on Linux; coverage with coverlet.MTP reported as artifact, no gate (TEST_STRATEGY.md §7.1). The macOS/Linux jobs run build and tests only — the manual WebView checks of GRAPH-001/UI-003 remain open.

---

## M2 — Persistence and analysis runs

### STORE-001 — DbContext and persistence for core tables
- **Priority:** P0 · **Milestone:** M2 · **Status:** done
- **Prerequisites:** CORE-004
- **Projects:** Storage, Storage.Tests, Directory.Packages.props
- **Description:** Add EF Core SQLite (10.0.x, re-verify), `CodebaseExplorerDbContext`, persistence entities and configurations for `Repositories`, `AnalysisRuns`, `AnalysisDiagnostics`, `Files`, `Projects`, `Modules`; BLOB Guid converters; connection pragmas; `IDbContextFactory` registration.
- **Acceptance criteria:**
  - Mapping tests (Domain → record → Domain round-trip) for each table.
  - EF entities are `internal`.
- **Note:** EF Core SQLite 10.0.15 (re-verified). Decided: timestamps as fixed-width ISO-8601 UTC TEXT (`UtcIsoTextConverter`) because EF cannot order `DateTimeOffset` in SQLite; `AnalysisDiagnostics` got `Ordinal`, `StartColumn`, `EndColumn` so diagnostics round-trip exactly; the cyclic FKs (Files↔Projects, Repositories↔AnalysisRuns) need the insert order documented in DATABASE_SCHEMA.md §2. Schema is created with `EnsureCreated` in tests until migrations exist (STORE-003); the app does not read or write the database yet (STORE-004).

### STORE-002 — Persistence for entities, relationships, evidence, dependencies
- **Priority:** P0 · **Milestone:** M2 · **Status:** done
- **Prerequisites:** STORE-001
- **Projects:** Storage, Storage.Tests
- **Description:** Tables `CodeEntities`, `CodeEntityDeclarations`, `CodeRelationships` (incl. denormalized container IDs), `AnalysisEvidence`, `ExternalDependencies`; decide on evidence sampling for very high-occurrence relationships (ADR-007).
- **Acceptance criteria:** Round-trip tests; indexes from DATABASE_SCHEMA.md exist (verified via migration SQL test).
- **Note:** Evidence sampling decided: none for now (ADR-007, DATABASE_SCHEMA.md §4.10). `CodeEntities` got a `SymbolKey` column (the id derives from it). Relationship rows carry the denormalized project/module ids computed from the model. Indexes are verified against `sqlite_master` of the created schema; the same check runs on the migration once it exists (STORE-003). Read-back verifies ids, natural keys, level and occurrence count.

### STORE-003 — Migrations, startup migration and backup
- **Priority:** P0 · **Milestone:** M2 · **Status:** done
- **Prerequisites:** STORE-002
- **Projects:** Storage, Desktop, Storage.Tests
- **Description:** Initial migration, design-time factory, startup `MigrateAsync` with backup of existing DB (keep 3), error page on failure.
- **Acceptance criteria:** Test migrates an empty DB to latest; test verifies backup creation; app starts with an existing DB; database file and data directory are created with user-only permissions where the OS supports it (Unix modes 0700/0600; on Windows the per-user profile ACL applies).
- **Note:** Port `IDatabaseInitializer` (Application) with `DatabaseInitializer` (Storage); backup via SQLite online backup API (WAL-safe), keep 3. The app shows `StartupErrorWindow` instead of the main window on failure; the log path follows with CORE-007 (no file log yet). Pre-creating an empty DB file breaks EF's SQLite migrator, so the 0700 data directory plus 0600 after opening provide the user-only permissions; the Unix test runs only in CI (Linux/macOS). Manually verified on Windows: first start creates the DB, second start reuses it, an unreadable DB shows the error window and stays unchanged.

### STORE-004 — Architecture model store (save run, read slices)
- **Priority:** P0 · **Milestone:** M2 · **Status:** done
- **Prerequisites:** STORE-003, CORE-006
- **Projects:** Application (port `IArchitectureModelStore`), Storage, Storage.Tests, IntegrationTests
- **Description:** Transactional replace of a repository's model per successful run (batched); read slices for graph projection (projects, aggregated relationships by project/module) and inspector queries. Replace the M1 session cache.
- **Acceptance criteria:**
  - Failed/cancelled runs never modify the stored model.
  - Saving the `dotnet-layered` model and reading the L1 slice yields the same graph as M1.
  - Saving 1,000,000 synthetic relationships completes without exceeding 1.5 GB process memory (benchmark, documented result).
- **Note (2026-11-18):** Port `IArchitectureModelStore` replaces `IArchitectureModelCache`; `ArchitectureModelStore` (Storage) saves every finished run and replaces the model of a run that produced one in one transaction (delete by repository, then batched inserts of 5,000 rows with a cleared change tracker). **Decided with the user:** the Application layer reads the whole current model (cached per run id) instead of SQL slices; slice queries move to M4 (GRAPH-008, QA-002), see SYSTEM_ARCHITECTURE.md §6 and ADR-010. The `dotnet-layered` L1 graph is unchanged (end-to-end test now runs through SQLite). Benchmark: 1,331 MB peak with `System.GC.ConserveMemory=9`, which the app now sets — with default GC it was 1,951 MB (TEST_STRATEGY.md §8). Integration tests run the real composition root against a temporary data directory (`TestApplication`, `CompositionRoot.BuildServiceProvider(overrides)`).

### STORE-005 — User settings store and theme persistence
- **Priority:** P0 · **Milestone:** M2 · **Status:** done
- **Prerequisites:** STORE-003
- **Projects:** Application (port), Storage, Desktop
- **Description:** `UserSettings` table access via `IUserSettingsStore`; persist theme choice and restore at startup.
- **Acceptance criteria:** Theme survives restart; settings keys documented in DATABASE_SCHEMA.md.
- **Note:** Port `IUserSettingsStore` (Application `Settings/`, string values, keys in `UserSettingKeys`) with `UserSettingsStore` (Storage; value stored as JSON string, unreadable rows read as missing) and migration `AddUserSettings`. `IThemeService` got `RestoreAsync` (called in `App.StartAsync` after the database is ready, before the main window) and `ChangeAsync` (applies, then stores; a failed save shows a notification). Restart verified by an integration test with a second composition root on the same data directory (`TestApplication.Restart`).

### REPO-005 — Recent repositories and reopen without re-analysis
- **Priority:** P0 · **Milestone:** M2 · **Status:** done
- **Prerequisites:** STORE-004
- **Projects:** Application, Desktop
- **Description:** Start page lists recent repositories (name, path, last run status/date); opening loads the stored model; "Re-analyze" action; "Remove" deletes data (cascade).
- **Acceptance criteria:** Reopen shows the graph without running analysis; remove deletes all rows of the repository and, for app-managed clones (from M5), the clone directory after confirmation.
- **Note:** Use cases `ListRecentRepositories`, `OpenRepository`, `RemoveRepository` (Contracts `RecentRepositoryDto`, `OpenRepositoryDto`, `RunStatuses`); store methods `ListRepositoriesAsync`, `MarkOpenedAsync`, `DeleteRepositoryAsync` (single `DELETE`, FK cascade; invalidates the model cache). A successful import also sets `LastOpenedAt`. Start page: list with name, path, last run status/date; *Open* (disabled without a stored model), *Re-analyze* (full import of the folder, cancellable), *Remove* with an inline confirmation (no dialog service needed); removing the open repository clears the session and the explorer returns to its empty state. `RepositorySession` now holds `OpenRepositoryDto`; `PageViewModel.ActivateAsync` refreshes a page when the shell shows it. App-managed clones do not exist before M5; since GIT-003 removing a clone also deletes its clone directory. Reopening after a restart is covered by an integration test with a second composition root; manually verified on Windows (2026-11-18): list, Open, Re-analyze and the Remove confirmation (Keep) work against the real database.

### UI-004 — Repository overview page
- **Priority:** P1 · **Milestone:** M2 · **Status:** done
- **Prerequisites:** STORE-004
- **Projects:** Application (query), Contracts, Desktop
- **Description:** Languages (file counts/lines), projects by ecosystem, external dependency count, last run info, warnings count; link to explorer.
- **Acceptance criteria:** Values match fixture expectations; no EF types in ViewModels.
- **Note:** Use case `GetRepositoryOverview` (Contracts `RepositoryOverviewDto` with `LanguageStatisticsDto`, `EcosystemStatisticsDto`, `RunInfoDto`), computed from the stored model: files and lines per language, projects per ecosystem (placeholders for missing project files counted separately), external dependencies, relationships, the run of the current model with its warnings/errors, and a newer run that did not replace the result (failed/cancelled re-analysis). New navigation page *Overview*; after an import or opening a stored repository the shell now navigates to the overview (WF-01 step 5), whose *Open Architecture Explorer* button opens the graph. Shows a hint when no projects were recognized. The fixture test checks the oracle values of `dotnet-layered` (EXPECTED.md: 5 projects + 1 placeholder, 5 external dependencies, 13 relationships, 1 warning) and file/line counts against the files on disk; ViewModels use only Contracts DTOs. Manually verified on Windows (2026-11-18) with the `dotnet-layered` repository: values as expected, *Open Architecture Explorer* shows the stored graph.

### UI-005 — Analysis run history and diagnostics
- **Priority:** P0 · **Milestone:** M2 · **Status:** done
- **Prerequisites:** STORE-004
- **Projects:** Application, Desktop
- **Description:** List of runs with status, duration, commit; diagnostics list filterable by severity.
- **Acceptance criteria:** Partially succeeded runs show their errors with analyzer and location.
- **Note:** Use case `GetRunHistory` (store method `ListRunsAsync`; Contracts `AnalysisRunDto`, `DiagnosticDto` got `Analyzer`). New navigation page *Run History* (`RunHistoryPageViewModel`) for the open repository: runs newest first (status, start, trigger, duration, error/warning counts, *current* marker), details with statistics and commit, diagnostics with severity filter (All/Errors/Warnings/Info), analyzer and `path:line`. Commit is always empty until M5 (shown as such). Limitation: diagnostic locations store file ids only, so for runs whose model was replaced the path of a file that no longer exists cannot be shown (line only). Covered by integration tests (real SQLite, substituted analyzer for partial/failed runs); manually verified on Windows (2026-11-18): list, Open, Re-analyze and the Remove confirmation (Keep) work against the real database.

### CORE-007 — File logging and global exception handling
- **Priority:** P1 · **Milestone:** M2 · **Status:** done
- **Prerequisites:** —
- **Projects:** Infrastructure, Desktop
- **Description:** Rolling file logs in `<data>/logs` (implementation choice documented; prefer a minimal in-house provider over new dependencies), unhandled exception handlers (AppDomain, TaskScheduler, Avalonia dispatcher), redaction rules.
- **Acceptance criteria:** Crash produces a log entry and a user-facing message; logs never contain file contents.
- **Note:** In-house `FileLoggerProvider` (Infrastructure, registered as `ILoggerProvider`; no new package — Infrastructure now references `Microsoft.Extensions.Logging.Abstractions`): daily files plus 10 MB rollover, 10 files kept, flushed per entry, disabled on write errors; `LogRedactor` masks credentials and tokens and cuts entries at 8,000 characters (SECURITY.md §4). EF Core SQL command logs are filtered below Warning (they would flood the file). `GlobalExceptionHandler` (Desktop): UI-thread exceptions → log + notification, app keeps running; fatal → log + marker, notification at the next start; unobserved tasks → log. `StartupErrorWindow` and the Settings page show the log directory; startup failures show the startup error window. Composition tests now use `TestApplication`, so tests never write into the user's log directory. Tests: redaction, rollover, retention, write failure, handler behavior and an import of `dotnet-layered` whose log contains no fixture file content.

### CORE-010 — Remove the zero-test exit-code exception
- **Priority:** P1 · **Milestone:** M2 · **Status:** done
- **Prerequisites:** CORE-002, ANALYSIS-001, GRAPH-002, STORE-001
- **Projects:** tests/Directory.Build.props
- **Description:** Every test project contains tests by now; remove `--ignore-exit-code 8`.
- **Acceptance criteria:** `dotnet test --solution CodebaseExplorer.slnx` passes without the flag.
- **Note:** The flag and its comment are gone from `tests/Directory.Build.props`; all five test projects contain tests. A focused run whose filter matches nothing now fails with exit code 8 (TEST_STRATEGY.md §2, DEVELOPMENT_GUIDE.md §8).

---

## M3 — Deep C# analysis and Component Inspector

### ANALYSIS-005 — Roslyn syntax analysis (modules and entities)
- **Priority:** P0 · **Milestone:** M3 · **Status:** done
- **Prerequisites:** ANALYSIS-004
- **Projects:** Analysis, Analysis.Tests, Directory.Packages.props
- **Description:** Add `Microsoft.CodeAnalysis.CSharp` (re-verify 5.x version; **not** `Workspaces.MSBuild`). Parse project `.cs` files; namespaces → `CodeModule` (hierarchy), types and members → `CodeEntity` with declarations (partial types merged).
- **Acceptance criteria:** Fixture expectations for modules/entities met; symbol keys are documentation-comment IDs; no assemblies loaded from the repository.
- **Note:** Microsoft.CodeAnalysis.CSharp 5.12.0 (re-verified). **Decided with the user (ADR-011):** compilations reference the embedded .NET 10 reference assemblies of `Basic.Reference.Assemblies.Net100` 1.8.15 and never SDK packs or NuGet assemblies, because Roslyn formats resolved and unresolved types differently in documentation comment IDs (verified) and entity ids must not depend on the machine (fixture V-1). New pipeline seam `IRepositoryLanguageAnalyzer`: `CSharpAnalyzer` analyzes all C# projects in topological order with `CompilationReference`s to referenced projects (cycles broken with a warning, failures isolated per project). `DeclarationCollector` creates namespace modules (with enclosing namespaces) and entities for types and source-declared members (EXPECTED.md §8.2 rules; global-namespace types go to module `(global)`; top-level statements produce no entity). Implicit usings follow the SDK defaults. Tests: all 7 modules and 41 entities of `dotnet-advanced` match EXPECTED.md §8 exactly (key, kind, visibility, modifiers, declarations, containment), keys are stable across runs, references are in-memory, no assembly is loaded from the repository; `dotnet-layered` is analyzed without errors.

### ANALYSIS-006 — Roslyn semantic relationships without build
- **Priority:** P0 · **Milestone:** M3 · **Status:** done
- **Prerequisites:** ANALYSIS-005
- **Projects:** Analysis, Analysis.Tests
- **Description:** Build `CSharpCompilation`s in topological project order with metadata references (SDK reference packs if found, in-repo `CompilationReference`s, cached NuGet assemblies only via existing `project.assets.json`). Extract `Inherits`, `Implements`, `UsesType`, `Instantiates`, `Calls` with evidence levels.
- **Acceptance criteria:**
  - Without SDK packs present, analysis still succeeds with more `Syntactic` evidence (test with packs disabled).
  - Memory released per project (compilations not retained after linking).
  - Fixture expectations met, including cross-project relationships.
- **Note:** `RelationshipCollector` (second phase of `CSharpAnalyzer`, after all declarations): `Inherits`, `Implements`, `UsesType` (incl. generic arguments and constraints), `Instantiates`, `Calls` (incl. `base`/`this` initializers) between repository entities; rules `csharp.<Kind>`, one evidence per occurrence, no excerpts. Per ADR-011 neither SDK packs nor NuGet assemblies are used, so the first acceptance criterion is met by construction: in-repository resolution never depends on packs; the `Syntactic` fallback (unique name + arity match, e.g. a missing project reference) is tested with a temporary two-project repository. Compilations are released after the run (weak-reference test). All 16 rows of `dotnet-advanced` EXPECTED.md §9 are found as `Confirmed` with the documented evidence location (one oracle location corrected in this task: the `Instantiates` evidence is the type name after `new`, as in the other row); no relationship from the comment or string. `dotnet-layered` now has 29 relationships (13 manifest + 16 code); project edges in the graph are unchanged and aggregate the code relationships in their details.

### ANALYSIS-007 — DI registration heuristics (C#)
- **Priority:** P2 · **Milestone:** M3 · **Status:** done
- **Prerequisites:** ANALYSIS-006
- **Projects:** Analysis, Analysis.Tests
- **Description:** Detect `services.Add{Singleton|Scoped|Transient}<TService, TImpl>()` and common overloads → `RegistersService` with `Heuristic` evidence.
- **Acceptance criteria:** All detections carry rule ids; no false positives on the fixture's negative cases.
- **Note:** Part of `RelationshipCollector`: `Add|TryAdd{Singleton|Scoped|Transient}<TService, TImpl>()`, `<TImpl>()` and the `typeof(TService), typeof(TImpl)` overload → `RegistersService` (source: registering member, target: implementation; attributes `serviceType`, `lifetime`; `Heuristic`; rules `csharp.di.generic-registration` / `csharp.di.typeof-registration`). Recognized by shape and name because `IServiceCollection` is never resolved (ADR-011); calls that resolve to a repository method are excluded. Both registrations of `dotnet-advanced` EXPECTED.md §10 are found; the comment, the string and `Catalog.AddSingleton` produce none; a temporary repository covers the `typeof` overload and a repository-defined `AddScoped<TA, TB>()`.

### REPO-006 — Fixture `dotnet-advanced`
- **Priority:** P1 · **Milestone:** M3 · **Status:** done
- **Prerequisites:** REPO-004
- **Projects:** TestRepositories
- **Description:** Partial classes, nested types, generics, multi-targeting, conditional references, missing project reference, central package management with `VersionOverride`.
- **Acceptance criteria:** `EXPECTED.md` complete; used by ANALYSIS-005/006 tests and as additional regression input for the M1 parsers (ANALYSIS-003/004).
- **Note:** `tests/CodebaseExplorer.TestRepositories/Repositories/dotnet-advanced` (16 files, LF). EXPECTED.md §1–7 (files, solution, projects incl. placeholder `Advanced.Plugins`, references, conditions, diagnostics) are asserted by `DotnetAdvancedFixtureTests` and already pass with the M1 parsers. §8 (modules and entities with documentation-comment IDs and identifier locations), §9 (minimum set of semantic relationships incl. cross-project) and §10 (DI registrations and negative cases) are the oracle for ANALYSIS-005 … 007. Open verification item V-1 for ANALYSIS-005: symbol keys must not depend on whether NuGet or SDK reference assemblies are present (unresolved parameter types).

### QA-003 — Hostile .NET fixtures proving zero execution
- **Priority:** P0 · **Milestone:** M3 · **Status:** done
- **Prerequisites:** ANALYSIS-006
- **Projects:** TestRepositories, IntegrationTests
- **Description:** .NET hostile fixtures: MSBuild `Exec` in targets and `Directory.Build.props`, property functions, analyzer/source-generator package references, all writing marker files if executed. Integration tests run the full pipeline and assert that no marker file exists and no child process was started. Provides a reusable harness (marker check + process-spawn guard) for the ecosystem-specific hostile tests in ANALYSIS-009 (npm), ANALYSIS-011 (Python) and GIT-001 (Git hooks/fsmonitor).
- **Acceptance criteria:** Tests run in CI on all OSes; SECURITY.md §6 references them; harness documented in TEST_STRATEGY.md.
- **Note:** Fixture `dotnet-hostile` (property functions in `Directory.Build.props` and the project, `Exec` before `Restore` and `CoreCompile`, an imported target with `WriteLinesToFile`, analyzer/generator references incl. a fake `.dll`, a `[ModuleInitializer]`); no solution file, so IDEs do not pick it up. `HostileDotNetFixtureTests` runs the full import through the real composition root on a copy and asserts: success, project and entity found, copy byte-identical, no marker in the copy, the test output or the data directory, no assembly loaded from the copy. **Process-spawn guard:** .NET has no cross-platform API to observe child processes, so the guard is static — `ZeroExecutionHarness.ProcessApiUsages` checks the IL of Analysis, Storage, Git, AI, Graph, Application, Domain and Contracts for `Process`/`ProcessStartInfo` references and process-creation P/Invokes; a self-test proves detection. Runs in the normal CI jobs (all three OSes); verified locally on Windows.

### GRAPH-005 — Layouts and visual encoding
- **Priority:** P0 · **Milestone:** M3 · **Status:** done
- **Prerequisites:** GRAPH-004
- **Projects:** GraphView.Web, Desktop
- **Description:** Hierarchical and force layouts, layout switcher, visual encoding from GRAPH_ENGINE.md §9; license review for layout extensions (elkjs EPL-2.0). Spike GRAPH-001 measured Cytoscape's built-in `cose` at 12.5–13.9 s for 500 nodes / 2,000 edges, so the default layout must be faster (measure dagre/fcose/web-worker options).
- **Acceptance criteria:** Layout choice persisted; license review documented in SOLUTION_STRUCTURE.md §6.2.
- **Note:** Done 2026-11-18. Layouts: force (fcose, default), hierarchical (dagre), concentric (built-in); switcher in the Architecture Explorer, choice stored as `graph.layout`. Measurements in GRAPH_ENGINE.md §8.1: the default force layout needs ≤ 0.43 s for 500 nodes / 2,000 edges (headless Edge); hierarchical exceeds the 2 s target on graphs that large (2.2–2.8 s headless, more in WebView2). elkjs and web-worker layouts not adopted. Module/type node shapes follow with the kinds in GRAPH-006. Verified with an end-to-end harness in headless Edge; not yet checked in the running app.

### GRAPH-006 — Module and entity levels with expand/collapse
- **Priority:** P0 · **Milestone:** M3 · **Status:** done
- **Prerequisites:** GRAPH-002, STORE-004, ANALYSIS-005
- **Projects:** Graph, Graph.Tests, Storage (slice queries), Desktop, GraphView.Web
- **Description:** L2/L3 projection, compound nodes, `graph/patch` messages, deepest-visible-ancestor edge attachment.
- **Acceptance criteria:** Expanding and collapsing is reversible and yields identical views; projection tests for mixed levels.
- **Note:** `GraphQuery.Expanded`; `VisibleTree` (Graph) builds the visible nodes: expanded projects show their namespaces, expanded namespaces their sub-namespaces and types (types not expandable in M3); namespace chains without types are compressed (`Shop` → `Shop.Domain` → `Shop.Domain.Orders` shows `Shop.Domain.Orders`); relationships attach to the deepest visible ancestor, edges inside a node or between a compound and its content are dropped; unknown/hidden ids are ignored. Protocol `graph/patch` (`GraphPatchPayload`): `GraphViewBridge.PatchGraphAsync` sends the difference, the web view validates it against its copy of the graph (`applyPatch`, all or nothing) and re-runs the layout; double click on an expandable node sends `node/expandRequested`/`collapseRequested`, the bridge checks the node state, the explorer keeps the expanded set (hidden inner expansions are remembered). Styles: namespaces rectangles, types circles, double outline = expandable, translucent compound boxes; focus dimming keeps a selected compound with its content. **Not done here:** Storage slice queries — the whole model is still read per run (ADR-010); slices follow with the node budget (GRAPH-008, QA-002). Tests: projection (mixed levels, compression, deepest-visible-ancestor, reversibility by JSON comparison, ignored ids, edge details), bridge diff and request validation, web `applyPatch`/controller/dimming, ViewModel end to end over `dotnet-layered`. **Manually verified on Windows:** expand project → namespace → types and collapse again by double click (after re-analyzing a repository stored before ANALYSIS-005, which has no namespaces).

### UI-006 — Component Inspector
- **Priority:** P0 · **Milestone:** M3 · **Status:** done
- **Prerequisites:** STORE-004, GRAPH-006
- **Projects:** Application (query), Contracts, Desktop
- **Description:** Panel with element details, metrics (fan-in/out, size), incoming/outgoing relationships grouped by kind with level badges, "show in graph".
- **Acceptance criteria:** Works for projects, modules, entities, external dependencies and aggregated edges.
- **Note:** Use case `GetElementDetails` (Application `Inspector/`, Contracts `ElementDetailsDto`): facts per kind (folder, project file, target frameworks; namespace counts; qualified name, visibility, modifiers, signature, declarations, symbol key; package ecosystem), fan-in/fan-out/size, members (type → members, namespace → types, project → top-level namespaces) and incoming/outgoing relationships grouped by kind, strongest evidence first, with the inner element (`via`) when a member holds the relationship. A project, namespace or type stands for its subtree, so the lists match the aggregated graph edges. Each related element carries the graph node to focus (a member → its type, a file → its project) and the nodes to expand. Desktop: the explorer's side panel (now 320 px, scrollable) shows the inspector for a selected node; clicking an entry runs *Show in graph* (expands project and namespaces via `graph/patch`, then focuses). **Aggregated edges** keep the evidence list of GRAPH-010/UI-007 (relationships with kind, level, location and snippet). **External dependencies** are covered by the use case and appear in the lists; their nodes are hidden in the explorer (no UI switch for external dependencies yet), so *Show in graph* reports that. Tests: use case over `dotnet-advanced` (type, project, namespace, package, expand path, errors) and ViewModel over `dotnet-layered` (details, show in graph = patch + focus). **Manually verified on Windows:** inspector of `Shop.Infrastructure`, *Show in graph* on `IOrderRepository` expands `Shop.Domain` and selects the type.

### UI-007 — Evidence list and source snippet view
- **Priority:** P0 · **Milestone:** M3 · **Status:** done
- **Prerequisites:** UI-006
- **Projects:** Application, Desktop
- **Description:** Evidence records with analyzer, rule, level, location; read-only snippet (± 5 lines) read through `IRepositoryFileReader`; copy path; plain-text rendering.
- **Acceptance criteria:** Snippet shows the exact line range; missing/changed files are reported, not crashed.
- **Note:** Implemented **before UI-006** at the user's request: the evidence list and snippet live in the Architecture Explorer's selection panel (evidence of a selected edge, GRAPH-010) and move into the inspector with UI-006. Use case `GetSourceSnippet` (Application `Source/`, Contracts `SourceSnippetDto`): only files of the stored model, read through `IRepositoryFileReader` (containment, 2 MB), evidence range ± 5 lines (clamped), lines over 500 characters shortened; changed content (SHA-256 vs. stored hash) → shown with `ChangedSinceAnalysis`; file shorter than the range → `RangeTruncated` or `source.lines-missing`; deleted file → `source.file-missing`; missing folder → `source.folder-missing`; foreign paths → `source.file-not-in-model`. Desktop: evidence `ListBox` with selection, snippet as plain-text lines with line numbers (evidence lines highlighted, monospace, selectable), *Copy path* via `IClipboardService` (absolute path). Snippet text is never logged. Tests: use case against a mutable copy of `dotnet-layered` (exact range, multi-line, changed, shortened, deleted, missing folder, invalid requests) and the ViewModel (selection → snippet, copy path, clearing). **Manually verified on Windows:** snippet of `Shop.Infrastructure.csproj:6` with line 6 highlighted, *Copy path* puts the absolute path on the clipboard (a disabled-button bug found there was fixed).

---

## M4 — Polyglot analysis and large repositories

### ANALYSIS-008 — Spike: Tree-sitter binding and grammars
- **Priority:** P0 · **Milestone:** M4 · **Status:** done · **Spike (time-box 3 days)**
- **Prerequisites:** —
- **Projects:** Analysis (spike branch), docs
- **Description:** Evaluate `TreeSitter.DotNet` 1.3.0 and alternatives: bundled grammars (TS, TSX, JS, Python), native binaries for win-x64/osx-arm64/linux-x64, parse speed, cancellation/timeouts, behavior on malformed/huge input, maintenance and license.
- **Acceptance criteria:** ADR-004 updated with the decision; crash-containment approach documented (in-process with limits vs. parser host process).
- **Note:** Done 2026-11-18 (ADR-004 "Spike result"). `TreeSitter.DotNet` 1.3.0 confirmed: TS, TSX, JS (JSX) and Python grammars and native binaries for all target RIDs; 1.9 MB parses in 0.4–0.65 s, hostile input ≤ 3.3 s, never crashed. **No** per-parse timeout/cancellation in the binding → parse time bounded by the 2 MB limit, cancellation between files and during walks. Tree-sitter queries are not used (super-linear on hostile input). **Crash containment: in-process with limits** (native crash = accepted residual risk, SECURITY.md §2). Proof code kept: `Languages/TreeSitter` (`TreeSitterParser`, `SyntaxWalk`) with `TreeSitterParserTests` (run on all CI OSes → verifies native assets); `Directory.Build.targets` trims the package to the runtime + 4 grammars for 64-bit win/osx/linux (~26 MB instead of ~600 MB).

### ANALYSIS-009 — Node project-system analyzer
- **Priority:** P0 · **Milestone:** M4 · **Status:** done
- **Prerequisites:** ANALYSIS-001, REPO-007
- **Projects:** Analysis, Analysis.Tests
- **Description:** `package.json`, npm/yarn workspaces, `pnpm-workspace.yaml`, `tsconfig.json` (JSONC) incl. `paths`/`references`; workspace dependencies → `ProjectReference`, others → npm `PackageReference`.
- **Acceptance criteria:** Fixture expectations met; scripts are never run (only listed), proven by a hostile fixture with `preinstall`/`postinstall` scripts using the QA-003 harness; deep/huge JSON rejected safely.
- **Note:** Done 2026-11-18. `NodeProjectSystemAnalyzer` (`node.manifest`, ANALYSIS_ENGINE.md Stage 2 "Implemented"): one project per `package.json`, workspace roots from `workspaces`/`pnpm-workspace.yaml` (minimal in-house reader of the `packages:` list instead of a YAML library), repository packages → `ProjectReference`, others → npm `PackageReference`, `tsconfig.json` references (solution-style configs excluded), scripts listed as properties with an `Info` per lifecycle script. `Parsing/SafeJson`: `Utf8JsonReader`, depth 64, JSONC, UTF-16 positions. Shared helpers moved/added: `RepositoryPaths` → `FileSystem/`, `Pipeline/FragmentBuilder`. Tests: EXPECTED.md §2–5 of `ts-monorepo-workspaces`, `js-commonjs` and the frontend of `mixed-polyglot`; pnpm/yarn workspaces, invalid/too deep/oversized manifests, missing names, duplicates, unresolved tsconfig references; `SafeJsonTests`, `WorkspaceFileTests`. Hostile fixture `node-hostile` + `HostileNodeFixtureTests` (QA-003 harness): lifecycle scripts, `.npmrc`, node-gyp, `file:` dependency, module side effect, TS plugin — nothing runs. **Bug found and fixed in Storage:** a project at the repository root (root directory stored as `""`) could not be read back (`RecordMapper`), regression test `ProjectAtTheRepositoryRoot_IsLoadedAgain`.

### ANALYSIS-010 — TypeScript/JavaScript import analysis
- **Priority:** P0 · **Milestone:** M4 · **Status:** done
- **Prerequisites:** ANALYSIS-008, ANALYSIS-009
- **Projects:** Analysis, Analysis.Tests
- **Description:** Tree-sitter extraction of imports/exports/requires and top-level declarations; resolution per ANALYSIS_ENGINE.md; modules = directories.
- **Acceptance criteria:** Fixture expectations met incl. path aliases; unresolved specifiers produce diagnostics.
- **Note:** Done 2026-11-18. `TypeScriptAnalyzer` (`treesitter.typescript`, one analyzer for TS/TSX/JS; ANALYSIS_ENGINE.md Stage 3 "Implemented"): directory and file modules, top-level declarations (incl. CommonJS exports), `import`/`export … from`/`require`/`import()` resolved relative, through `tsconfig` `paths`/`baseUrl` (with `extends`), to workspace packages (entry points) or npm packages (declared → `Syntactic`, undeclared → `Heuristic`). `LanguageAnalysisContext` now carries all stage-2 projects. Graph: module labels are relative to the parent also for `/` paths; Inspector shows the module kind (Namespace, Folder, Package, File module) and "Modules"/"Sub-modules" instead of namespace-only wording. Tests: EXPECTED.md of `ts-monorepo-workspaces` §6–8, `js-commonjs` §4–6, `mixed-polyglot` frontend; `TypeScriptAnalyzerTests` (kinds/modifiers/export forms/overloads, `extends`+`baseUrl`, `.js`→`.ts`, built-ins, URL imports, workspace subpaths, hoisted root dependencies, computed imports, syntax errors, oversized files, targets outside projects); `PolyglotImportTests` (composition root: graph edges and drill-down into folders/files; one project per ecosystem in `mixed-polyglot`).

### ANALYSIS-011 — Python project-system analyzer
- **Priority:** P0 · **Milestone:** M4 · **Status:** done
- **Prerequisites:** ANALYSIS-001, REPO-007
- **Projects:** Analysis, Analysis.Tests, Directory.Packages.props (TOML parser if needed)
- **Description:** `pyproject.toml` (PEP 621, Poetry), `requirements*.txt`, `setup.cfg`; package roots and `src/` layout; `setup.py` detected only.
- **Acceptance criteria:** Fixture expectations met; a hostile `setup.py` fixture proves nothing is executed (QA-003 harness); TOML library choice verified and documented (or minimal in-house parser justified).
- **Note:** Done 2026-11-18. `PythonProjectSystemAnalyzer` (`python.manifest`, ANALYSIS_ENGINE.md Stage 2 "Implemented"): one project per folder with manifests; PEP 621/735, Poetry (incl. groups and inline tables), `setup.cfg` and requirement files (includes inside the repository, options/URLs reported); repository projects → `ProjectReference`, others → PyPI `PackageReference`. **TOML library: Tomlyn 2.11.1** (BSD-2-Clause, verified on nuget.org; syntax parser with positions and depth limit; SOLUTION_STRUCTURE.md §6.1); `setup.cfg` by a minimal in-house INI reader. Tests: EXPECTED.md §2–4 of `python-src-layout` and the Poetry project of `mixed-polyglot`; `PythonManifestTests` (PEP 508, requirement files, `setup.cfg`, Poetry/dependency groups, invalid and 100,000-level nested TOML, include cycles, `setup.py` detection, cross-project references). Hostile fixture `python-hostile` + `HostilePythonFixtureTests` (QA-003 harness): `setup.py`, in-tree build backend, `conftest.py`, site customization, `.pth`, package code, `-e .` — nothing runs.

### ANALYSIS-012 — Python import analysis
- **Priority:** P0 · **Milestone:** M4 · **Status:** done
- **Prerequisites:** ANALYSIS-008, ANALYSIS-011
- **Projects:** Analysis, Analysis.Tests
- **Description:** Tree-sitter extraction of `import`/`from … import`, relative imports, top-level classes/functions; resolution against package roots and declared dependencies (heuristic name mapping).
- **Acceptance criteria:** Fixture expectations met; heuristic mappings carry `Heuristic` level.
- **Note:** Done 2026-11-18. `PythonAnalyzer` (`treesitter.python`, ANALYSIS_ENGINE.md Stage 3 "Implemented"): package and file modules (dotted, relative to `src/` or the project root), top-level classes/functions, imports anywhere in a file resolved relative/absolute against a repository-wide module index, standard library skipped, PyPI by declared name (`Syntactic`), mapping table (`Heuristic`) or undeclared (`Heuristic`). Declared distributions are re-read from the manifests of the project's folder (`pyproject.toml`, `setup.cfg`, `requirements*.txt` without includes). `Span` moved to `Languages/TreeSitter`. Tests: EXPECTED.md of `python-src-layout` §5–7 and `mixed-polyglot` §4; edge cases (star/aliased/nested/`try` imports, `__future__`, imports above the root, unresolved relative imports, decorated `async` functions, duplicate names).

### ANALYSIS-013 — Determinism and limits hardening
- **Priority:** P1 · **Milestone:** M4 · **Status:** done
- **Prerequisites:** ANALYSIS-010, ANALYSIS-012
- **Projects:** Analysis, Analysis.Tests
- **Description:** Determinism test across all fixtures; limit enforcement (file count, depth, parse timeouts) with diagnostics; statistics per stage.
- **Acceptance criteria:** All fixtures analyzed twice produce byte-identical serialized models.
- **Note:** Done 2026-11-18. `DeterminismAndLimitsTests`: all 10 fixtures, sequential vs. 8 workers, byte-identical JSON of model and diagnostics. Limits: file count and depth were already enforced with diagnostics by the scanner (REPO-002, tested); new `AnalysisLimits.MaxParseDuration` (5 s) for the Tree-sitter analyzers (`TreeSitterParser.ParseWithin`: a parse that exceeded the budget is dropped, the extraction is cancelled when it runs out; `Warning` `analysis.parse-timeout`, the module stays). Statistics: per-analyzer durations `analyzer.<id>` in `StageDurations` (stored with the run; not yet shown in the UI).

### REPO-007 — Polyglot fixtures
- **Priority:** P0 · **Milestone:** M4 · **Status:** done
- **Prerequisites:** —
- **Projects:** TestRepositories
- **Description:** `ts-monorepo-workspaces`, `js-commonjs`, `python-src-layout`, `mixed-polyglot` (dotnet + ts + python) with `EXPECTED.md`.
- **Acceptance criteria:** Each fixture's expectations reviewed and complete.
- **Note:** Done 2026-11-18. Four fixtures (LF, byte-exact) with `EXPECTED.md`: `ts-monorepo-workspaces` (19 files), `js-commonjs` (7), `python-src-layout` (9), `mixed-polyglot` (10). Their EXPECTED.md files fix the conventions the analyzers implement: one Node project per `package.json` (root with `workspaces` = `NodeWorkspaceRoot`), dependencies on repository packages = `ProjectReference`, solution-style `tsconfig` references are no dependency; one Python project per folder with manifests; TS/JS modules = directory modules + one `FileModule` per source file, Python modules = packages + file modules (dotted, relative to `src/` or the project root); `Imports` connect file modules (rules `ts.{syntax}.{resolution}`, `py.import.*`). `PolyglotFixtureTests` asserts section 1 (files, markers, classifications, LF) of all four; the analyzer sections are asserted by ANALYSIS-009 … 012. Hostile npm/`setup.py` fixtures are added with ANALYSIS-009/011.

### GRAPH-007 — Filters, search and neighborhood view
- **Priority:** P0 · **Milestone:** M4 · **Status:** done
- **Prerequisites:** GRAPH-006
- **Projects:** Application, Storage (search query), Graph, Desktop, GraphView.Web
- **Description:** Filters (language, ecosystem, relationship kind, min level, tests/generated/external toggles), `Ctrl/Cmd+K` search, focus + n-hop neighborhood.
- **Acceptance criteria:** Filter combinations covered by projection tests; search returns results < 200 ms on the performance fixture.
- **Note:** Done 2026-11-18. `GraphQuery.Filters` (`GraphFilters`: languages, ecosystems, relationship kinds, minimum level, tests and generated code **hidden by default** as specified in GRAPH_ENGINE.md §3) and neighborhood mode (`Focus`, `Depth` 1–3, `Direction`) with breadcrumbs (GRAPH_ENGINE.md §3 "Implemented"); search as Application use case `SearchElements` over the cached model (not a Storage query — the whole model is read, ADR-010/012). Desktop: search box (`Ctrl/Cmd+K` while the Avalonia part has focus), results open the neighborhood, filter flyout, neighborhood depth/direction, breadcrumbs. Tests: `GraphFilterAndNeighborhoodTests` (filter combinations incl. test/generated evidence, external dependencies by ecosystem, kinds × level, directions, depth, compound parents, budget, focus errors, edge details), `SearchElementsTests`, ViewModel tests (filters, search → neighborhood, breadcrumbs, whole graph). Search benchmark on the 2,000-project repository: max 14.4 ms (TEST_STRATEGY.md §8). Hiding test code changed the default dotnet-layered graph (Shop.Tests hidden; tests adjusted). **Not verified manually** in the running app.

### GRAPH-008 — Node budget and truncation
- **Priority:** P0 · **Milestone:** M4 · **Status:** done
- **Prerequisites:** GRAPH-006
- **Projects:** Graph, Graph.Tests, Desktop
- **Description:** Enforce node budget; top-N by degree + "N more" aggregate nodes; truncation hints in the UI.
- **Acceptance criteria:** No `GraphViewDto` exceeds the budget (property-based/parameterized tests).
- **Note:** Done 2026-11-18. `GraphQuery.NodeBudget` (500, 2 … 5,000); surplus roots → top-N by degree + one `aggregate` "N more" node carrying their edges; expansions that do not fit are refused; `TruncationDto` with reason (`node-budget`, `expansion-refused`) and suggestions (GRAPH_ENGINE.md §4 "Implemented"). Desktop: truncation banner in the explorer, the aggregate is not inspected; web view: dashed aggregate style (bundle rebuilt). Tests: `GraphNodeBudgetTests` (18 random models × budgets/expansions/packages, degree ranking and edge re-attribution, refused expansion, invalid budgets, stable aggregate id with edge details). **Not done here:** Storage slice queries — the whole model is still read and cached per run (ADR-010); QA-002 decides whether slices are needed.

### QA-002 — Performance fixture generator and benchmarks
- **Priority:** P0 · **Milestone:** M4 · **Status:** done
- **Prerequisites:** ANALYSIS-006, STORE-004
- **Projects:** tools/development, TestRepositories, docs
- **Description:** Generator for synthetic repositories (e.g. 2,000 .NET projects, 50k TS files) into a temp folder (not committed); benchmark script measuring analysis time, memory, projection time.
- **Acceptance criteria:** Results recorded in TEST_STRATEGY.md; MVP targets (PRODUCT_CONCEPT.md §8) evaluated.
- **Note:** Done 2026-11-18. Generator `SyntheticRepository` (TestRepositories: .NET solutions with project references, packages and cross-project type usage; npm workspaces with relative, workspace and package imports), explicit benchmarks `PerformanceBenchmarks` (IntegrationTests, real composition root incl. SQLite) and `tools/development/Run-Benchmarks.ps1` (parser-checked only; measured with the equivalent `dotnet test` commands). Results (TEST_STRATEGY.md §8): 100 .NET projects 5.6 s to the first graph (target < 30 s ✅), 2,000 projects 81.5 s (target < 3 min ✅, peak 783 MB), 50,502 TS files 148.1 s (peak 1.19 GB). Slice queries (ADR-010 follow-up): not needed for the targets — **ADR-012 (accepted 2026-11-18)** keeps whole-model reads for the MVP.

### GRAPH-009 — Repository level, folder fallback and navigation history
- **Priority:** P0 · **Milestone:** M4 · **Status:** done
- **Prerequisites:** GRAPH-006
- **Projects:** Graph, Graph.Tests, Desktop, GraphView.Web
- **Description:** L0 repository level (ecosystem/solution/workspace groups), folder-level graph for repositories without recognized projects (USER_WORKFLOWS.md WF-01), breadcrumbs and *Back* navigation restoring the previous view state (WF-03).
- **Acceptance criteria:** L0 projection tests; a fixture without projects shows a folder graph; Back restores expansion state and filters.
- **Note:** Done 2026-11-18 (GRAPH_ENGINE.md §3 "Implemented (GRAPH-009)"). `GraphLevels.Repository`: groups by workspace › solution › ecosystem (`group` nodes, expandable into projects, budget-aware, group edges); folder fallback for models without projects (`GraphLevels.Folder`, `folder` nodes); breadcrumbs include groups. Explorer: view mode *Projects/Grouped*, *Back* (50 states: expansions, filters, level, neighborhood; restores the filter controls), folder hint, group/folder/aggregate nodes are not inspected, module nodes are called "Module". Web view: group and folder styles (bundle rebuilt). Fixture `plain-folders` (EXPECTED.md). Tests: `GraphRepositoryLevelTests` (grouping, expansion edges, reversible collapse, budget, neighborhood into a collapsed group, folders), `FolderGraphTests` (fixture), ViewModel tests (Back step by step, grouped view). Known limitation: *Show in graph* cannot reveal elements inside a collapsed group. **Not verified manually** in the running app.

---

## M5 — Git integration

### GIT-006 — Spike: LibGit2Sharp verification
- **Priority:** P0 · **Milestone:** M5 · **Status:** done · **Spike (time-box 1 day)**
- **Prerequisites:** —
- **Projects:** Git (spike), docs
- **Description:** Verify LibGit2Sharp 0.32.x: native binaries on all RIDs, no hook/fsmonitor execution, HTTPS clone with token, shallow clone, performance of history walk; SSH status (OPD-02).
- **Acceptance criteria:** GIT_INTEGRATION.md §3 table updated with verified facts.
- **Note:** Done 2026-11-18 (GIT_INTEGRATION.md §3.1). LibGit2Sharp 0.32.0 + NativeBinaries 2.0.324 confirmed: native binaries for all target RIDs (glibc and musl); hooks, fsmonitor, filter/diff drivers, credential helpers and `core.sshCommand` are not executed. **Finding:** the native build supports SSH by starting the system `ssh` executable, so SSH URLs must be rejected by the app (OPD-02 stays open, options updated in FEATURE_ROADMAP.md). Shallow clones work over HTTPS only (not the local transport); the product clones full history because activity statistics need it. HTTPS clone verified for GitHub and GitLab; token clone only through the opt-in test (no private test repository available). History walk on 2,837 commits: 1.4 s without, 26.5 s with line counts — GIT-004 counts lines without rename detection and in parallel. Proof code: `LibGit2SharpVerificationTests` (two opt-in `Category=Network` tests). Package referenced by the Git module.

### GIT-001 — Git ports and LibGit2Sharp adapter (detect, snapshot)
- **Priority:** P0 · **Milestone:** M5 · **Status:** done
- **Prerequisites:** GIT-006
- **Projects:** Application (ports), Git, IntegrationTests, Directory.Packages.props
- **Description:** `IGitRepositoryReader.TryOpenAsync/ReadSnapshotAsync`; worktrees and `.git` files supported; sanitized remotes.
- **Acceptance criteria:** Tests with `GitRepositoryBuilder`; hostile `.git/config` executes nothing.
- **Note:** Done 2026-11-18. Port `IGitRepositoryReader` (Application/Git): `OpenAsync(path)` discovers the working tree containing the folder (also a parent, linked worktrees, `.git` files) and returns `GitRepositoryInfo` with `RootPrefix` (analyzed folder relative to the working tree) — `git.not-a-repository` (NotFound, also for bare repositories) or `git.read-failed`; `ReadSnapshotAsync` returns HEAD (null when unborn), branch (null when detached), uncommitted changes (untracked count, ignored files do not) and remotes sanitized by `GitRemoteUrls.Sanitize` (user info, query, fragment and the scp user part removed). Adapter `LibGit2RepositoryReader` (registered by `AddGitModule`) runs LibGit2Sharp on the thread pool and never writes. Tests: `GitRepositoryReaderTests` (builder repositories incl. worktree, subfolder, bare, corrupt config, detached, unborn, dirty variants, remotes; hostile config + hooks: no marker, no file changed; process-API guard over the Git module), `GitRemoteUrlsTests`. The reader is not used by the import yet (GIT-005).

### GIT-002 — GitRepositoryBuilder test helper
- **Priority:** P0 · **Milestone:** M5 · **Status:** done
- **Prerequisites:** GIT-006
- **Projects:** TestRepositories
- **Description:** Fluent builder creating temporary Git repositories with commits, authors, branches, renames; cleans up reliably (read-only files on Windows).
- **Acceptance criteria:** Used by GIT-001/003/004 tests; no nested `.git` committed.
- **Note:** Done 2026-11-18. `TestRepositories/Git`: `GitRepositoryBuilder` (`Create`, `Write`/`WriteLines`/`Delete`/`Rename` + `Commit` with explicit `GitTestAuthor` and time — default one hour after the previous commit, starting 2026-01-01 —, `Branch`, `Checkout`, `Merge` (no fast-forward), `DetachHead`, `Remote`, `AppendConfig`, `Hook`, `Worktree`) and `GitTestRepository` (`Commits`, `WorktreePath`, `CreateBareClone` for local-transport clone tests; `Dispose` deletes everything incl. read-only object files). Repositories always start on `main`, independent of the machine's `init.defaultBranch`. Built with LibGit2Sharp (test-only reference of TestRepositories). `GitRepositoryBuilderTests`, including a check that no fixture contains a nested `.git`.

### GIT-003 — HTTPS clone of GitHub/GitLab remotes
- **Priority:** P0 · **Milestone:** M5 · **Status:** done
- **Prerequisites:** GIT-001
- **Projects:** Application, Git, IntegrationTests
- **Description:** URL validation/sanitization, clone into `<data>/clones/<host>/<owner>/<repo>`, progress and cancellation, token via credentials callback (in-memory only until OPD-03), update (fetch + checkout) for app-managed clones, no submodules.
- **Acceptance criteria:** Rejected URL schemes tested; cancellation leaves no half-cloned directory registered.
- **Note:** Done 2026-11-18. `GitRemoteUrls.ParseCloneUrl` (HTTPS only, safe segments, no query/fragment, credentials stripped and reported; github.com/gitlab.com recognized, everything else `OtherGitRemote`), port `IGitRemoteCloner` with adapter `Cloning/LibGit2RemoteCloner` (full clone into `<target>.partial`, renamed when complete; token only via the credentials callback, asked once; progress `Receiving`/`CheckingOut`; cancellation through the transfer callback; no submodules, their presence reported; update = fetch + forced checkout + hard reset; every path must lie inside the clones directory), use case `ImportRemoteRepository` (`ExistingCloneAction` Ask/Update/Reclone, then the regular import with `RepositoryLocation.Cloned`; Contracts `RemoteRepositoryImportDto`). The root validator now accepts folders below the clones directory (not the data directory otherwise). `RemoveRepository` deletes the clone directory of app-managed clones (the open part of REPO-005). Tests: `GitRemoteUrlsTests` (accepted/rejected URLs incl. `ext::`, scp-like, local paths), `GitRemoteClonerTests` (local transport, test-only: branches, unknown branch, unreachable remote, cancellation, stale partial, existing target, paths outside the clones directory, submodules, token never written, update, delete), `ImportRemoteRepositoryTests` (substituted cloner: registration as clone, nothing registered after cancel/failure, Ask/Update/Reclone, remove deletes clones only). Opt-in network test `RemoteImportNetworkTests`: clone → analyze → update of a public GitHub and GitLab repository, passed 2026-11-18. Private repositories with a token: not verified (no test token).

### GIT-004 — File activity statistics
- **Priority:** P0 · **Milestone:** M5 · **Status:** done
- **Prerequisites:** GIT-001, GIT-002
- **Projects:** Git, IntegrationTests
- **Description:** Single-pass history walk within `GitHistoryWindow`: per-file commit count, distinct authors (count only), first/last change, lines added/deleted.
- **Acceptance criteria:** Correct numbers on builder-created histories; bounded runtime on the performance fixture.
- **Note:** Done 2026-11-18. `IGitRepositoryReader.ReadFileActivityAsync(info, GitHistoryWindow, progress)` → `GitFileActivityReport` (files relative to the analyzed folder, `CommitsScanned`, `OldestCommitAt`, `ReachedCommitLimit`). `History/FileActivityWalker`: commits from HEAD in time order within the window (default 5,000 commits / 365 days, committer time), merge commits skipped, diffs against the first parent without rename detection, binary files count commits but no lines, distinct authors by case-insensitive e-mail (kept only during the walk); commits are diffed in parallel (≤ 8 workers, one `Repository` each) and merged deterministically. Tests: `GitFileActivityTests` (hand-computed numbers incl. rename = delete + add, merges, commit limit, since-date, subfolder, author e-mail case, binary, empty repository, 60-commit determinism + progress, cancellation). Runtime: 8.8 s on 2,511 commits of `libgit2/libgit2sharp` (TEST_STRATEGY.md §8). **Open:** "bounded runtime on the performance fixture" waits for QA-002 (M4); the window bounds the work meanwhile.

### GIT-005 — Persist Git data and activity overlay
- **Priority:** P0 · **Milestone:** M5 · **Status:** done
- **Prerequisites:** GIT-004, STORE-004
- **Projects:** Storage, Application, Graph, Desktop
- **Description:** `GitSnapshots`/`GitChanges` tables and migration; aggregation to projects/modules; activity overlay in graph and inspector.
- **Acceptance criteria:** Overlay matches persisted numbers; repositories without Git work unchanged.
- **Note:** Done 2026-11-18. Tables `GitSnapshots`/`GitChanges` (migration `AddGitActivity`, `GitActivityStore` behind the new port `IGitActivityStore`; statistics keyed by file id, so a model replacement cascades them away). `ImportLocalRepository` reads the snapshot before the analysis (run `CommitSha`/`Branch` — the run history now shows them) and the activity after it (stage `ReadingGitHistory`), stores both after the model; unreadable repositories add a `git.read-failed` warning, folders without Git nothing. The walker restricts diffs to the analyzed subfolder (pathspec). Aggregation `GitActivityIndex` (Application): **file changes** = sum of the files' commit counts per project / module (entity declarations of the subtree) / entity; shared by `GetGraphView` (`GraphQuery.IncludeActivity` → `GraphNodeMetricsDto.ChangeCount`) and `GetElementDetails` (`ElementDetailsDto.Activity`). Overlay: *Git activity* toggle in the explorer (not persisted), halo via Cytoscape `underlay-*` growing with `log10(1 + changes)`; inspector line "N file changes in M files · +a −d lines · last change … · window …". Tests: `GitActivityEndToEndTests` (persisted snapshot and numbers, overlay and inspector equal an independent recomputation from the stored rows incl. module and type nodes, no-Git folder unchanged, unreadable repository = warning, re-import replaces, remove deletes), explorer view-model tests (toggle patches in/out, hint without Git data, inspector text), Vitest `activityHeat` and palette contrast. **Not verified:** the overlay's look in the running app (no manual UI check in this session).

### UI-008 — Clone dialog
- **Priority:** P0 · **Milestone:** M5 · **Status:** done
- **Prerequisites:** GIT-003
- **Projects:** Desktop
- **Description:** URL, optional branch and token (masked), progress, cancel, existing-clone handling.
- **Acceptance criteria:** Token never displayed after entry and never logged (log test).
- **Note:** Done 2026-11-18. Implemented as an inline form on the start page (*Clone remote repository…*), like the inline remove confirmation of REPO-005 — there is no dialog service; a modal window can follow if wanted. `StartPageViewModel` (partial `StartPageViewModel.Clone.cs`) calls `ImportRemoteRepository`: address, optional branch, token in a masked `TextBox` (`PasswordChar`) that is emptied as soon as the clone starts (the token stays in a private field only for the update/re-clone choice and is dropped afterwards); credentials typed into the address are removed from the field as well; progress *Receiving objects / Checking out files* then the analysis stages in the shared panel; cancel; existing clone → *Update existing clone* / *Clone again*; status mentions removed URL credentials and uncloned submodules. Recent repositories show *Clone of <url>* and the remove confirmation says that the clone is deleted (`RecentRepositoryDto.RemoteUrl`). Logging: error codes and exception types only. Tests: `CloneFormViewModelTests` (success, token emptied at start, failures and an exception containing the token — token in no log entry, message, notification or field —, existing clone + update with the entered token, cancel registers nothing, invalid address, command state). **Not verified:** the form in the running app (no manual UI check in this session).

---

## M6 — Documentation export

### EXPORT-001 — Markdown/Mermaid architecture report
- **Priority:** P0 · **Milestone:** M6 · **Status:** done
- **Prerequisites:** STORE-004
- **Projects:** Application (use case and `Export/` folder with the Markdown/Mermaid renderer: pure text generation, no I/O), Contracts (export DTOs), Desktop (file writing via the save dialog)
- **Description:** Report with overview, languages, projects, Mermaid project graph (budgeted), external dependencies, analysis metadata and evidence statistics.
- **Acceptance criteria:** Snapshot test of the report for `dotnet-layered`; Mermaid output is valid (escaping of names tested).
- **Note:** Done 2026-11-18. Use case `ExportArchitectureReport` (Application/Export) → `ArchitectureReportDto` (file name `ARCHITECTURE-<repo>-<yyyy-MM-dd>.md`, Markdown, truncation flag; Contracts/Export with `ArchitectureReportRequest`/`ReportSections`). `MarkdownReportRenderer` is pure and deterministic: title and scope, overview, languages, projects (kind, folder, files, references in/out, packages), Mermaid dependency graph, external dependencies (used by), analysis metadata (status, times in UTC, commit/branch, files, warnings/errors, analyzer versions), evidence statistics (relationship kinds × levels, evidence per analyzer). No absolute local path is written. Scope: whole repository = project level incl. test projects with a node budget of 100; current view = the explorer's query (expansions become subgraphs). `MermaidDiagram`: generated ids, quoted labels with numeric entity codes for every special character, at most 500 edges (Mermaid default), edges in node order. `MarkdownText` escapes ` ` * _ [ ] | ~` and HTML. Tests: `ExportArchitectureReportTests` (snapshot `Export/Snapshots/dotnet-layered.md`, hostile npm package name, current view with subgraphs and section order, validation, file names; TEST_STRATEGY.md §5.2). Mermaid validity is checked against the emitted grammar, not by a Mermaid renderer (not verified in a viewer).

### EXPORT-002 — Export dialog
- **Priority:** P0 · **Milestone:** M6 · **Status:** done
- **Prerequisites:** EXPORT-001
- **Projects:** Desktop
- **Description:** Scope and section selection, target folder (default outside repository), open folder after export.
- **Acceptance criteria:** Writing into the analyzed repository requires explicit confirmation.
- **Note:** Done 2026-11-18. Implemented as the navigation page *Export* (`ExportPageViewModel`, `ExportPageView`) rather than a modal dialog, like the other pages (no dialog service exists). Scope: whole repository or the Architecture Explorer's current view (`ArchitectureExplorerPageViewModel.CurrentQuery`; enabled only while it shows the current repository); section check boxes; target folder (default: the user's documents folder) with *Choose folder…*; a target inside the analyzed folder — also an app-managed clone — shows a warning and enables *Export report* only after ticking an explicit confirmation, which is reset whenever the target changes; *Open the folder after the export* (`IFolderLauncher`). `ReportFileWriter` (Desktop service) writes UTF-8 without BOM with `FileMode.CreateNew` and never overwrites (`name (2).md`, …). Tests: `ExportPageViewModelTests` (empty state, export into the chosen folder + open + no overwrite, confirmation inside the repository and its reset, no section / relative target, current view, write failure inline, `IsInsideRepository` segment comparison). **Not verified:** the page in the running app.

---

## M7 — Optional local AI

### AI-001 — AI module foundation, settings and provider gate
- **Priority:** P0 · **Milestone:** M7 · **Status:** done
- **Prerequisites:** STORE-005
- **Projects:** Application (port `IAiAssistant`), AI, Desktop (settings UI), IntegrationTests, Directory.Packages.props
- **Description:** Add `Microsoft.Extensions.AI` (10.x, re-verify); AI disabled by default; provider registry; gate enforcing enabled/configured/consent; network guard test proving no outbound calls when disabled.
- **Acceptance criteria:** With default settings no `IChatClient` is constructed and no network handler is invoked (test).
- **Note:** Done 2026-11-19. `Microsoft.Extensions.AI` **10.11.0** (re-verified on nuget.org 2026-11-19: 11.0.0 exists but depends on the 11.0 runtime packages; 10.11.0 depends on Microsoft.Extensions.* 10.0.13). Application/AI: `AiSettings` (enabled, provider, endpoint, model, context tokens, temperature, remote consents; `IsRemoteEndpoint` = not loopback), `AiSettingsStore` (keys `ai.*` in `UserSettings`; damaged values fall back to "off"; changing the endpoint withdraws every consent), port `IAiAssistant` (`CheckProviderAsync` here, grown by AI-002…AI-008), `AiErrorCodes`. AI module: `AiProviderGate` (registry of `IAiProvider`s; `ForConnection` = enabled + known provider + valid endpoint; `ForChat` additionally a model and, for remote endpoints/cloud providers, the repository's consent), `AiTransport` (the only place HTTP clients are created; no redirects, no cookies). Settings page: AI section (enable, endpoint with remote warning, model, context size, temperature, save). Tests: `AiProviderGateTests`, `AiSettingsStoreTests`, `AiNetworkGuardTests` (real composition root with a guarded `HttpMessageHandler` and a spy provider: nothing constructed, nothing sent), `SettingsPageViewModelTests` (AI section).

### AI-002 — Ollama provider
- **Priority:** P0 · **Milestone:** M7 · **Status:** done (manual test with a real Ollama instance pending)
- **Prerequisites:** AI-001
- **Projects:** AI, Desktop
- **Description:** `OllamaSharp` (re-verify) as `IChatClient`; endpoint health check; model listing; non-loopback endpoint treated as remote.
- **Acceptance criteria:** Integration test against a stub HTTP server; manual test with a real Ollama instance documented.
- **Note:** Done 2026-11-19. `OllamaSharp` **5.5.2** (MIT, re-verified 2026-11-19: latest stable, net10.0 asset, depends on Microsoft.Extensions.AI 10.8.0, resolved to 10.11.0). `OllamaProvider` (AI/Providers): `CheckAsync` = `IsRunningAsync` + `ListLocalModelsAsync` (sorted) + optional `GetVersionAsync`, 10 s timeout, failures → `ai.provider-unavailable` with a reason (no exception text, no content logged); `CreateChatClient` = `OllamaApiClient` over an `AiTransport` client (10 min timeout) that is disposed with the chat client. Non-loopback endpoints are remote through the gate (AI-001). Settings page: *Test connection* lists the models (auto-complete for the model field). **Found:** OllamaSharp ends a streaming answer quietly when the token is cancelled instead of throwing, so callers check the token after streaming. Tests: `OllamaProviderTests` against `StubOllamaServer` (a loopback HTTP/1.1 stub with `/`, `/api/tags`, `/api/version`, NDJSON `/api/chat`): model listing, server error, nothing listening, streaming with model/temperature in the request, cancellation; `AiNetworkGuardTests` (real provider with default settings sends nothing; enabled settings check the stub). **Manual test with a real Ollama instance: not yet done** (no Ollama on the development machine); the steps are in [docs/user/AI_MODEL_SETUP.md](../user/AI_MODEL_SETUP.md) §6.

### AI-003 — Search chunk index
- **Priority:** P0 · **Milestone:** M7 · **Status:** done
- **Prerequisites:** STORE-004
- **Projects:** Storage, Analysis (chunking during analysis or post-step — decided in task), Storage.Tests
- **Description:** Verify FTS5 availability in the bundled SQLite; create `SearchChunks`; chunk source files (secret-file exclusions); query API.
- **Acceptance criteria:** Verification result documented in DATABASE_SCHEMA.md §4.18; search tests pass.
- **Note:** Done 2026-11-19. **FTS5 verified** in the bundled SQLite (DATABASE_SCHEMA.md §4.18). **Decided ([ADR-013](../decisions/ADR-013-search-index-on-first-ai-use.md)):** not during analysis — the Application use case `EnsureSearchIndex` builds the index on first AI use per run (Analysis is unchanged). Application/AI: port `ISearchIndexStore` (`GetStateAsync`, `ReplaceAsync`, `SearchAsync`), `SearchChunk`/`SearchHit`/`SearchIndexState`, `SourceChunker` (40-line windows every 30 lines), `SearchTerms` (identifier splitting, stop words, only letters/digits), `SecretFilePolicy` (`.env*`, keys/certificates, credential files, `appsettings.*.json`; only source/test/manifest/configuration/documentation files). Files changed since the analysis (hash) are skipped. Storage: migration `AddSearchIndex` (FTS5 `SearchChunks` with `Keywords` column, `SearchIndexStates`, delete trigger), `SearchIndexStore` (prepared inserts in one transaction, quoted `OR` prefix query, `bm25`). Tests: `SearchIndexStoreTests` (FTS5 compile option, ranking per repository, prefix/keywords, replace, delete trigger, no query syntax), `SearchTextTests`, `EnsureSearchIndexTests` (dotnet-layered: finds `PlaceOrderHandler.cs`, secret and changed files not indexed, rebuild only for a new run).

### AI-004 — Retriever and prompt builder
- **Priority:** P0 · **Milestone:** M7 · **Status:** done
- **Prerequisites:** AI-003
- **Projects:** AI, Application
- **Description:** Facts-first retrieval (model queries + FTS + evidence snippets), citation keys, token budgeting, system prompt rules.
- **Acceptance criteria:** Deterministic context for a given question and model (snapshot tests); context manifest produced.
- **Note:** Done 2026-11-19. `ContextRetriever` (AI/Retrieval) collects, in priority order: facts about the selected element (declaration, members, direct relationships with their first evidence location), the repository overview (languages, projects, project/package/framework references with evidence), top-level types whose names match the question (identifier words, crude stemming `SearchTerms.Stem`); snippets: declarations of the selected/matched types (length from the entity metrics, ≤ 60 lines), lines around relationship evidence (±3), FTS hits (12, terms + stems). Snippets are read through `SnippetSource` only from allowed files whose hash still matches the analysis; mostly-overlapping snippets are dropped. `PromptBuilder` (AI/Prompting, pure): system prompt with the six rules of AI_ARCHITECTURE.md §2, history (most recent first, ≤ 20 % of the window), context block `<context>` with `[F1]` facts (+ `evidence: path:start-end`) and `[S1]` fenced snippets, `Question:`; budget = window − answer reserve (25 %, ≥ 1024) − system/question/history, facts up to 50 % first, then snippets, then remaining facts; tokens estimated as characters / 4. Repository text cannot close the context block (`</context>` neutralized, fence longer than any backtick run). Application/AI: `AiContextManifest`/`AiContextEntry` (key, kind, label, path, lines, element, tokens; omitted count), `AiPromptMessage`, `AiRole`. Tests: `PromptContextTests` (dotnet-layered snapshots `AI/Snapshots/prompt-repository.txt` and `prompt-element.txt`, determinism, small budget keeps facts first and reports omissions), `PromptBuilderTests` (format, injection hardening, history), `SearchTextTests` (stems).

### AI-005 — Citation validator
- **Priority:** P0 · **Milestone:** M7 · **Status:** done
- **Prerequisites:** AI-004
- **Projects:** AI, Application
- **Description:** Parse `[F1]`/`[S1]` keys and `path:start-end` citations; validate against model; mark verified/unverified.
- **Acceptance criteria:** Tests with valid, invalid, out-of-range and fabricated citations.
- **Note:** Done 2026-11-19. `CitationValidator` (AI/Citations, pure): key citations `[F1]`, `[s2]`, `[F1, S2]` are verified when the key is in the context manifest and its element (if any) still exists in the model, and resolve to the entry's path/lines/element; `path:start-end` (also `path:line`, en dash, backticks, `./`, backslashes) is verified when the file is in the model and the range lies within its line count; a bare file name resolves if it is unique; strings without a folder that match no file (`localhost:11434`, `example.com:443`, times) are not citations. Each citation once, in order of appearance, at most 200. Application/AI: `AiCitation` (text, key, path, lines, element, verified, problem). Tests: `CitationValidatorTests` (dotnet-layered model: verified keys and paths, grouped keys, fabricated key, stale element, out-of-range/invalid/unknown file, non-citations).

### AI-006 — AI panel and conversation persistence
- **Priority:** P0 · **Milestone:** M7 · **Status:** done (not yet checked manually in the running app or with a real Ollama instance)
- **Prerequisites:** AI-002, AI-005
- **Projects:** Storage (tables + migration), Application, Desktop
- **Description:** Streaming answers, sanitized Markdown, clickable citations (focus graph / open snippet), "AI-generated" label, context manifest view, conversation history.
- **Acceptance criteria:** Raw HTML in model output is not rendered; cancelling a streaming answer works.
- **Note:** Done 2026-11-19. `IAiAssistant.AskAsync` (AI module): gate (enabled, model, remote consent) → `EnsureSearchIndex` → `ContextRetriever` → `PromptBuilder` → streaming `IChatClient` (no tools, temperature and output limit from the settings, answer cut at 40,000 characters, token checked after streaming because OllamaSharp ends quietly) → `CitationValidator`; HTTP 404 → `ai.model-not-found`, other transport failures → `ai.request-failed`; logs contain sizes and durations only. Application/AI: use cases `AskRepositoryQuestion` (validates the question, continues or starts a conversation with its history, stores question and answer only after a complete answer) and `AiConversations` (list, messages, delete), port `IAiConversationStore`, `AiQuestion`/`AiAnswer`; Contracts/AI DTOs. Storage: migration `AddAiConversations` (`AIConversations`, `AIMessages`, `AIArtifacts`), `AiConversationStore` (citations and manifest as JSON). Desktop: page *Assistant* (`AssistantPageViewModel`, `AssistantPageView`): conversation list (new, delete), question box (Ctrl+Enter), *about the selected element* check box (explorer's inspected element, new `InspectedElementId`), streaming text, answer as plain-text blocks (`MarkdownBlocks`: headings, list items, paragraphs, fenced code; HTML, images and links stay literal text), "AI-generated" label, citation buttons (verified: show cited lines next to the answer or focus the element in the graph via `ArchitectureExplorerPageViewModel.ShowElementAsync`, which uses the new `ElementDetailsDto.Reference`; unverified: flagged ⚠ with the reason), expandable context manifest, Cancel (nothing saved), consent box for remote endpoints, permanent remote indicator. Tests: `AiConversationStoreTests`, `AskRepositoryQuestionTests` (stub Ollama: streaming, validated citations, stored conversation, history in follow-up request, no tools, disabled/consent/404/500 store nothing, cancellation), `AiNetworkGuardTests` (asking with default settings indexes and sends nothing), `AssistantPageViewModelTests` (answer, history reload, HTML literal, citation opens lines, cancel, consent), `MarkdownBlocksTests`.

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
- **Note (2026-11-18, partial, on the user's request):** part of the cache cleanup exists — the Settings page shows the disk usage of the database and backups, opens the data and log folders in the file manager (`IFolderLauncher`) and compacts the database (`IStorageMaintenance`, `VACUUM`; DATABASE_SCHEMA.md §5). Deleting all data from inside the app, analysis limits/excludes, history window and the editor template remain open.

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
