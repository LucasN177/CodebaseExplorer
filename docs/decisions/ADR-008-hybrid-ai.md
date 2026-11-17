# ADR-008: Hybrid, Local-First AI via Microsoft.Extensions.AI

- **Status:** Accepted
- **Date:** 2026-11-17

## Context

Natural-language explanations and repository Q&A help users understand code faster. However:
repositories are often confidential (local-first requirement), AI output can be wrong, and the
product positions deterministic analysis as the source of truth. Users differ: some have Ollama
running locally, some want to use cloud models, many want no AI at all.

## Decision

- AI is an **optional module**, **disabled by default**.
- Provider abstraction: **`Microsoft.Extensions.AI` (`IChatClient`)**. Planned packages:
  `Microsoft.Extensions.AI` (10.x line; 10.11.0 seen 2026-11-17) and `OllamaSharp` (5.5.2) for the
  **Ollama** provider (MVP). Versions re-verified in AI-001/AI-002.
- **Cloud providers are post-MVP** and require: explicit provider configuration, API key in secure
  storage (OPD-03), **per-repository consent**, a visible indicator, and a context manifest the user
  can inspect. Non-loopback "local" endpoints are treated as remote.
- **Retrieval-based grounding:** answers are generated from retrieved analysis facts and source
  snippets (lexical FTS5 retrieval first; embeddings post-MVP).
- **Mandatory, validated citations** (file + line range / element id).
- **No side effects:** no tools, no code modification, no writing to repositories.

## Alternatives

| Alternative | Why not |
|---|---|
| Cloud-only AI | Violates local-first; blocks confidential use. |
| Local-only AI forever | Excludes users who explicitly want stronger cloud models; abstraction makes opt-in cheap. |
| Provider-specific SDKs directly | Couples the AI module to vendors; `Microsoft.Extensions.AI` gives a standard .NET abstraction. |
| Semantic Kernel | Broader orchestration framework than needed; MEAI is the lower-level abstraction we need. Can be reconsidered if orchestration needs grow. |
| AI-generated architecture model | Non-deterministic and unverifiable; contradicts ADR-007. |

## Consequences

**Positive**
- Privacy by default; flexible provider choice; consistent .NET abstraction.
- Answers are verifiable through citations.

**Negative**
- Local model quality varies; answers may be weaker on small models — mitigated by facts-first context.
- Retrieval and citation validation are non-trivial engineering work.
- Additional settings/consent UX.
