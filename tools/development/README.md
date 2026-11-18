# Development tools

Scripts and small utilities for developers. Nothing in this folder is shipped with the product.

## Content

| Tool | Task | Purpose |
|---|---|---|
| `Run-Benchmarks.ps1` | QA-002 (**exists**) | Builds in Release and runs the explicit benchmark tests (`PerformanceBenchmarks`, the STORE-004 model-store benchmark); prints results for TEST_STRATEGY.md §8. The synthetic repository generator it relies on is `tests/CodebaseExplorer.TestRepositories/SyntheticRepository.cs` (2,000 .NET projects, 50,000 TypeScript files …), writing into temporary folders that are deleted afterwards; generated output is never committed. |
| Packaging scripts | QA-004 (planned) | Self-contained publish per runtime identifier (`win-x64`, `osx-arm64`, `linux-x64`). |

## Rules

- Scripts must be cross-platform (PowerShell 7 `pwsh` or .NET file-based apps / small console
  projects); no Bash-only or cmd-only scripts.
- A tool that becomes a .NET project is added to `CodebaseExplorer.slnx` under a `tools` solution
  folder and follows the coding standards.
- Tools never execute code from analyzed repositories.
