# Architecture Decision Records

ADRs document significant architectural decisions: context, decision, alternatives, consequences
and status. They are immutable once accepted — a changed decision gets a **new** ADR that
supersedes the old one (the old one's status is set to `Superseded by ADR-NNN`).

**Exception: spike amendments.** An ADR accepted *conditionally on a spike* (ADR-004, ADR-006) may be
amended with a dated "Spike result" section and a status change (e.g. to plain `Accepted`). If the
spike changes the decision itself, a new superseding ADR is written instead.

| ADR | Title | Status |
|---|---|---|
| [ADR-001](ADR-001-modular-monolith.md) | Modular Monolith with Clean Architecture | Accepted |
| [ADR-002](ADR-002-avalonia-ui.md) | Avalonia UI with MVVM (CommunityToolkit.Mvvm) | Accepted |
| [ADR-003](ADR-003-language-independent-domain-model.md) | Language-Independent Domain Model | Accepted |
| [ADR-004](ADR-004-static-analysis-strategy.md) | Static Analysis Strategy (Roslyn without MSBuild, Tree-sitter, manifest parsers) | Accepted (Tree-sitter binding confirmed by ANALYSIS-008; reference-assembly source superseded by ADR-011) |
| [ADR-005](ADR-005-sqlite-storage.md) | SQLite with EF Core for Local Storage | Accepted |
| [ADR-006](ADR-006-cytoscape-visualization.md) | Cytoscape.js in an Avalonia WebView | Accepted for Windows (GRAPH-001); macOS/Linux conditional |
| [ADR-007](ADR-007-evidence-based-analysis.md) | Evidence-Based Analysis | Accepted |
| [ADR-008](ADR-008-hybrid-ai.md) | Hybrid, Local-First AI via Microsoft.Extensions.AI | Accepted |
| [ADR-009](ADR-009-incremental-analysis.md) | Incremental Analysis as a Long-Term Core Goal | Accepted |
| [ADR-010](ADR-010-model-read-path-and-gc.md) | Whole-Model Reads in M2 and Memory-Conserving GC | Accepted |
| [ADR-011](ADR-011-embedded-reference-assemblies.md) | Embedded Reference Assemblies for C# Compilations | Accepted |
| [ADR-012](ADR-012-whole-model-reads-after-benchmarks.md) | Keep Whole-Model Reads for the MVP after the M4 Benchmarks | Proposed |

## Template

```markdown
# ADR-NNN: <Title>

- **Status:** Proposed | Accepted | Superseded by ADR-NNN | Rejected
- **Date:** YYYY-MM-DD

## Context
<forces, constraints, problem>

## Decision
<what we do>

## Alternatives
| Alternative | Why not |

## Consequences
<positive, negative, follow-ups>
```

Statuses: `Proposed` (draft, needs user approval) → `Accepted` → optionally `Superseded`.
AI agents may draft ADRs only with status `Proposed`.
