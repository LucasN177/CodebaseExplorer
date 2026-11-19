# ADR-013: Build the AI Search Index on First AI Use, Not During Analysis

- **Status:** Accepted (decided in AI-003, where TASK_BREAKDOWN.md left "chunking during analysis or post-step" open)
- **Date:** 2026-11-19
- **Related:** [ADR-008](ADR-008-hybrid-ai.md), [ADR-005](ADR-005-sqlite-storage.md), AI_ARCHITECTURE.md §2/§4,
  DATABASE_SCHEMA.md §4.18

## Context

AI retrieval needs lexical search over source code (FTS5, ADR-008). The chunks could be produced by the analysis
pipeline while it reads the files anyway, or in a separate step later. Facts that matter:

- AI is **off by default** (ADR-008); most imports will never be used for AI.
- Chunks copy source text into the application database. Without AI that copy has no purpose, makes the database
  much larger (roughly the size of the indexed source) and keeps repository content in a second place.
- The analysis pipeline has analyzers that emit the model; it has no notion of AI and should not get one
  (analyzers are independent and only emit the common model).
- Citations must match the analyzed version of a file (line numbers).

## Decision

- The index is built by the Application use case **`EnsureSearchIndex`** when AI is first used for a repository
  (question or explanation) and rebuilt when the repository's current run changes. `SearchIndexStates` records the run.
- Files are read through `IRepositoryFileReader` (containment, size limit). Only files allowed by `SecretFilePolicy`
  (source, test, manifest, configuration, documentation; no secret file names) are indexed. **Files whose content hash
  differs from the analyzed hash are skipped**, so every chunk matches the analyzed version.
- Chunks: 40 lines, starting every 30 lines; identifiers split into words in a separate `Keywords` column.
- `SearchChunks` is an FTS5 virtual table created by raw SQL in the migration `AddSearchIndex`; a trigger deletes the
  chunks of a deleted repository (virtual tables have no foreign keys).

## Alternatives

| Alternative | Why not |
|---|---|
| Chunk during analysis (pipeline post-step) | Copies source into the database for every import although AI is off by default; couples the analysis to an optional feature; slows every import. |
| Index in a separate database file | Two files to back up, migrate and compact; cascading deletes across files impossible. |
| No index, scan files at question time | Too slow for large repositories; no ranking. |

## Consequences

**Positive**
- No cost and no extra copy of source code unless the user enables and uses AI.
- The analysis module stays unaware of AI.
- Index and citations always match the analyzed file versions.

**Negative**
- The first question about a repository waits for indexing (proportional to repository size); the UI shows progress
  text, and later questions reuse the index.
- Files edited after the analysis are missing from the index until the repository is re-analyzed.
