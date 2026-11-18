# Test Strategy — Codebase Explorer

> Status: **accepted** · Last reviewed: 2026-11-17
> Implemented so far: test infrastructure (xunit.v3 on Microsoft.Testing.Platform, NSubstitute),
> architecture rule tests, composition root tests, application paths tests, and Domain unit tests
> for IDs, natural keys, value objects, all model elements and `ArchitectureModel` validation —
> Application result/error/progress tests, file-reader/root-validation, gitignore, scanner,
> language/marker detection, `dotnet-layered` fixture tests, analysis pipeline, solution and
> project-file parsers, the .NET project-system analyzer, graph projection and graph JSON
> round-trips, and the import/graph use cases including an end-to-end test over `dotnet-layered`
> — and the graph host bridge, navigation policy and app manifest (GRAPH-004), and the Start page import ViewModel (UI-002) the Architecture Explorer ViewModel (UI-003) and the shell status (UI-001), and edge evidence (GRAPH-010) — 776 tests in total (4 skipped on
> this Windows machine: 3 symbolic-link tests without link permission, 1 explicit benchmark).

## 1. Goals

1. **Correctness of analysis** against written expectations, not against current output.
2. **Architecture integrity** — dependency rules can't silently erode.
3. **Security guarantees** — proof that repository code is never executed.
4. **Determinism** — same input, same model.
5. **Fast feedback** — the default `dotnet test` run completes in minutes, without network.

## 2. Tooling

| Tool | Version (CPM) | Purpose |
|---|---|---|
| xunit.v3 | 4.0.3 | Test framework; test projects are executables (`OutputType=Exe`). |
| Microsoft.Testing.Platform | via xunit.v3; opted in via `global.json` `test.runner` | Runner for `dotnet test` on the .NET 10 SDK. |
| NSubstitute | 6.2.0 | Test doubles for ports. |
| coverlet.MTP | 10.1.1 | Code coverage as a Microsoft.Testing.Platform extension (QA-001, §7.1); collects only when `--coverlet` is passed. |
| Vitest | 5.1.0 (npm, exact) | Tests of the graph web view (`src/CodebaseExplorer.GraphView.Web/test`): message validation, controller, element mapping, static security checks. Run with `npm test`; not part of `dotnet test`. |

Run all tests:

```bash
dotnet test --solution CodebaseExplorer.slnx
```

Run one project / filter (xunit.v3 MTP options):

```bash
dotnet test --project tests/CodebaseExplorer.IntegrationTests --filter-class "*ProjectReferenceRulesTests"
```

> Temporary: test projects without tests return exit code 8; `tests/Directory.Build.props` passes
> `--ignore-exit-code 8` until CORE-010 removes it.

## 3. Test levels

| Level | Project(s) | Scope | Rules |
|---|---|---|---|
| **Unit** | Domain.Tests, Graph.Tests, parts of Analysis.Tests | Pure logic: invariants, IDs, projection, parsers on inline input. | No file system (except temp dirs where unavoidable), no network, < 50 ms per test. |
| **Analyzer (fixture)** | Analysis.Tests | Analyzers and pipeline against fixture repositories. | Assert against `EXPECTED.md`-derived expectations; fixtures are read-only. |
| **Storage** | Storage.Tests | Mappings, migrations, queries against real SQLite (temp file or shared in-memory connection). | No mocking of EF Core; each test owns its database. |
| **Integration** | IntegrationTests | Architecture rules, composition root, end-to-end use cases (fixture → analysis → storage → graph DTO), hostile fixtures, Git via builder. | May take seconds; no network unless marked. |
| **ViewModel** | IntegrationTests (or a future Desktop.Tests project if volume grows — requires doc update) | ViewModels with substituted use cases. | No Avalonia rendering. |
| **UI / manual** | — | Graph rendering and platform checks per OS. | Checklist in PR description for UI tasks; headless Avalonia testing to be evaluated in UI-001. |
| **Performance** | tools/development + generated repositories | Benchmarks for analysis, persistence, projection. | Not part of default CI run; results recorded below. |

## 4. Test repositories (fixtures)

Location: `tests/CodebaseExplorer.TestRepositories/Repositories/<name>/`; located via
`TestRepositoryCatalog.GetFixturePath(name)`. Rules in the folder's README:

- Fixtures are **data**: excluded from compilation, never built/restored/executed.
- Each fixture has `EXPECTED.md` (projects, modules, entities, relationships with locations,
  external dependencies, expected diagnostics).
- Git histories are generated at test time (`GitRepositoryBuilder`, GIT-002).
- Large performance repositories are **generated** (QA-002), never committed.

Planned fixtures:

