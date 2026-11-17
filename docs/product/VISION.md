# Product Vision — Codebase Explorer

> Status: **accepted** · Owner: product · Last reviewed: 2026-11-17

## Vision statement

**Codebase Explorer makes any software repository understandable in minutes instead of weeks.**

It is a cross-platform desktop application that analyzes existing repositories locally and turns
their structure, architecture, dependencies and history into an interactive, evidence-backed
architecture model that developers can navigate visually.

## The problem we solve

Developers spend a large part of their time *reading* code they did not write: onboarding into a
new team, taking over a legacy system, reviewing a monorepo they only touch occasionally, auditing
a dependency, or preparing a refactoring. The tools they use for this — IDEs, `grep`, file trees,
outdated wiki pages — are optimized for *editing files*, not for *understanding systems*.

The questions people actually ask when facing an unknown codebase are architectural:

- How is this repository organized? Which projects and modules exist?
- Which languages and frameworks are used, and where?
- How do the components depend on each other? Where are the boundaries?
- Which classes and services depend on which others?
- Which external packages does the system rely on?
- Which parts are tightly coupled, and what is affected if I change component X?
- How has the architecture evolved over the Git history?

Today these answers are reconstructed manually, inconsistently and repeatedly by every developer.

## What Codebase Explorer is

1. **A visual architecture explorer.** The central experience is an interactive graph that the
   user navigates hierarchically: repository → projects → modules → code entities. Large systems
   are aggregated; nobody has to look at a hairball of 50,000 nodes.
2. **A deterministic analysis engine.** Static analysis (Roslyn for C#, Tree-sitter and
   project-file analyzers for other languages) builds a language-independent architecture model.
   Every relationship carries *evidence*: the file and line range it was derived from, the analyzer
   that found it and how certain it is (confirmed, syntactic, heuristic).
3. **Repository intelligence over time.** Git metadata and history connect the structure with
   change activity: what changes often, what changes together, what changed recently.
4. **An optional AI companion.** Local models (Ollama) — and only on explicit request, cloud
   providers — can explain components and answer questions *grounded in the analysis results*,
   always citing files and line ranges. AI supports understanding; it never replaces the
   deterministic analysis and never modifies code.

## Guiding principles

| Principle | Meaning |
|---|---|
| **Visual-first** | Every feature must be reachable from, and pay off in, the architecture explorer. Text views support the graph, not the other way round. |
| **Evidence over assertion** | The product never shows a relationship it cannot trace back to a source location and an analyzer rule. Uncertainty is shown, not hidden. |
| **Local-first and private** | Repository contents are analyzed and stored on the user's machine. Nothing leaves it unless the user explicitly configures a cloud provider. |
| **Never execute repository code** | No builds, no restores, no scripts, no MSBuild evaluation of targets, no package lifecycle hooks. Repositories are untrusted input. |
| **Universal by design** | The domain model is language-independent. C#, TypeScript, JavaScript and Python come first; further languages are added as analyzers, not as special cases. |
| **Scales to enterprise monorepos** | Analysis and visualization are hierarchical and, in the long run, incremental. |
| **AI as an assistant, not an oracle** | AI answers are grounded in analysis facts and source citations, clearly labeled, and optional. |

## Target outcome (12-month horizon)

- A developer can import a local repository or a GitHub/GitLab URL and see an interactive project
  dependency graph within minutes, even for repositories with thousands of projects/packages.
- For C#, the graph can be drilled down to namespaces and types with confirmed relationships.
- For TypeScript, JavaScript and Python, package- and module-level dependencies are reliable.
- The user can export a Markdown/Mermaid architecture overview to share with the team.
- With Ollama installed, the user can ask "What does the `Billing` module do and who uses it?"
  and receive an answer with clickable file/line citations.

## Non-goals

- Codebase Explorer is **not an IDE** and will not offer code editing.
- It is **not a CI linting tool** or a replacement for SonarQube-style quality gates
  (architecture rules may come later, but as exploration aids).
- It is **not a cloud SaaS**; there is no server component in the product.
- It does **not run, build or test** the analyzed code.
- AI will **not change code automatically**.

## Related documents

- Product concept and differentiation: [PRODUCT_CONCEPT.md](PRODUCT_CONCEPT.md)
- MVP scope: [MVP_SCOPE.md](MVP_SCOPE.md)
- Roadmap: [FEATURE_ROADMAP.md](FEATURE_ROADMAP.md)
- User workflows: [USER_WORKFLOWS.md](USER_WORKFLOWS.md)
