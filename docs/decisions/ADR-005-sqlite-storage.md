# ADR-005: SQLite with EF Core for Local Storage

- **Status:** Accepted
- **Date:** 2026-11-17

## Context

Analysis results must persist locally (local-first) so repositories can be reopened without
re-analysis, runs can be compared, and AI retrieval can search content. Data volumes range from a
few hundred rows to millions (relationships and evidence in enterprise monorepos). The app is
single-user; the database is embedded and must work on all three OSes without a server.
Schema evolution over many releases is expected.

## Decision

Use **SQLite** as embedded database with **Entity Framework Core** (SQLite provider) and
**EF Core migrations**.

- Package line: EF Core **10.0.x** (10.0.15 seen on nuget.org 2026-11-17) because EF Core 11
  targets net11.0 only. Introduced in STORE-001.
- One database file per user in the app data directory; WAL mode; foreign keys on.
- Persistence entities are separate from Domain classes and never leave `Storage`.
- Deterministic IDs stored as 16-byte BLOBs; enums as strings.
- Bulk write paths may use raw `SqliteCommand` batches inside Storage if EF batching is measured
  to be insufficient (QA-002) — still behind the same ports.
- FTS5 virtual table for lexical AI retrieval, subject to verification (AI-003).

## Alternatives

| Alternative | Why not |
|---|---|
| LiteDB / other document DB | Weaker relational querying for aggregation (GROUP BY across millions of edges); smaller ecosystem. |
| Graph database (embedded Neo4j-like) | No mature embedded cross-platform option for .NET; aggregation queries are simple enough for SQL. |
| DuckDB | Excellent analytics, but less mature EF/.NET integration and write-heavy transactional patterns; possible future read-side accelerator. |
| JSON files per run | No queries, no partial loading; poor at scale. |
| Dapper only | Fine for performance, but migrations and mapping would be hand-written; EF migrations are valuable for long-term schema evolution. |

## Consequences

**Positive**
- Zero-admin, cross-platform, transactional storage; mature tooling; migrations.

**Negative**
- EF Core overhead on bulk inserts (mitigated as described).
- SQLite single-writer model — acceptable for a single-user app; analysis writes are batched.
- Polymorphic relationship endpoints cannot have FKs; integrity is ensured by Domain validation and tests.
