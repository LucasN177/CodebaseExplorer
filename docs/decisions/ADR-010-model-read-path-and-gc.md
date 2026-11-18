# ADR-010: Whole-Model Reads in M2 and Memory-Conserving GC

- **Status:** Accepted (read path chosen by the user during STORE-004; ADR accepted by the user on 2026-11-18)
- **Date:** 2026-11-18

## Context

SYSTEM_ARCHITECTURE.md §6 states that the model is read in **slices** (by level, focus, filters) so the
full model of a monorepo never has to be loaded. STORE-004 introduced the persistent
`IArchitectureModelStore`. The graph projector (`IGraphProjector.Project(ArchitectureModel, …)`, GRAPH-002)
and the edge details (`DescribeEdge`, GRAPH-010) operate on a complete `ArchitectureModel`. Real slice reads
need SQL aggregation over the denormalized container ids and a slice-shaped projector API — a change of the
Graph module contract (GRAPH_ENGINE.md §3).

The STORE-004 benchmark (save 1,000,000 synthetic relationships) showed that the model itself is ~914 MB of
live memory, the store adds ~18 MB live, but with the default GC the process peaked at 1,951 MB because
batch garbage promoted across awaits is collected late (target: < 1.5 GB).

## Decision

1. **Read path (M2):** the Application layer loads the whole current model from the store; the store keeps
   the most recently loaded model per run id, so graph and edge-detail requests do not re-read it. The UI
   still only receives projected DTOs. SQL slice queries are introduced together with the node budget and
   benchmarks in M4 (GRAPH-008, QA-002), which also decide the slice-shaped projector API.
2. **GC configuration:** the Desktop app sets `System.GC.ConserveMemory=9`
   (`RuntimeHostConfigurationOption` in the Desktop csproj). Measured on the benchmark: peak 1,331 MB
   (level 7: 1,505 MB; level 5: 1,762 MB; default: 1,951 MB) at ~20 % longer save time.

## Alternatives

| Alternative | Why not |
|---|---|
| L1 slice via SQL now | Changes the Graph module contract before the node budget and performance fixtures exist (M4) to validate the design; more scope than STORE-004 needed. |
| Keep the M1 session cache next to the store | Two sources of truth; models would still be lost on restart for the read path. |
| Forced `GC.Collect` in the store after large saves | Hides the policy in code, unpredictable pauses, and still leaves the default GC behavior for the rest of the app. |
| `ConserveMemory=7` | Passes the benchmark with only ~30 MB headroom; results vary between runs. |
| No GC change, accept the failed criterion | The process would exceed the memory target for large repositories. |

## Consequences

**Positive**
- Simple, correct read path today; models survive restarts; projector unchanged.
- The memory target holds with headroom; the store's own memory overhead is shown to be small.

**Negative**
- Large repositories load their whole model into memory (~1 KB per relationship in the synthetic benchmark)
  until slices exist (M4).
- More GC work: ~20 % longer saves of very large models; normal-sized repositories are unaffected in practice.

**Follow-ups**
- M4 (GRAPH-008, QA-002): slice queries and projector API; re-measure with and without `ConserveMemory`.
- Domain memory per relationship is dominated by natural-key strings; consider a leaner representation if
  QA-002 shows the model itself is the limit (DOMAIN_MODEL.md "streaming persistence is a scaling task").
