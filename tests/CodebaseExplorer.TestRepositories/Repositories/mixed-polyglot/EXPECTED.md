# Fixture `mixed-polyglot` — expected analysis results

One repository with a .NET API, a TypeScript frontend and a Poetry-based Python tool. Oracle for the
coexistence of the three ecosystems (ANALYSIS-009 … ANALYSIS-012) and for multi-language graphs. Conventions
as in the other fixtures (locations `line:column`, 1-based; LF, byte-exact).

## 1. Files

| Path | Classification |
|---|---|
| `backend/Api/Api.csproj`, `frontend/package.json`, `tools/py/pyproject.toml` | `ProjectManifest` |
| `frontend/tsconfig.json` | `Configuration` |
| `backend/Api/Program.cs`, `frontend/src/api.ts`, `tools/py/report_tool/__init__.py`, `tools/py/report_tool/report.py` | `Source` |
| `README.md`, `EXPECTED.md` | `Documentation` |

Total: 10 files.

## 2. Projects

| Project | Kind | Ecosystem | Root | Manifest | Primary language | Properties |
|---|---|---|---|---|---|---|
| `Api` | `DotNetProject` | `DotNet` | `backend/Api` | `backend/Api/Api.csproj` | `csharp` | `Sdk=Microsoft.NET.Sdk`, `TargetFramework=net10.0`, `OutputType=Exe` |
| `frontend` | `NodePackage` | `Node` | `frontend` | `frontend/package.json` | `typescript` | `private=true`, `tsconfig=frontend/tsconfig.json` |
| `report-tool` | `PythonProject` | `Python` | `tools/py` | `tools/py/pyproject.toml` | `python` | `version=1.0.0`, `requiresPython=^3.12`, `layout=flat`, `manifests=pyproject.toml` |

No project references between them. The repository root is no project; `README.md` and `EXPECTED.md` have no
owner.

## 3. Package references (`Confirmed`)

| Project | Package | Rule | Location | Attributes |
|---|---|---|---|---|
| `frontend` | npm `axios` | `package.json.dependencies` | `frontend/package.json:5:5` | `versionSpec=^1.7.0`, `dependencyType=dependencies` |
| `report-tool` | PyPI `pandas` | `pyproject.poetry.dependencies` | `tools/py/pyproject.toml:7:1` | `versionSpec=^2.2` |
| `report-tool` | PyPI `pytest` | `pyproject.poetry.group-dependencies` | `tools/py/pyproject.toml:10:1` | `versionSpec=^8.3`, `group=dev`, `devOnly=true` |

The Poetry entry `python = "^3.12"` is the Python version constraint (`requiresPython`), not a package.

## 4. Code (ANALYSIS-005, -010, -012)

| Project | Modules | Entities | Imports |
|---|---|---|---|
| `Api` | namespace `Api` | `Program` (class, `backend/Api/Program.cs:3:21`), `Main` (method) | — |
| `frontend` | directory `src`, file module `src/api.ts` | `fetchOrders` (`Function`, `Exported`, `src/api.ts#fetchOrders`, `frontend/src/api.ts:3:23`) | `src/api.ts` → npm `axios`, `ts.import.package`, `Syntactic`, `frontend/src/api.ts:1:1` |
| `report-tool` | package `report_tool`, file module `report_tool.report` | `build_report` (`Function`, `Public`, `report_tool.report:build_report`, `tools/py/report_tool/report.py:4:5`) | `report_tool.report` → PyPI `pandas`, `py.import.package`, `Syntactic`, `tools/py/report_tool/report.py:1:1` |

No diagnostics of severity `Warning` or `Error`.
