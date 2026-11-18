# ADR-011: Embedded Reference Assemblies for C# Compilations

- **Status:** Accepted (decided by the user during ANALYSIS-005, 2026-11-18)
- **Date:** 2026-11-18
- **Supersedes:** the reference-assembly source in ADR-004, decision 2 ("reference assemblies of the target
  framework from the locally installed .NET SDK packs if present"). The rest of ADR-004 stays in force.

## Context

C# entities are identified by their Roslyn documentation comment ID (DOMAIN_MODEL.md §3.2), and the entity
id is a hash of that key. A documentation comment ID contains the parameter types of members, and Roslyn
formats a type differently depending on whether it can be resolved. Verified with
Microsoft.CodeAnalysis.CSharp 5.12.0 (2026-11-18):

| Declaration | Without metadata references | With .NET reference assemblies |
|---|---|---|
| `void A(Guid id)` (with `using System;`) | `M:N.C.A(Guid)` | `M:N.C.A(System.Guid)` |
| `void E(List<C<int>> y)` | `M:N.C.E(List{N.C{System.Int32}})` | `M:N.C.E(System.Collections.Generic.List{N.C{System.Int32}})` |
| `void D(this IServiceCollection s)` (NuGet type) | `M:N.C.D(IServiceCollection)` | `M:N.C.D(IServiceCollection)` |

With ADR-004's rule "SDK reference packs **if present**", the same repository would get different entity
ids on a machine with and without an installed .NET SDK (or with a different SDK version). That breaks the
stable-identity principle (DOMAIN_MODEL.md §1) and makes stored models, comparisons and tests
machine-dependent. The same applies to NuGet assemblies resolved "only if a restore happened".

## Decision

1. Every C# compilation references the **same embedded set of .NET 10 reference assemblies** from the
   package `Basic.Reference.Assemblies.Net100` (MIT; metadata-only reference assemblies shipped as
   resources and loaded as in-memory `MetadataReference`s — never executed, never read from the machine or
   the repository). The installed SDK is not used.
2. Compilations additionally reference the compilations of referenced in-repository projects
   (`CompilationReference`, topological order), as in ADR-004.
3. **NuGet package assemblies are never referenced**, also not when `obj/project.assets.json` and a local
   package cache exist. Package types stay unresolved in every environment, so their spelling in symbol keys
   is deterministic (`IServiceCollection` as written). ANALYSIS-006 therefore does not resolve NuGet types;
   relationships to types outside the repository are not created in M3 anyway (fixture `dotnet-advanced`
   EXPECTED.md §9).
4. The framework version of the embedded set is .NET 10 for every project, independent of its target
   framework(s). Symbol keys only need deterministic type names; framework APIs that differ between target
   frameworks may resolve imprecisely for older targets (documented limitation).

## Alternatives

| Alternative | Why not |
|---|---|
| SDK reference packs if present (ADR-004) | Entity ids depend on the machine (see table). |
| No metadata references at all for keys, a second compilation with SDK packs for semantics | Deterministic, but keys depend on how code is written (`Guid` vs. `System.Guid` give different ids for the same member), and every project is bound twice. |
| NuGet assemblies from the local cache via `project.assets.json` | Keys and evidence would depend on whether the user restored the repository; would also read assemblies produced from untrusted package sources (metadata only, but avoidable). |

## Consequences

**Positive**
- Entity ids are identical on every machine and in CI; no dependency on an installed .NET SDK.
- Framework types resolve to their full names, matching the documentation comment ID convention.

**Negative**
- A new package dependency (~6 MB of reference assemblies in the Analysis module's output).
- Projects targeting older frameworks are compiled against .NET 10 reference assemblies.
- NuGet types are never resolved, so the DI heuristics (ANALYSIS-007) recognize `IServiceCollection`
  by name.
- When the package line moves to a newer .NET version, framework type names stay the same, but the update
  must be checked against stored keys (a key change = new entity ids).
