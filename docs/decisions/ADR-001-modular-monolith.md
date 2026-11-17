# ADR-001: Modular Monolith with Clean Architecture

- **Status:** Accepted
- **Date:** 2026-11-17
- **Deciders:** Project initialization (product & architecture)

## Context

Codebase Explorer is a single-user desktop application. It combines several technically distinct
capabilities — static analysis (Roslyn, Tree-sitter), persistence (SQLite), Git access, graph
projection, optional AI, and an Avalonia UI — that evolve at different speeds and pull in heavy,
specialized dependencies. The codebase will be developed over months, largely through AI coding
agent sessions, which makes clear and *mechanically enforced* boundaries especially valuable:
agents work on small slices and must not erode the structure.

There is no server, no multi-tenant requirement, and no independent deployment need for parts of
the system.

## Decision

Build a **modular monolith**: one process and one deployable application, divided into separate
assemblies with **Clean Architecture** dependency rules.

- `Domain` (innermost, no dependencies) → `Application` (use cases + ports) → technical modules
  (`Analysis`, `Graph`, `Git`, `AI`, `Storage`, `Infrastructure`) implement ports →
  `Desktop` (presentation + composition root).
- `Contracts` holds DTOs that cross technology boundaries; it has no dependencies.
- Technical modules never reference each other; they communicate through Application ports.
- Each module exposes one `DependencyInjection` registration method; implementations are internal.
- The rules are enforced by `ProjectReferenceRulesTests` (IntegrationTests).

## Alternatives

| Alternative | Why not |
|---|---|
| Single project with folders | Boundaries not enforceable; heavy dependencies (Roslyn, EF Core) leak everywhere; poor fit for agent-driven development. |
| Microservices / out-of-process services | No deployment or scaling driver; adds IPC, lifecycle and packaging complexity to a desktop app. Explicitly rejected by product constraints. |
| Vertical-slice-only architecture (feature folders, no layers) | Good for CRUD apps; here the analysis engine, model and graph are shared across all features, so horizontal technical modules with a shared domain fit better. Vertical slices are still used *inside* Application (feature folders). |
| Plugin architecture with dynamic loading | Premature; analyzer extensibility is achieved via DI registrations. Revisit for third-party analyzers (Horizon 3). |

## Consequences

**Positive**
- Strong, testable boundaries; heavy dependencies confined to one module each.
- Modules are replaceable (e.g. a second Git adapter) without touching others.
- Simple deployment and debugging (one process).

**Negative / costs**
- More projects and some mapping code (Domain ↔ persistence ↔ DTO).
- Cross-module workflows must be orchestrated in Application use cases, not by modules calling each other.
- Out-of-process isolation (e.g. for crash-prone native parsers) is not available by default; if
  needed, it will be introduced locally inside a module (ANALYSIS-008) without changing the style.

**Follow-ups**
- Keep `ProjectReferenceRulesTests` in sync with SYSTEM_ARCHITECTURE.md §3.
