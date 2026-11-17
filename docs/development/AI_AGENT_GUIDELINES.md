# AI Agent Guidelines — Codebase Explorer

> Status: **binding** for all AI coding agents (Claude Code and others) · Last reviewed: 2026-11-17
> The short, mandatory version lives in [`CLAUDE.md`](../../CLAUDE.md). This document explains the
> rules and gives session templates.

## 1. Why these rules exist

This project is developed over months, largely in AI-assisted sessions that each see only part of
the codebase. The main risks are **architecture erosion**, **scope creep**, **plausible-looking
fake results**, and **documentation drift**. The rules below counter exactly those risks.

## 2. Session lifecycle

### 2.1 Start of session

1. Read `CLAUDE.md`.
2. Identify the task ID. If the request has no task ID, map it to an existing task or ask whether
   a new task should be added to TASK_BREAKDOWN.md.
3. Read: the task entry, its prerequisites' status, the architecture doc(s) of affected modules,
   related ADRs, and the existing code in the affected folders.
4. Produce a short plan (files, steps, tests, docs). For tasks larger than a few files, present the
   plan before implementing.

### 2.2 During the session

- Implement in small steps; build after each meaningful step.
- Keep within the task's scope and affected projects. If you discover necessary work outside the
  scope, **note it** as a follow-up (PR description / new task proposal) instead of doing it.
- If reality contradicts the documentation (e.g. a package API differs, a spike assumption is
  wrong), stop and report; propose a doc/ADR update rather than silently diverging.

### 2.3 End of session

1. `dotnet build CodebaseExplorer.slnx` — 0 warnings, 0 errors.
2. `dotnet test --solution CodebaseExplorer.slnx` — all green (or, during long tasks, at least the
   affected test projects, with the full run before completion).
3. Update docs and task status.
4. Summarize: what was done, what was verified and how, what is not done, follow-ups.

## 3. Hard rules (non-negotiable)

| Rule | Detail |
|---|---|
| **No fake implementations** | Never return hard-coded, random or simulated analysis results, graphs, metrics, Git data or AI answers to make something "look working". Use test doubles only in tests. |
| **No silent architecture changes** | Dependency rules, module responsibilities and ADR decisions change only via an ADR, with explicit user approval. |
| **No unrequested features** | Implement the task, not adjacent ideas. |
| **No unverified versions** | Package versions must be verified (DEVELOPMENT_GUIDE.md §4). Never guess. |
| **No repository code execution** | Never add code that builds, restores, runs scripts of, or evaluates MSBuild for analyzed repositories. |
| **No AI write access** | AI features never modify repositories or the model. |
| **Honest reporting** | Report failing tests, skipped steps and unverified assumptions explicitly. Do not claim a feature works unless it was run/tested. |
| **Docs follow code** | A change that makes a doc wrong must update that doc. README must never describe planned features as available. |

## 4. When unsure

1. Look for the answer in the architecture docs and existing code first.
2. Prefer the existing pattern in the codebase over introducing a new one.
3. If two documents disagree, flag the contradiction and ask; do not pick one silently.
4. If a decision is genuinely open (see "Open product decisions" in FEATURE_ROADMAP.md and
   "open verification items" in architecture docs), do not resolve it implicitly in code.

## 5. Prompt templates for future sessions

### Implement a task

```text
Implement task <TASK-ID> from docs/implementation/TASK_BREAKDOWN.md.
Follow CLAUDE.md and the feature workflow in docs/development/DEVELOPMENT_GUIDE.md.
First read the relevant docs and code and show me your plan. Then implement in small steps,
write tests, build the solution, run the tests, update docs and the task status.
```

### Run a spike

```text
Run spike <TASK-ID>. Time-box: <n> days of effort. Produce verified facts only, minimal proof code
on a spike branch, and update the referenced ADR/architecture doc with results, open issues and a
recommendation. Do not add production code.
```

### Review a change

```text
Review the changes on this branch against docs/implementation/DEFINITION_OF_DONE.md,
the dependency rules in docs/architecture/SYSTEM_ARCHITECTURE.md §3 and docs/architecture/SECURITY.md.
List violations with file:line and a suggested fix.
```

### Propose an architecture change

```text
I want to change <X>. Do not implement it. Write a draft ADR in docs/decisions/ (next number,
status Proposed) with context, decision, alternatives and consequences, and list all docs and tests
that would need to change.
```

## 6. Checklist for agents (copy into the PR)

- [ ] Task ID referenced; scope respected
- [ ] Plan written before implementation
- [ ] Dependency rules respected; architecture tests green
- [ ] No fake results; no repository code execution
- [ ] Tests added; full build and test run green
- [ ] Docs + task status updated
- [ ] Follow-ups listed instead of implemented
