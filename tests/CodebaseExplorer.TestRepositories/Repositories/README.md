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

No fixtures exist yet. The first one (`dotnet-layered`) is created by task `REPO-004`.
