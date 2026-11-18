# ADR-004: Static Analysis Strategy (Roslyn without MSBuild, Tree-sitter, Manifest Parsers)

- **Status:** Accepted; Tree-sitter binding confirmed by spike ANALYSIS-008 (2026-11-18, see "Spike result"); the reference-assembly source in decision 2 is superseded by [ADR-011](ADR-011-embedded-reference-assemblies.md)
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
   **Confirmed by spike ANALYSIS-008:** `TreeSitter.DotNet` 1.3.0 (MIT, netstandard2.0), used
   in-process with input limits (see "Spike result").
4. All results carry evidence with levels (ADR-007) reflecting how a relationship was resolved.

## Spike result (ANALYSIS-008, 2026-11-18)

Measured on Windows x64 (Release, .NET 10) with a throw-away console app. The proof code that stays is
`Analysis/Languages/TreeSitter` (`TreeSitterParser`, `SyntaxWalk`) with `TreeSitterParserTests`; these tests run in
CI on Windows, macOS and Linux and thereby verify the native binaries on all three systems.

| Question | Finding |
|---|---|
| Package | `TreeSitter.DotNet` 1.3.0 (MIT, Marius Greuel; published 2026-01-22; ~420k downloads; single maintainer, last push 2026-01). Managed assembly 64 KB, no dependencies. |
| Grammars | ~30 bundled, including **TypeScript, TSX, JavaScript (with JSX) and Python** (TS/TSX ABI 14, JS/Python ABI 15). The native runtime is Tree-sitter ≥ 0.25 (it exports `ts_parser_parse_with_options`). |
| Native binaries | win-x64/x86/arm64, osx-x64/arm64, linux-x64/x86/arm/arm64 — all target RIDs (win-x64, osx-arm64, linux-x64) are present. ~70 MB per RID with all grammars; the build keeps only the runtime and the four grammars for 64-bit Windows/macOS/Linux (~26 MB, `Directory.Build.targets`). |
| Speed | 1.9 MB of generated TypeScript parses in 0.44–0.48 s (TS/TSX), JavaScript 0.64 s, Python 0.43 s. A cursor walk visits ~1.7 M nodes/s; 64 parses of 50 KB on 8 threads sharing one grammar take 0.19 s; loading a grammar takes < 0.1 ms. |
| Malformed/huge input | Never crashed; always returned a tree with error nodes. Worst cases at ~1.9 MB: 1.9 M nested `(` 0.76 s; `{[(<` × 450k 3.3 s (Python grammar); an unterminated template literal 2.8 s (Python grammar); random characters 0.55 s. Error recovery flattens such nesting, but valid input can be deep, so trees are only walked iteratively. |
| Queries | **Not used.** The query engine is super-linear on hostile input (80 KB of nested brackets: 5.3 s for one query; at 1.9 MB it did not finish within 10 minutes). Extraction uses cursor walks with node-type checks. |
| Timeout/cancellation | The binding exposes **no** timeout, cancellation flag or progress callback, and its native handles (`Parser.Self`, the `Tree` constructor) are internal, so `ts_parser_parse_with_options` cannot be used without reflection. A single parse cannot be interrupted; cancellation is checked between files and during walks (`SyntaxWalk`). |
| Positions | Rows 0-based; columns 0-based **UTF-16 code units** (the binding parses UTF-16 strings), so they map directly to 1-based `SourceLocation` columns in characters. |
| Thread safety | Grammar (`Language`) objects are shared across threads; a `Parser` is created per parse. |

**Decisions**

1. Use `TreeSitter.DotNet` 1.3.0. The alternatives on nuget.org (`TreeSitter.Bindings`, `TreeSitterSharp`,
   `TreeSitter.TSBind`, `CycoDevTreeSitter`, `TreeSitterLanguagePack`) have far fewer users, fewer RIDs or no
   bundled grammars; an own P/Invoke layer would add timeouts but also native-memory and ABI risks that the
   measured parse times do not justify. Re-evaluate if the package stops being maintained or a grammar needs an
   update it does not ship.
2. **Crash containment: in-process with limits**, no parser host process. No crash was observed on any hostile
   input; input is bounded by `AnalysisLimits.MaxParseFileBytes` (2 MB; larger files are recorded but not parsed),
   which bounds a parse to a few seconds — the per-file 5 s target of ANALYSIS_ENGINE.md §5 is met by the size
   limit, not by a timer; every walk is iterative and cancellable; exceptions are isolated per file and project.
   A native crash would still end the process — accepted residual risk (SECURITY.md §2); an out-of-process
   parser host is reconsidered in the release readiness review (QA-006) if crashes are reported.
3. Tree-sitter queries are not used on repository content.

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
- Native Tree-sitter dependency adds packaging and robustness risks: mitigated by input limits, trimmed native
  assets and CI on all three operating systems (ANALYSIS-008); a native crash is not contained.
- No per-parse timeout: a pathological 2 MB file can occupy one worker for a few seconds.
