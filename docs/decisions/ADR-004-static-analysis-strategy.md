# ADR-004: Static Analysis Strategy (Roslyn without MSBuild, Tree-sitter, Manifest Parsers)

- **Status:** Accepted (Tree-sitter binding choice pending spike ANALYSIS-008); the reference-assembly source in decision 2 is superseded by [ADR-011](ADR-011-embedded-reference-assemblies.md)
- **Date:** 2026-11-17

## Context

High-fidelity C# analysis is usually done by loading projects via `MSBuildWorkspace`, which
performs MSBuild evaluation and design-time builds. MSBuild evaluation of an untrusted repository
can **execute code** (property functions, imported `.props/.targets`, tasks, `Exec` during
design-time targets) and requires a restore (network access, package scripts in some ecosystems).
The product rules forbid executing repository code (SECURITY.md S2). Similarly, running `npm`,
`pip` or `setup.py` to discover dependencies is forbidden.

We still need: C# semantic relationships (inheritance, implementation, type usage, calls), module
imports for TS/JS/Python, and project/package dependencies for all ecosystems.

## Decision

1. **Project systems are analyzed by static manifest parsers** (XML/JSON/TOML/INI/text) owned by
   the Analysis module: `.sln`/`.slnx`/`.csproj` (+ static resolution of `Directory.Build.props`
   and `Directory.Packages.props`), `package.json`/workspaces/`tsconfig.json`,
   `pyproject.toml`/`requirements*.txt`/`setup.cfg`.
2. **C# is analyzed with Roslyn compiler APIs directly** (`CSharpSyntaxTree`, `CSharpCompilation`,
   semantic model) using metadata references from installed SDK reference packs, in-repository
   project compilations, and — only if already present — cached NuGet assemblies identified via
   existing `obj/project.assets.json`. **`MSBuildWorkspace` and `Microsoft.Build.Locator` are not
   used.** Analyzers and source generators are never loaded.
3. **TypeScript, JavaScript and Python are analyzed with Tree-sitter** grammars via a .NET binding.
   Candidate: `TreeSitter.DotNet` 1.3.0 (MIT, netstandard2.0). The binding is confirmed or replaced
   in spike **ANALYSIS-008** (bundled grammars, native binaries for win-x64/osx-arm64/linux-x64,
   robustness, cancellation).
4. All results carry evidence with levels (ADR-007) reflecting how a relationship was resolved.

## Alternatives

| Alternative | Why not |
|---|---|
| `MSBuildWorkspace` + design-time builds | Executes repository-defined MSBuild logic; needs restore; slow and fragile on large repos. Possibly offered later as explicit opt-in "trusted mode" (OPD-08) with its own ADR. |
| TypeScript compiler API via Node.js | Requires bundling Node and running JS tooling; heavier; type-checker depth not needed for the MVP. |
| Python `ast` module via embedded CPython | Requires a Python runtime; Tree-sitter covers syntax needs uniformly. |
| Regex-based import extraction | Too imprecise (comments, strings, multiline constructs); Tree-sitter gives real syntax trees. |
| SCIP/LSIF indexers per language | Require running language toolchains on the repository; possible future *import* path. |

## Consequences

**Positive**
- Zero-execution guarantee; works offline and without SDKs/restores (with reduced fidelity).
- Uniform Tree-sitter approach for non-C# languages; adding languages is cheaper.

**Negative**
- C# fidelity is lower than with full design-time builds: unresolved NuGet types, invisible
  source-generated code, approximated MSBuild conditions. Mitigated by explicit evidence levels and
  diagnostics.
- We maintain our own limited MSBuild property/condition resolver.
- Native Tree-sitter dependency adds packaging and robustness risks (spike required).
