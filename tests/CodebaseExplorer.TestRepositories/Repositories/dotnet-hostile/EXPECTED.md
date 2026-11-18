# Fixture `dotnet-hostile` — expected behavior (QA-003)

A repository whose build logic would run code if anything evaluated or built it. **Every payload only
writes a `MARKER-*.txt` file**; nothing in this fixture is destructive. Codebase Explorer must analyze it
statically like any other repository and must never execute any of it (SECURITY.md S2, §6).

## 1. Payloads (each writes a marker if executed)

| File | Payload | Would run when |
|---|---|---|
| `Directory.Build.props` | Property function `$([System.IO.File]::WriteAllText(…))` → `MARKER-property-function.txt` | MSBuild evaluates the project |
| `Directory.Build.props` | `<Exec>` in a target before `Restore` → `MARKER-restore-exec.txt` | `dotnet restore` |
| `src/Hostile.App/Hostile.App.csproj` | Property function → `MARKER-project-property-function.txt` | MSBuild evaluates the project |
| `src/Hostile.App/Hostile.App.csproj` | `<Exec>` in a target before `CoreCompile` → `MARKER-build-exec.txt` | `dotnet build` / design-time build |
| `src/Hostile.App/hostile.targets` (imported) | `<WriteLinesToFile>` task → `MARKER-imported-target.txt` | MSBuild runs the imported target |
| `src/Hostile.App/Hostile.App.csproj` | `PackageReference Hostile.Analyzers` with `OutputItemType="Analyzer"` and `<Analyzer Include="../../tools/Hostile.Generator.dll" />` | A compiler loads analyzers / source generators |
| `tools/Hostile.Generator.dll` | Not a real assembly (text); loading it would throw `BadImageFormatException` | Anything loads repository assemblies |
| `src/Hostile.App/Startup.cs` | `[ModuleInitializer]` → `MARKER-module-initializer.txt` in the app's base directory | The code is compiled **and** loaded |

## 2. Expected

- The import succeeds; the model contains the project `Hostile.App` and the entity
  `M:Hostile.App.Startup.WriteMarker` (the code is parsed, not run).
- After the analysis the repository copy is byte-identical (no new, missing or changed file), and no
  `MARKER-*` file exists in the repository copy, the test output directory or the app's data directory.
- No assembly is loaded from the repository.
- The modules that read repository content (`Analysis`, `Storage`, `Git`, `AI`, and `Graph`, `Application`,
  `Domain`, `Contracts`) contain no reference to `System.Diagnostics.Process`/`ProcessStartInfo` and no
  P/Invoke of a process-creation function.

Diagnostics for the unsupported property functions and the unevaluated `<Import>` are allowed (they
document what static analysis skipped); they are not part of this oracle.

Asserted by `tests/CodebaseExplorer.IntegrationTests/Security/HostileDotNetFixtureTests.cs` with the
reusable `ZeroExecutionHarness` (TEST_STRATEGY.md §5).
