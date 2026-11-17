# ADR-002: Avalonia UI with MVVM (CommunityToolkit.Mvvm)

- **Status:** Accepted
- **Date:** 2026-11-17

## Context

The product is a cross-platform desktop application (Windows, macOS, Linux) built on .NET 10.
It needs a modern look (Fluent, light/dark), good performance for data-heavy views (trees, lists,
inspector panels), and the ability to embed a web-based graph renderer. The team's core skill is
C#/.NET; a separate JavaScript application shell is not desired.

## Decision

Use **Avalonia UI** (verified: 12.1.6 on nuget.org, 2026-11-17) with the **Fluent theme** and
**MVVM** based on **CommunityToolkit.Mvvm** (8.4.2).

- Views are XAML (`.axaml`) with compiled bindings (`AvaloniaUseCompiledBindingsByDefault`).
- ViewModels contain presentation logic only and call Application use cases; no business logic in
  Views or code-behind (code-behind is limited to `InitializeComponent` and view-only concerns).
- ViewModel ↔ View mapping is explicit via `DataTemplate`s (no reflection-based view locator).
- Theme variants: System/Light/Dark via `RequestedThemeVariant` behind `IThemeService`.
- The graph is rendered in an embedded WebView (ADR-006).

## Alternatives

| Alternative | Why not |
|---|---|
| .NET MAUI | No first-class Linux desktop support. |
| WPF / WinUI 3 | Windows-only. |
| Electron / Tauri shell with .NET backend | Two application stacks, IPC for everything, larger footprint; .NET-first team. |
| Uno Platform | Viable cross-platform alternative; Avalonia chosen for its mature desktop focus, styling system and ecosystem for desktop tooling apps. |
| Blazor Hybrid | Desktop Linux support depends on community hosts; whole UI in HTML would dilute the native desktop experience. |

## Consequences

**Positive**
- One language and toolchain for UI and core; cross-platform rendering consistency.
- MVVM + source generators keep ViewModels concise and testable without a UI.

**Negative / risks**
- Avalonia 12 is a recent major version; some ecosystem packages lag behind (e.g. no
  `Avalonia.Diagnostics` 12.x was found — dev tooling to be evaluated in UI-001).
- Embedding a WebView adds platform-specific runtime requirements (ADR-006, GRAPH-001).
- UI automation testing options for Avalonia (e.g. headless testing) must be evaluated (TEST_STRATEGY.md).
