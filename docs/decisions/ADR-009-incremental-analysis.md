# ADR-009: Incremental Analysis as a Long-Term Core Goal

- **Status:** Accepted (design prerequisites binding now; implementation post-MVP)
- **Date:** 2026-11-17

## Context

Full analysis of an enterprise monorepo can take minutes. Users re-analyze after pulling changes,
and history features (architecture over time) require analyzing many commits. Without
incrementality these workflows do not scale. Retrofitting incrementality into an engine that was
not designed for it is expensive: it needs stable identities, change detection and well-defined
units of re-analysis.

## Decision

Incremental analysis is **not part of the MVP**, but the MVP design **must** satisfy these
prerequisites:

1. **Stable, deterministic IDs** from natural keys for all model elements (DOMAIN_MODEL.md §3).
2. **Content hashes** (SHA-256) for every scanned file, persisted.
3. **Per-project analyzer fragments** as the unit of language analysis; analyzers have no hidden
   global state.
4. **Deterministic output** for identical input and analyzer versions.
5. `LastSeenRunId` on persisted model rows and analyzer versions on `AnalysisRuns`.
6. Linking is a separate stage that can re-run over a mix of reused and new fragments.

The post-MVP algorithm (ANALYSIS_ENGINE.md §8): diff file hashes → affected projects → reverse
dependency closure for semantic re-analysis → re-run manifests if changed → re-link → validate →
persist delta.

Architecture **history** storage (full snapshots vs. deltas) remains open (OPD-07).

## Alternatives

| Alternative | Why not |
|---|---|
| Implement incremental analysis in the MVP | Delays the vertical slice; correctness risk before the base engine is proven. |
| Ignore incrementality until needed | Retrofitting stable IDs and fragment boundaries later would require rewriting analyzers and storage. |
| File-level incrementality only (no project closure) | Incorrect for semantic C# relationships when referenced projects change. |

## Consequences

**Positive**
- MVP stays focused while keeping the path to fast re-analysis and history open.
- Stable IDs also benefit UI state, AI citations and exports.

**Negative**
- Slight upfront complexity (natural keys, hashing, determinism tests).
- Renames produce new IDs; rename detection is future work.
