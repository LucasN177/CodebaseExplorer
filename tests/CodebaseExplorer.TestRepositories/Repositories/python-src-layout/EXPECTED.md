# Fixture `python-src-layout` — expected analysis results

Oracle for ANALYSIS-011 (Python project system, §1–§4) and ANALYSIS-012 (Python imports and declarations,
§5–§7). Locations are `line:column` (1-based). LF, byte-exact (`.gitattributes`: `-text`).

**Location conventions.** `pyproject.toml` PEP 621 arrays: first character (`"`) of the requirement string;
Poetry tables: first character of the key; requirement files: column 1 of the line. Imports: first character of
the statement. Entities: first character of the class/function name.

## 1. Files

`pyproject.toml` (marker `PythonProject`) and `requirements-dev.txt` (marker `PythonRequirements`) are
`ProjectManifest`s; `tests/test_service.py` is `Test`; the five files under `src/` are `Source`
(`src/inventory/storage/__init__.py` is empty); `EXPECTED.md` is `Documentation`. Total: 9 files.

## 2. Project

One project per folder that contains a Python manifest (`pyproject.toml`, `setup.cfg`, `setup.py`,
`requirements*.txt`); all manifests of the folder contribute to it. The manifest file of the project is the
first that exists of `pyproject.toml`, `setup.cfg`, `setup.py`, `requirements.txt`, other `requirements*.txt`
(ordinal).

| Project | Kind | Ecosystem | Root | Manifest | Primary language |
|---|---|---|---|---|---|
| `Inventory-Service` | `PythonProject` | `Python` | `.` | `pyproject.toml` | `python` |

Properties: `version=0.3.0`, `requiresPython=>=3.11`, `buildBackend=hatchling.build`, `layout=src` (a `src/`
folder containing a package), `manifests=pyproject.toml;requirements-dev.txt`.

`[build-system] requires` (`hatchling`) is a build requirement, **not** a dependency.

## 3. Package references (PyPI, `Confirmed`)

External dependency names are normalized per PEP 503; the display name is the name as first written.

| Package (display) | Rule | Location | Attributes |
|---|---|---|---|
| `requests` | `pyproject.project.dependencies` | `pyproject.toml:10:5` | `versionSpec=>=2.31` |
| `PyYAML` (`pyyaml`) | `pyproject.project.dependencies` | `pyproject.toml:11:5` | `versionSpec=~=6.0` |
| `attrs` | `pyproject.project.dependencies` | `pyproject.toml:12:5` | — |
| `pytest` | `pyproject.project.optional-dependencies` | `pyproject.toml:16:8` **and** `requirements.txt` `requirements-dev.txt:2:1` (2 evidence records) | `versionSpec=>=8`, `extra=dev` (from the first declaration) |
| `black` | `requirements.txt` | `requirements-dev.txt:3:1` | `versionSpec===24.8.0`, `marker=python_version >= "3.11"` |

## 4. Diagnostics of the project-system analysis

| Severity | Code | Location |
|---|---|---|
| `Info` | `python.requirements.option-ignored` (`--index-url`) | `requirements-dev.txt:4:1` |
| `Warning` | `python.requirements.include-outside-repository` (`-r ../outside.txt`, not followed) | `requirements-dev.txt:5:1` |

## 5. Modules (ANALYSIS-012)

Module names are dotted paths relative to the source root: `src/` for files below `src/` (layout `src`),
otherwise the project root. Folders with Python files are `Package` modules (also without `__init__.py`,
namespace packages); every `.py` file except `__init__.py` is a `FileModule` whose parent is its package.
Declarations and imports of an `__init__.py` belong to its package module.

| Package modules | File modules |
|---|---|
| `inventory`, `inventory.storage`, `tests` | `inventory.models`, `inventory.service`, `inventory.storage.db`, `tests.test_service` |

## 6. Entities (top-level classes and functions)

Symbol key `{dotted module}:{name}`; names starting with `_` are `Private`, others `Public`.

| Entity | Kind | Visibility | Symbol key | Declaration |
|---|---|---|---|---|
| `Item` | `Class` | `Public` | `inventory.models:Item` | `src/inventory/models.py:7:7` |
| `make_item` | `Function` | `Public` | `inventory.models:make_item` | `src/inventory/models.py:12:5` |
| `InventoryService` | `Class` | `Public` | `inventory.service:InventoryService` | `src/inventory/service.py:11:7` |
| `_helper` | `Function` | `Private` | `inventory.service:_helper` | `src/inventory/service.py:23:5` |
| `Database` | `Class` | `Public` | `inventory.storage.db:Database` | `src/inventory/storage/db.py:4:7` |
| `test_service_requires_database` | `Function` | `Public` | `tests.test_service:test_service_requires_database` | `tests/test_service.py:6:5` |

Variables (`__all__`, `_registry`) and methods are not entities.

## 7. Imports

Rules: `py.import.relative` and `py.import.absolute` (module of the repository, `Confirmed`);
`py.import.package` (top-level import name equals a declared distribution, `Syntactic`);
`py.import.package-mapped` (import name mapped to a declared distribution by the built-in mapping table, e.g.
`yaml` → `PyYAML`, `Heuristic`); `py.import.package-undeclared` (not declared; target named by the mapping
table or the import name, `Heuristic`). Standard-library modules produce nothing.

| Source module | Target | Rule | Level | Location |
|---|---|---|---|---|
| `inventory` | `inventory.models` | `py.import.relative` | `Confirmed` | `src/inventory/__init__.py:1:1` |
| `inventory.models` | PyPI `attrs` | `py.import.package` | `Syntactic` | `src/inventory/models.py:3:1` |
| `inventory.service` | PyPI `requests` | `py.import.package` | `Syntactic` | `src/inventory/service.py:2:1` |
| `inventory.service` | PyPI `pyyaml` | `py.import.package-mapped` | `Heuristic` | `src/inventory/service.py:3:1` |
| `inventory.service` | PyPI `beautifulsoup4` | `py.import.package-undeclared` | `Heuristic` | `src/inventory/service.py:4:1` |
| `inventory.service` | `inventory.storage` (package) | `py.import.relative` | `Confirmed` | `src/inventory/service.py:6:1` |
| `inventory.service` | `inventory.models` | `py.import.relative` | `Confirmed` | `src/inventory/service.py:7:1` |
| `inventory.service` | `inventory.storage.db` | `py.import.absolute` | `Confirmed` | `src/inventory/service.py:8:1` |
| `inventory.storage.db` | `inventory.models` | `py.import.relative` | `Confirmed` | `src/inventory/storage/db.py:1:1` |
| `tests.test_service` | PyPI `pytest` | `py.import.package` | `Syntactic` | `tests/test_service.py:1:1` |
| `tests.test_service` | `inventory.service` | `py.import.absolute` | `Confirmed` | `tests/test_service.py:3:1` |

**Negative cases:** `json` and `dataclasses` (standard library) and the commented `import os`
(`service.py:16`) produce nothing; no diagnostics.
