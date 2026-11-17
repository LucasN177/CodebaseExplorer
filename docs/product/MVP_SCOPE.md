# MVP Scope — Codebase Explorer

> Status: **accepted** · Last reviewed: 2026-11-17
> Implementation status: **foundation only** — none of the MVP features below is implemented yet.

This document is the binding scope boundary for the first releasable version. A feature that is not
listed under "In scope" is out of scope for the MVP, even if it appears in the roadmap. Changing this
scope requires an explicit product decision recorded here (section 6).

## 1. MVP goal

> A developer can import a local or remote (GitHub/GitLab) repository containing C#, TypeScript,
> JavaScript and/or Python code, and explore its architecture in an interactive, hierarchical graph
> in which every relationship is backed by evidence — fully offline, without executing any
> repository code.

## 2. In scope

| ID | Capability | Included in MVP | Explicitly *not* included in MVP |
|---|---|---|---|
| MVP-01 | **Repository import** | Local folder; HTTPS clone of GitHub/GitLab URLs (public, and private via personal access token); list of recent repositories; re-analysis on demand. | SSH clone (open decision, see GIT_INTEGRATION.md); Bitbucket/Azure DevOps-specific integration (generic HTTPS Git URLs may work but are not tested); OAuth login flows; API-based browsing without clone. |
| MVP-02 | **Repository scanner** | File enumeration honoring `.gitignore` and built-in excludes (`bin`, `obj`, `node_modules`, `.git`, virtualenvs …); language detection by extension and markers; file classification; content hashing; size limits; binary detection. | Content-based language detection for extension-less files beyond shebang lines. |
| MVP-03 | **Project & language detection** | .NET: `.sln`, `.slnx`, `.csproj` (incl. SDK-style properties, `Directory.Build.props`, `Directory.Packages.props` static resolution). Node: `package.json`, npm/yarn/pnpm workspaces, `tsconfig.json`. Python: `pyproject.toml`, `requirements*.txt`, `setup.cfg`, package folders. | `.vbproj`/`.fsproj` analysis beyond detection; Bazel, Nx/Turborepo-specific graphs, Gradle/Maven; `setup.py` evaluation (never executed; only detected). |
| MVP-04 | **C# analysis** | Roslyn syntax + semantic analysis *without build*: namespaces, types (class, interface, struct, record, enum, delegate), members at signature level; inheritance, interface implementation, type usage, object creation, method invocation between types; external package references. | Running analyzers/source generators from the analyzed repo; MSBuild design-time builds; generated-code fidelity (source generator output is not visible); call graphs at statement precision; data-flow analysis. |
| MVP-05 | **Basic TS/JS/Python analysis** | Tree-sitter parsing (pending spike ANALYSIS-008): modules/files, `import`/`require`/`export` (TS/JS), `import`/`from … import` (Python); top-level classes and functions; relative and package import resolution; package dependencies from manifests. | Type checking; resolution of path aliases beyond `tsconfig` `paths`/`baseUrl`; dynamic imports with computed specifiers; Python namespace-package edge cases; framework-specific semantics (Angular DI, Django apps) — later heuristics. |
| MVP-06 | **Project dependencies** | Project→project and project→external package relationships for all supported ecosystems, with evidence (manifest file + line). | Transitive package resolution from lock files (post-MVP; lock files are only detected). |
| MVP-07 | **Common architecture model** | Language-independent domain model (DOMAIN_MODEL.md) with stable IDs and evidence levels (confirmed/syntactic/heuristic). | Cross-language links (e.g. HTTP client in TS → ASP.NET controller) — post-MVP heuristic analyzers. |
| MVP-08 | **SQLite persistence** | Local database per user; EF Core migrations; analysis runs; latest model per repository; re-open without re-analysis. | Multi-user/shared databases; storing full model history per run (only run metadata + latest model, see ADR-009). |
| MVP-09 | **Architecture graph** | Cytoscape.js in Avalonia WebView; levels repository → project → module → entity; expand/collapse; aggregated edges with counts; layouts; filters (language, relationship kind, evidence level); search and focus; neighborhood (n-hop) view; node budget for large graphs. | 3D views; custom user-drawn diagrams; collaborative annotations. |
| MVP-10 | **Component Inspector** | Details for the selected node/edge: kind, location, metrics (fan-in/fan-out, size), incoming/outgoing relationships, evidence list with file/line, Git activity; "copy path"; "open in external editor" if configured. | Inline source editing; full source viewer with semantic navigation (a read-only snippet view is in scope). |
| MVP-11 | **Git metadata** | Detect Git repositories; current branch, HEAD commit, remotes; per-file last change, commit count and distinct author count (bounded history depth); activity aggregated to projects/modules. | Architecture comparison across commits (post-MVP); blame per line; PR/MR integration. |
| MVP-12 | **Basic documentation export** | Markdown report: repository overview, languages, projects, project dependency graph as Mermaid, external dependencies, analysis metadata and evidence statistics. | PDF/HTML export, Confluence/wiki publishing, C4-model generation. |
| MVP-13 | **Basic local AI (optional)** | Disabled by default. Ollama provider via `Microsoft.Extensions.AI`; repository Q&A and "explain this component" grounded in analysis facts and retrieved source snippets; mandatory citations (file + line range) validated against the database; conversations stored locally. | Cloud providers (post-MVP, architecture prepared); embeddings/vector search (lexical retrieval first); agents/tools that modify anything; automatic code changes. |

