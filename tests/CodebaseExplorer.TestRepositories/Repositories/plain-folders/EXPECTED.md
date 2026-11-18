# Fixture `plain-folders` — expected analysis results

A repository without any project manifest (GRAPH-009, USER_WORKFLOWS.md WF-01 edge case "Folder contains no
recognizable projects"). LF, byte-exact.

## 1. Files

`README.md`, `EXPECTED.md`, `docs/setup.md`, `docs/guides/deploy.md` (`Documentation`), `scripts/build.sh`,
`scripts/cleanup.py` (`Source`), `notes/2026-11.txt` (`Documentation`). Total: 7 files. No file carries a project marker.

## 2. Model

No projects, modules, entities, external dependencies or relationships (`scripts/cleanup.py` belongs to no project,
so no language analyzer looks at it). The run succeeds without diagnostics.

## 3. Graph (folder fallback)

The graph has the level `folder` and no edges. Roots, in this order: `docs` (2 files below it, expandable into
`guides`), `notes` (1 file), `scripts` (2 files), `(repository root)` (2 files: `EXPECTED.md`, `README.md`).
