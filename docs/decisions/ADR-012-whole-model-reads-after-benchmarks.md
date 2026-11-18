# ADR-012: Keep Whole-Model Reads for the MVP after the M4 Benchmarks

- **Status:** Accepted (drafted in QA-002; accepted by the user on 2026-11-18)
- **Date:** 2026-11-18
- **Related:** [ADR-010](ADR-010-model-read-path-and-gc.md) (whole-model reads in M2, slices deferred to M4),
  SYSTEM_ARCHITECTURE.md §6, GRAPH_ENGINE.md §3–4, TEST_STRATEGY.md §8

## Context

ADR-010 deferred SQL slice queries ("read the model in slices by level, focus and filters") to M4, where the node
budget (GRAPH-008) and the performance benchmarks (QA-002) would decide whether and how to introduce them. Both exist
now. The benchmarks import generated repositories through the real composition root (scan, all analyzers, SQLite)
and measure the first graph (TEST_STRATEGY.md §8, 2026-11-18, Release, Windows 11):

| Repository | Import | Load model (cold) + project | Projection (model cached) | Peak working set |
|---|---|---|---|---|
| 100 .NET projects, 602 files | 5.2 s | 0.37 s | 8 ms | 265 MB |
| 2,000 .NET projects, 12,002 files, 45,969 relationships | 74.2 s | 7.3 s | 120 ms | 783 MB |
| 250 npm packages, 50,502 TypeScript files, 106,979 relationships | 132.0 s | 16.0 s | 227 ms | 1,186 MB |

The MVP targets (PRODUCT_CONCEPT.md §8) are met: first graph < 30 s for 100 projects (5.6 s) and < 3 min for 2,000
projects (81.5 s). The node budget (GRAPH-008) already guarantees that the UI never receives more than 500 nodes. The
remaining cost of whole-model reads is the **cold load** after an import or an app start (7–16 s for the large
repositories) and memory proportional to the model.

## Decision

1. Keep the whole-model read path of ADR-010 for the MVP; do **not** introduce SQL slice queries and a slice-shaped
   projector API now.
2. Re-evaluate when one of these is observed: the cold load of a real repository exceeds 30 s, the process peak exceeds
   the 4 GB budget of ANALYSIS_ENGINE.md §7, or features need models of several runs at once (history comparison, M5+).

## Alternatives

| Alternative | Why not (now) |
|---|---|
| SQL slices per level/focus now | Large change of the Graph contract (projector on partial models, SQL aggregation of edges over denormalized container ids) for no target that is currently missed; projection itself is fast (≤ 0.23 s on the cached model). |
| Keep the model of the import in memory instead of reloading it after saving | Saves the cold load once per import but not after a restart; can be added independently if the load time becomes a problem. |

## Consequences

**Positive**
- No contract change; graph, inspector, search and edge details keep working on one consistent model.
- Targets met with headroom; simpler code.

**Negative**
- Cold loads of very large repositories take seconds (16 s for 50,000 TypeScript files) and memory grows with the
  model (1.2 GB peak for 107,000 relationships including the analysis).
- SYSTEM_ARCHITECTURE.md §6 keeps describing slices as the target architecture, not the current state.
