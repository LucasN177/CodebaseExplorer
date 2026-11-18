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
| `ts-monorepo-workspaces` | REPO-007 | npm workspace root with 3 packages (`@acme/core`, `@acme/shared`, `web`): workspace and version-spec project references, JSONC `tsconfig` files with `extends`/`paths`/references (incl. a solution-style root), `exports`/`main` entry points, a `postinstall` script, relative/alias/workspace/package/undeclared/dynamic imports and an unresolved one. Oracle for ANALYSIS-009/010. |
| `js-commonjs` | REPO-007 | CommonJS package without TypeScript: `require` of relative files, directories, JSON, built-ins and a computed argument; `module.exports`/`exports.x`. Oracle for ANALYSIS-009/010. |
| `python-src-layout` | REPO-007 | PEP 621 project with `src/` layout, optional dependencies and a `requirements-dev.txt` (option, marker, include outside the repository); relative, absolute, mapped (`yaml` → `PyYAML`), undeclared and standard-library imports. Oracle for ANALYSIS-011/012. |
| `mixed-polyglot` | REPO-007 | .NET API, TypeScript frontend and Poetry-based Python tool side by side, no root project. |
| `plain-folders` | GRAPH-009 | Notes, docs and scripts without any project manifest: the graph falls back to folders (WF-01). |
| `node-hostile` | ANALYSIS-009 | Hostile npm workspace: lifecycle scripts, `.npmrc` `script-shell`, node-gyp action, `file:` dependency with `postinstall`, module with a top-level side effect, TypeScript plugin — every payload only writes a `MARKER-*.txt` file. Proves that nothing is run (`HostileNodeFixtureTests`). |
| `python-hostile` | ANALYSIS-011 | Hostile Python project: `setup.py`, in-tree PEP 517 build backend, `conftest.py`, `sitecustomize.py`/`usercustomize.py`, `.pth` file, package `__init__`/`__main__`, `-e .` and index options — every payload only writes a `MARKER-*.txt` file (`HostilePythonFixtureTests`). |
| `dotnet-hostile` | QA-003 | Hostile .NET repository: MSBuild `Exec` targets in `Directory.Build.props` and the project, property functions, an imported target, analyzer/source-generator references (incl. a fake `.dll`) and a `[ModuleInitializer]` — every payload only writes a `MARKER-*.txt` file. Proves that the import executes nothing (`HostileDotNetFixtureTests`, `ZeroExecutionHarness`). |

Fixture files use LF line endings and are stored byte-exact (`.gitattributes`), because
`EXPECTED.md` documents exact line/column positions.
