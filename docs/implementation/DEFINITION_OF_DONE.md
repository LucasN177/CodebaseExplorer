# Definition of Done — Codebase Explorer

> Status: **binding** · Last reviewed: 2026-11-17

A task from [TASK_BREAKDOWN.md](TASK_BREAKDOWN.md) is **done** only when every applicable item
below is true. Reviewers (human or AI) check this list on every pull request.

## 1. Scope

- [ ] All acceptance criteria of the task are met — demonstrably (test, screenshot or documented manual check).
- [ ] Nothing outside the task's scope was added (no "while I was at it" features).
- [ ] No fake implementations: no hard-coded or simulated analysis results, no placeholder logic
      pretending to work. Unimplemented paths fail explicitly (`NotSupportedException` with a task reference) or are not reachable from the UI.

## 2. Architecture

- [ ] Dependency rules respected (SYSTEM_ARCHITECTURE.md §3); architecture tests green.
- [ ] Domain has no infrastructure dependencies; EF entities do not leave Storage; graph uses Contracts DTOs.
- [ ] No business logic in Avalonia views or code-behind; ViewModels use Application ports/use cases.
- [ ] New public types are deliberate; module implementations stay `internal`.
- [ ] Any architectural decision is captured in a new or updated ADR.

## 3. Code quality

- [ ] Follows [CODING_STANDARDS.md](../development/CODING_STANDARDS.md).
- [ ] Async I/O with `CancellationToken` passed through; long-running work reports progress.
- [ ] Nullable warnings resolved properly (no blanket `!` suppressions without justification).
- [ ] No new package without a verified version in `Directory.Packages.props` and an entry in
      SOLUTION_STRUCTURE.md §6.
- [ ] Security rules (SECURITY.md) respected: no process execution, safe parsing, containment, no secrets in logs.

## 4. Tests

- [ ] New behavior covered by tests at the right level (TEST_STRATEGY.md §5).
- [ ] Negative and cancellation paths tested where applicable.
- [ ] `dotnet build CodebaseExplorer.slnx` → 0 warnings, 0 errors.
- [ ] `dotnet test --solution CodebaseExplorer.slnx` → all tests pass.
- [ ] UI changes: app started and the change verified manually (note OS in the PR); for graph/WebView changes on each OS available.

## 5. Documentation

- [ ] Affected docs updated in the same change (architecture docs, schema, analysis engine, README status).
- [ ] Task status updated in TASK_BREAKDOWN.md (and the status overview table); milestone status updated if completed.
- [ ] Open decisions touched by the task are updated (resolved, or new findings recorded).
- [ ] Public APIs/ports have XML doc comments explaining intent and contracts (review-enforced; CS1591 is suppressed in the build).

## 6. Delivery

- [ ] Commits follow [GIT_WORKFLOW.md](../development/GIT_WORKFLOW.md) (branch, message format, task ID).
- [ ] PR description lists: task ID, summary, tests run, manual checks, docs updated, follow-ups.