### Cross-cutting MVP requirements

- **Platforms:** Windows 11 x64, macOS (Apple Silicon), Linux x64 (Ubuntu LTS as reference). Exact
  minimum OS versions depend on Avalonia/WebView support and are fixed in task QA-004.
- **No code execution:** No build, restore, script, MSBuild target, package hook, or repository
  analyzer/source generator is ever executed (SECURITY.md).
- **No automatic code changes:** Neither the analysis nor the AI writes to the analyzed repository.
  Writes happen only in the app's data directory (including clones of remotes); the sole exception
  is a documentation export into a folder the user explicitly chooses and confirms (SECURITY.md S5).
- **Cancellation & progress:** Every long-running operation reports progress and can be cancelled.
- **Themes:** Light and dark mode (Fluent theme). The graph follows the theme.
- **Accessibility baseline:** Keyboard navigation for the shell, inspector and graph selection;
  sufficient contrast in both themes.

## 3. Later extensions (post-MVP, prioritized)

1. Incremental analysis (re-analyze changed files only) — long-term core goal (ADR-009).
2. Architecture history: snapshot comparison between commits/branches; architecture drift.
3. Impact analysis: transitive "what depends on this" with path explanation.
4. Coupling & cycle analysis: cycles, instability metrics, change coupling from Git co-changes.
5. Cross-language heuristics: HTTP endpoints ↔ clients, message contracts, shared DTOs.
6. Cloud AI providers (opt-in) and embedding-based retrieval.
7. Additional languages: Java, Kotlin, Go, Rust, PHP (Tree-sitter analyzers), F#/VB project depth.
8. Architecture rules ("layer X must not depend on Y") as exploration overlays.
9. Lock-file based transitive dependency resolution and vulnerability/license overlays.
10. Richer exports (HTML, C4/Structurizr DSL, PlantUML).
11. Opt-in "trusted mode" design-time builds for higher C# fidelity (requires its own ADR and explicit
    per-repository consent; conflicts with the zero-execution default and is therefore undecided).

## 4. Explicitly excluded (not planned)

- Executing, building, restoring or testing analyzed repositories (by default and in the MVP).
- Automatic or AI-driven modification of repository code; AI agents with write tools.
- Code editing inside Codebase Explorer.
- A server/SaaS component, user accounts, telemetry that includes repository content.
- Uploading repository content anywhere without explicit configuration of a cloud AI provider.
- Microservice architecture for the product itself (ADR-001).

## 5. MVP acceptance criteria (release gate)

The MVP is releasable when all of the following hold:

1. All MVP-01 … MVP-13 capabilities meet the acceptance criteria of their tasks in
   [TASK_BREAKDOWN.md](../implementation/TASK_BREAKDOWN.md).
2. The fixture corpus (TEST_STRATEGY.md) passes on Windows, macOS and Linux in CI.
3. Hostile fixtures prove that no repository code is executed.
4. A reference repository with ≥ 2,000 projects/packages can be analyzed and explored without the
   UI freezing (UI thread never blocked > 200 ms by analysis work) and without exceeding 4 GB RAM.
5. Every relationship displayed has ≥ 1 evidence record; the inspector shows it.
6. With AI disabled (default), no network connection is opened except for explicit remote clones.
7. Documentation (README, architecture docs) reflects the implemented state.

## 6. Scope change log

| Date | Change | Decided by |
|---|---|---|
| 2026-11-17 | Initial MVP scope. | Project initialization |
