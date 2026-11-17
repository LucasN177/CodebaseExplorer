# Coding Standards — Codebase Explorer

> Status: **binding** · Last reviewed: 2026-11-17
> Mechanically enforced parts live in `.editorconfig` and `Directory.Build.props`
> (warnings are errors; code style is enforced in build).

## 1. Language and project settings

- C# `latest` on `net10.0`; nullable reference types **enabled**; implicit usings enabled.
- File-scoped namespaces; namespace = folder path (`CodebaseExplorer.<Project>.<Folder>`).
- One public type per file; file name = type name.
- `sealed` by default for classes not designed for inheritance.
- Module implementations are `internal`; only ports (Application), DTOs (Contracts), Domain types
  and `DependencyInjection` classes are `public`.

## 2. Naming

| Element | Convention | Example |
|---|---|---|
| Types, methods, properties | PascalCase | `RepositoryScanner`, `ScanAsync` |
| Interfaces | `I` prefix | `IRepositoryAnalyzer` |
| Private fields | `_camelCase` | `_fileReader` |
| Async methods | `Async` suffix | `AnalyzeAsync` |
| Ports | noun describing the capability | `IGitRepositoryReader`, `IArchitectureModelStore` |
| Use cases | verb phrase | `ImportLocalRepository`, `GetGraphView` |
| Tests | `<Subject>Tests`, methods `Method_State_ExpectedResult` | `ElementIdTests.FromNaturalKey_SameKey_ReturnsSameId` |

## 3. Domain code

- No dependencies on infrastructure, serialization or UI.
- Invariants are enforced in constructors/factory methods; invalid states are unrepresentable
  where practical (value objects instead of primitives for IDs, paths, locations).
- Prefer immutable types (`record`/`readonly record struct`, init-only properties,
  `IReadOnlyList<T>`).
- Enums: append-only; persisted by name.

## 4. Application code

- Use cases are small classes with one public `ExecuteAsync`/`…Async` entry point (or a few
  closely related ones), depending only on ports.
- Expected failures return `Result` / `Result<T>` with an `ApplicationError`
  (`Application/Common`); exceptions are for programming errors (invalid arguments, violated
  invariants) and unexpected infrastructure faults. "Expected" means the user or the environment
  can cause it in normal operation: a path that does not exist, an unknown repository id, an
  analysis that is already running, a rejected token.
- `ApplicationError` has a `Kind` (`Validation`, `NotFound`, `Conflict`, `Failure`), a stable
  `Code` (lowercase dot-separated segments: `<area>.<problem>`, e.g. `repository.path-not-found`),
  a human-readable `Message` and optional `Details` (paths, ids — never secrets or source code).
  Callers branch on `Kind`/`Code`, never on `Message`.
- **Cancellation is not a failure result.** Operations observe their `CancellationToken` and let
  `OperationCanceledException` propagate; ViewModels catch it to show "cancelled".
- Use cases that report progress take an `IProgress<T>`; analysis progress uses
  `AnalysisProgress(stage, processed, total?, currentItem?)` (`Application/Analysis`).
  `CurrentItem` is a path or project name, never file content.
- From CORE-006 and ANALYSIS-001 on, every use case and port with an expected-failure path uses
  these types in its signature.
- Ports are defined next to the feature that needs them (`Application/<Feature>/I….cs`);
  cross-cutting ports live in `Application/Abstractions`.

## 5. Async, cancellation, concurrency

- All I/O is asynchronous; no `.Result`/`.Wait()`/`GetAwaiter().GetResult()`.
- Every public async method that does I/O or CPU-heavy work takes a `CancellationToken`
  (last parameter, named `cancellationToken`, no default value in ports) and passes it on
  (CA2016 is a warning → error).
- Long-running operations accept `IProgress<T>` and report at a bounded rate.
- CPU-bound work runs on the thread pool (`Task.Run` only at the Application/UI boundary, not in
  library code); use bounded parallelism (`Parallel.ForEachAsync` with `MaxDegreeOfParallelism`).
- `ConfigureAwait(false)` is not required (CA2007 disabled): desktop UI code needs the context,
  library code tolerates it. Do not block on async code anywhere.
- Use `TimeProvider` instead of `DateTime.Now`/`DateTimeOffset.UtcNow` in testable code.

## 6. Dependency injection

- Constructor injection only; no service locator (`IServiceProvider` injection) outside the
  composition root and factories that genuinely need it.
- Lifetimes per SYSTEM_ARCHITECTURE.md §5; `ValidateOnBuild` must keep passing.
- Interfaces only at **meaningful boundaries** (ports between Application and modules, seams for
  testing external systems). Do not add an interface for every class.

## 7. MVVM and UI

- Views: XAML + compiled bindings (`x:DataType`); code-behind contains only `InitializeComponent`
  and purely visual logic (focus, animations).
- ViewModels: inherit `ViewModelBase`; use CommunityToolkit.Mvvm source generators
  (`[ObservableProperty]` on partial properties, `[RelayCommand]`); no references to Avalonia
  controls; UI services (dialogs, theme, clipboard, graph host) behind small interfaces in
  `Desktop/Services`.
- ViewModels call Application use cases/ports and consume Contracts DTOs — never EF entities,
  Roslyn/Tree-sitter types, or module internals.
- ViewModel ↔ View mapping via explicit `DataTemplate`s.
- Long operations: async commands with cancellation and busy state.

## 8. Error handling and logging

- Use `ILogger<T>` with message templates (`logger.LogInformation("Scanned {FileCount} files", n)`).
- Never log source content, tokens, API keys or full remote URLs with credentials.
- Catch exceptions only where you can add context, recover, or convert to diagnostics.
  Analyzer exceptions become diagnostics (ANALYSIS_ENGINE.md §3).

## 9. Security-relevant coding rules

- Never start processes (`Process.Start`) in Analysis, Git, AI, Storage — see SECURITY.md.
- XML: `DtdProcessing.Prohibit`, `XmlResolver = null`, size limits.
- File access to repository content only via `IRepositoryFileReader`.
- Render untrusted strings as text, never as markup/HTML.

## 10. Comments and documentation

- XML doc comments on public ports, DTOs and Domain types: intent and contract, not restating the name.
- Comments explain *why*, not *what*. Reference task IDs/ADRs where a decision is non-obvious.
- No commented-out code; no `TODO` without a task ID (`// TODO(ANALYSIS-006): …`).

## 11. Tests

See [TEST_STRATEGY.md](../implementation/TEST_STRATEGY.md). Test code follows the same standards
(except underscore naming of test methods).
