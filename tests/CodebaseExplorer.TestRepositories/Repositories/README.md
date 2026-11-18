# Fixture repositories

This folder contains small, purpose-built repositories that serve as **input data** for analyzer
and integration tests (see `docs/implementation/TEST_STRATEGY.md`, section "Test repositories").

## Rules

1. **Data, not code.** Fixture files are never compiled, restored, built or executed — not by the
   test suite and not by Codebase Explorer. The owning project excludes `Repositories/**` from
   compilation.
2. **One folder per fixture**, named `<ecosystem>-<scenario>`, for example `dotnet-layered`,
   `ts-monorepo-workspaces`, `python-src-layout`, `mixed-polyglot`.
3. **Minimal.** Each fixture contains only what is needed to exercise a scenario. Method bodies
   may be empty.
4. **Documented expectations.** Each fixture has an `EXPECTED.md` listing the projects, modules and
   relationships an analyzer must find, so that tests assert against a written specification and
   not against whatever the analyzer currently produces.
5. **Hostile fixtures are welcome.** Fixtures that contain malicious-looking MSBuild targets,
   `postinstall` scripts or `setup.py` code are used to prove that nothing is executed.
6. **Git history fixtures** are created at test time by a builder (task `GIT-002`), not committed
   as nested `.git` folders.

## Fixtures

| Fixture | Task | Purpose |
|---|---|---|
| `dotnet-layered` | REPO-004 | 5-project layered .NET solution (`.sln` + `.slnx`), central package management with a `VersionOverride`, a false MSBuild condition, a reference to a missing project, a `FrameworkReference`, multi-targeting. Oracle for ANALYSIS-002 … ANALYSIS-004. |
| `dotnet-advanced` | REPO-006 | 2-project .NET solution (`.slnx`): multi-targeting core with partial, nested and generic types, struct, enum, delegate; services project with a `VersionOverride`, true, false and unsupported (`Exists()`) conditions, a missing project reference and DI registrations including negative cases. Oracle for ANALYSIS-005 … ANALYSIS-007 and regression input for ANALYSIS-002 … ANALYSIS-004. |
| `dotnet-hostile` | QA-003 | Hostile .NET repository: MSBuild `Exec` targets in `Directory.Build.props` and the project, property functions, an imported target, analyzer/source-generator references (incl. a fake `.dll`) and a `[ModuleInitializer]` — every payload only writes a `MARKER-*.txt` file. Proves that the import executes nothing (`HostileDotNetFixtureTests`, `ZeroExecutionHarness`). |

Fixture files use LF line endings and are stored byte-exact (`.gitattributes`), because
`EXPECTED.md` documents exact line/column positions.
