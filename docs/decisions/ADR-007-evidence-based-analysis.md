# ADR-007: Evidence-Based Analysis

- **Status:** Accepted
- **Date:** 2026-11-17

## Context

Users make decisions (refactorings, onboarding, audits) based on the dependencies Codebase
Explorer shows. Static analysis without builds is necessarily imperfect (ADR-004): some
relationships are resolved by a compiler-grade semantic model, others only by syntax or naming
conventions. If the product presents all edges as equally certain, users will either over-trust
wrong edges or distrust everything. AI features additionally need verifiable anchors to avoid
hallucinated answers.

## Decision

Every `CodeRelationship` must have **at least one `AnalysisEvidence`** record containing:
analyzer id and version, a machine-readable **rule id**, an **evidence level** and — when derived
from a file — a **source location** (file + line/column range) and short excerpt.

Evidence levels (ordered):

1. **Heuristic** — inferred from patterns or conventions; may be wrong.
2. **Syntactic** — found in syntax/manifest; target resolved by name/path rules only.
3. **Confirmed** — resolved by a semantic model or an authoritative declaration whose target exists.

A relationship's level is the maximum of its evidence levels (derived, not assigned). The UI shows
levels (edge style, badges), allows filtering by minimum level, and the inspector lists evidence.
Analyzers may not emit relationships without evidence; the Domain model rejects them.

## Alternatives

| Alternative | Why not |
|---|---|
| Binary "found/not found" relationships | Hides uncertainty; misleads. |
| Numeric confidence scores (0–1) | False precision; hard to define consistently across analyzers; three levels are explainable. |
| Evidence only for heuristic edges | Inconsistent; confirmed edges also need navigation to source for usability and AI citations. |

## Consequences

**Positive**
- Trustworthy, explainable results; strong differentiator.
- Natural citation anchors for AI answers.
- Enables analyzer quality metrics (share of confirmed vs. heuristic).

**Negative**
- More storage (evidence rows can outnumber relationships); mitigated by excerpt length limits,
  indexing, and optional evidence sampling for very high-occurrence relationships (decided in
  STORE-002 if needed, with counts preserved in `Occurrences`).
- Analyzer implementations must carry locations through all stages.