| Fixture | Task | Purpose |
|---|---|---|
| `dotnet-layered` | REPO-004 (**exists**) | 5-project clean-architecture solution; M1 oracle. |
| `dotnet-advanced` | REPO-006 | Partial types, generics, multi-targeting, conditions, CPM overrides, missing references. |
| `ts-monorepo-workspaces` | REPO-007 | npm/pnpm workspaces, tsconfig paths and project references. |
| `js-commonjs` | REPO-007 | `require`, `module.exports`, index resolution. |
| `python-src-layout` | REPO-007 | `pyproject.toml`, `src/` layout, relative imports. |
| `mixed-polyglot` | REPO-007 | .NET + TS + Python in one repository. |
| `hostile-*` | QA-003 | MSBuild `Exec`, analyzer packages, npm lifecycle scripts, `setup.py`, Git hooks/fsmonitor. |
| `edge-files` | REPO-001/002 | Symlinks, huge files, binary files, deep nesting, unusual encodings. |

## 5. What every feature task must test

| Change type | Required tests |
|---|---|
| Domain type / invariant | Unit tests for each invariant and edge value. |
| Analyzer | Fixture test against `EXPECTED.md`; malformed-input test (diagnostic, no exception); determinism test; evidence location assertions. |
| Storage mapping / migration | Round-trip test; migration from previous version; index existence where specified. |
| Graph projection | Projection tests on hand-built models incl. budget and filter combinations. |
| Use case | Success, expected failure (typed error), cancellation. |
| ViewModel | Command enable/disable, state transitions, error display, with NSubstitute doubles. |
| Security-relevant code | Negative tests (traversal, oversized input, hostile content). |
| Web view (TS) | Vitest for message validation and label handling. |

## 6. Conventions

- Test class per unit under test: `<Subject>Tests`; method names `Method_State_ExpectedResult`
  (underscores allowed in tests, CA1707 disabled for `tests/**`).
- Arrange/Act/Assert structure; one behavior per test; parameterized tests (`[Theory]`) for tables.
- No `Thread.Sleep`; use `TimeProvider` fakes and `CancellationToken`s.
- Temp directories via a helper that deletes on dispose; tests must clean up.
- Tests never depend on the developer machine's state (installed SDK packs, Git config, Ollama) —
  where a dependency is optional (e.g. SDK reference packs), tests cover both presence and absence
  via injectable locators.
- Network tests are opt-in (trait `Category=Network`) and excluded from default runs.

## 7. Quality gates

| Gate | Where | Threshold |
|---|---|---|
| Build | local + CI | 0 warnings (warnings are errors), 0 errors |
| Tests | local + CI (3 OSes) | 100 % passing |
| Architecture rules | IntegrationTests | must pass; changes require ADR |
| Hostile fixtures | IntegrationTests (from M3) | must pass on all OSes |
| Coverage | CI (Linux job, QA-001) | reported as an artifact; target ≥ 80 % line coverage for Domain, Analysis, Graph; no hard gate yet (§7.1) |
| Performance | QA-002 benchmarks | MVP targets in PRODUCT_CONCEPT.md §8; regressions > 20 % require explanation |

### 7.1 Continuous integration and code coverage (QA-001)

`.github/workflows/ci.yml` runs on every push to `main` and every pull request:

- **.NET build and test** on `windows-latest`, `macos-latest` and `ubuntu-latest` (Release, SDK from
  `global.json`); a green build means 0 warnings.
- **Graph web view** on Linux: `npm ci --ignore-scripts`, `npm test`, `npm run build`, then a check that
  the committed bundle in `Desktop/GraphView` is unchanged.
- **Coverage** in the Linux .NET job, uploaded as artifact `coverage-cobertura` (one Cobertura file
  per test project; assemblies `CodebaseExplorer*` without `TestRepositories`).

**Decision (2026-11-18):** coverage uses **coverlet.MTP** (MIT, native Microsoft.Testing.Platform
extension). Microsoft.Testing.Extensions.CodeCoverage was the alternative; it ships under a Microsoft
license file instead of an OSI license, and the VSTest-based coverlet collector does not work with MTP.
Coverage is **reported, not gated**: each assembly is measured by several test projects and the files
are not merged yet, so a threshold would need a merge step (e.g. ReportGenerator) — revisit when a
gate is wanted. Baseline (Release, measured by the assembly's own test project): Domain 95.2 %,
Analysis 96.2 %, Graph 94.4 % line coverage.

Run locally:

```bash
dotnet test --solution CodebaseExplorer.slnx --coverlet --coverlet-output-format cobertura
```

## 8. Recorded results

| Date | Benchmark | Result | Notes |
|---|---|---|---|
| 2026-11-17 | Scanner, 10,000 small files in 100 directories (`RepositoryScannerTests.Scan_TenThousandFiles_CompletesWithinFiveSeconds`, explicit test) | 2.7 s | Debug build, Windows 11, local SSD; target < 5 s (REPO-002). Run: `dotnet test --project tests/CodebaseExplorer.Analysis.Tests --filter-method "*Scan_TenThousandFiles*" --explicit only --output Detailed` |

## 9. Accessibility checklist (QA-005)

- All shell actions reachable by keyboard; visible focus.
- Graph selection navigable by keyboard (next/previous node, open inspector).
- Contrast ≥ WCAG AA for text in light and dark themes.
- Color is never the only carrier of meaning (evidence level also via line style/badge text).
