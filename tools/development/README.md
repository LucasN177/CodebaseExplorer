# Development tools

Scripts and small utilities for developers. Nothing in this folder is shipped with the product.

## Planned content

| Tool | Task | Purpose |
|---|---|---|
| Synthetic repository generator | QA-002 | Generates large repositories (e.g. 2,000 .NET projects, 50k TypeScript files) into a temporary directory for performance benchmarks. Generated output is never committed. |
| Benchmark runner | QA-002 | Runs analysis/persistence/projection benchmarks against generated repositories and prints results for TEST_STRATEGY.md §8. |
| Packaging scripts | QA-004 | Self-contained publish per runtime identifier (`win-x64`, `osx-arm64`, `linux-x64`). |

## Rules

- Scripts must be cross-platform (PowerShell 7 `pwsh` or .NET file-based apps / small console
  projects); no Bash-only or cmd-only scripts.
- A tool that becomes a .NET project is added to `CodebaseExplorer.slnx` under a `tools` solution
  folder and follows the coding standards.
- Tools never execute code from analyzed repositories.
