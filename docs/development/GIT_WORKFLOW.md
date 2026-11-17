# Git Workflow — Codebase Explorer

> Status: **binding** · Last reviewed: 2026-11-17

## 1. Branching

- `main` is always buildable and green. No direct commits to `main` except the initial foundation.
- One branch per task: `<type>/<task-id>-<short-slug>`, e.g. `feature/ANALYSIS-003-csproj-parser`,
  `fix/REPO-002-gitignore-negation`, `docs/ADR-010-git-cli-adapter`, `spike/GRAPH-001-webview`.
- Types: `feature`, `fix`, `refactor`, `docs`, `test`, `chore`, `spike`.
- Spike branches are not merged as production code; their written results are merged via a `docs/` branch.

## 2. Commits

Format (Conventional Commits with task ID):

```text
<type>(<scope>): <summary in imperative mood> [<TASK-ID>]

<optional body: what and why, not how>
```

- `type`: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `build`, `perf`.
- `scope`: module name (`domain`, `analysis`, `graph`, `storage`, `git`, `ai`, `desktop`, `docs`, `build`).
- Example: `feat(analysis): parse ProjectReference items from csproj [ANALYSIS-003]`.
- Small, coherent commits; each commit builds. Do not mix refactoring and behavior changes.
- Never commit secrets, local databases, `bin/`/`obj/`, `node_modules/` (see `.gitignore`).

## 3. Pull requests

- One task per PR (exceptions: tightly coupled tasks explicitly listed).
- Title: `<TASK-ID>: <task title>`.
- Description template:

```markdown
## Task
<TASK-ID> — <title>

## Summary
- …

## Tests
- `dotnet build CodebaseExplorer.slnx` ✅
- `dotnet test --solution CodebaseExplorer.slnx` ✅ (<n> tests)
- Manual checks: <OS, scenario> / n/a

## Documentation
- Updated: …
- Task status updated in TASK_BREAKDOWN.md ✅

## Follow-ups
- …
```

- Merge strategy: squash merge; the squash message follows the commit format.
- Reviews check the [Definition of Done](../implementation/DEFINITION_OF_DONE.md).

## 4. Releases (from M8)

- Semantic versioning starting at `0.1.0` for the first MVP preview; tags `v<version>`.
- Release notes list implemented features (never planned ones as available).

## 5. Hosting

The repository is hosted on GitHub (`origin`). CI is planned as GitHub Actions with a build/test
matrix for Windows, macOS and Linux (task QA-001); CI specifics are added here when QA-001 is done.
