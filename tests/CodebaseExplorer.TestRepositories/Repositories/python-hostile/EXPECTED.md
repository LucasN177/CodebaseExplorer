# Fixture `python-hostile` — expected behavior (ANALYSIS-011, QA-003 harness)

A Python project whose packaging hooks and modules would run code if anything built, installed, imported or
tested it. **Every payload only writes a `MARKER-*.txt` file next to itself**; nothing here is destructive.
Codebase Explorer must analyze it statically and never execute any of it (SECURITY.md S2, §6).

## 1. Payloads (each writes a marker if executed)

| File | Would run when |
|---|---|
| `setup.py` | `python setup.py …`, `pip install .` (legacy), `-e .` in `requirements.txt` |
| `evil_backend.py` (`build-backend` with `backend-path = ["."]`) | a PEP 517 frontend builds the project |
| `conftest.py` | pytest collects the folder |
| `sitecustomize.py`, `usercustomize.py`, `evil.pth` | an interpreter starts with the folder on its path / as site-packages |
| `hostile/__init__.py`, `hostile/__main__.py` | the package is imported or run with `python -m hostile` |
| `requirements.txt` (`-e .`, `--extra-index-url`, direct URL) | `pip install -r requirements.txt` |

## 2. Expected

- The import succeeds with one project `python-hostile` (manifest `pyproject.toml`, properties
  `buildBackend=evil_backend`, `setupPy=true`, `manifests=pyproject.toml;requirements.txt;setup.cfg;setup.py`).
- PyPI dependencies: `requests` (`pyproject.toml`), `click` (`setup.cfg`), `evilpkg` (direct reference, no
  version spec). `setup_requires` and `[build-system] requires` are not dependencies.
- Diagnostics include `python.setup-py.not-executed` and `python.requirements.option-ignored` (`-e`,
  `--extra-index-url`).
- After the analysis the repository copy is byte-identical and no `MARKER-*` file exists in the copy, the test
  output directory or the app's data directory.
