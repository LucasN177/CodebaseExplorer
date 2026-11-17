# ADR-003: Language-Independent Domain Model

- **Status:** Accepted
- **Date:** 2026-11-17

## Context

Codebase Explorer must support C#, TypeScript, JavaScript and Python from the start and further
languages later. Repositories are often polyglot. Consumers of analysis results — graph,
inspector, export, AI retrieval, Git overlays, future incremental analysis — should not need
per-language code paths. At the same time, each language has different concepts (namespaces vs.
ES modules vs. Python packages; classes vs. functions; NuGet vs. npm vs. PyPI).

## Decision

Define one **language-independent architecture model** in `CodebaseExplorer.Domain`:
`Repository`, `CodeProject`, `CodeModule`, `RepositoryFile`, `CodeEntity`, `CodeRelationship`,
`AnalysisEvidence`, `ExternalDependency`, `AnalysisRun` (DOMAIN_MODEL.md).

- Generic kinds (`ProjectKind`, `ModuleKind`, `EntityKind`, `RelationshipKind`) cover concepts
  across languages; language specifics are expressed as data (`LanguageId`, `Ecosystem`,
  property bags), not as subclasses per language.
- `LanguageId` and `AnalyzerId` are extensible string value objects, not enums, so new languages
  need no Domain change.
- All analyzers emit fragments of this model; nothing downstream depends on Roslyn or Tree-sitter
  types.
- Identity is deterministic from natural keys (stable IDs), with per-language symbol key formats.

## Alternatives

| Alternative | Why not |
|---|---|
| Per-language models with adapters for the UI | Every consumer would need N code paths; polyglot graphs become hard. |
| Generic property-graph (nodes/edges with arbitrary labels and properties) | Maximal flexibility but no invariants, weak typing, hard to evolve safely; queries become stringly typed. |
| Reuse an existing standard (e.g. LSIF/SCIP index format) | Designed for code navigation at symbol/occurrence level, not architecture aggregation with evidence levels; could be an *input* format later. |

## Consequences

**Positive**
- Graph, inspector, export and AI work for all languages at once.
- New languages are additive (new analyzer + fixtures).
- Stable IDs enable incremental analysis and history comparison.

**Negative**
- Some language-specific nuance is flattened (e.g. TS type-only imports, Python dynamic features);
  mitigated by attributes and rule ids in evidence.
- Kind enums must be curated carefully; values are append-only.
