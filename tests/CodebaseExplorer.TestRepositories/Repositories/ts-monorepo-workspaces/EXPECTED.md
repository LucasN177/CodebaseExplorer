# Fixture `ts-monorepo-workspaces` — expected analysis results

Oracle for ANALYSIS-009 (Node project system, §1–§5) and ANALYSIS-010 (TypeScript/JavaScript imports and
declarations, §6–§8). Tests assert against this document, not against current analyzer output. Paths are
repository-relative with `/`; locations are `line:column` (1-based, columns in UTF-16 code units). All files
use LF line endings and are stored byte-exact (`.gitattributes`: `-text`).

**Location conventions.** Manifest entries: first character (`"`) of the JSON property name (for
`tsconfig.json` references: of the `"path"` property). Imports: first character of the statement for
`import`/`export … from`, of the call for `require(…)`/`import(…)`. Entities: first character of the declared
identifier.

## 1. Files

| Path | Classification | Marker |
|---|---|---|
| `package.json`, `packages/core/package.json`, `packages/shared/package.json`, `apps/web/package.json` | `ProjectManifest` | `NodePackage` |
| `tsconfig.json`, `tsconfig.base.json`, `packages/core/tsconfig.json`, `apps/web/tsconfig.json` | `Configuration` | — |
| `apps/web/src/App.test.tsx` | `Test` | — |
| the other 9 `.ts`/`.tsx` files | `Source` | — |

`EXPECTED.md` is `Documentation`. Total: 19 files. No `node_modules` folder exists; nothing is installed.

## 2. Projects

Ecosystem `Node` for all. Name = the `name` field of `package.json`. Primary language `typescript` (a
`tsconfig.json` in the project folder or TypeScript files owned by the project).

| Project | Kind | Root | Manifest | Properties |
|---|---|---|---|---|
| `acme-monorepo` | `NodeWorkspaceRoot` | `.` | `package.json` | `private=true`, `workspaces=packages/*;apps/*`, `scripts=build;postinstall`, `lifecycleScripts=postinstall`, `tsconfig=tsconfig.json` |
| `@acme/core` | `NodePackage` | `packages/core` | `packages/core/package.json` | `version=1.2.0`, `workspace=acme-monorepo`, `tsconfig=packages/core/tsconfig.json` |
| `@acme/shared` | `NodePackage` | `packages/shared` | `packages/shared/package.json` | `version=0.1.0`, `workspace=acme-monorepo` |
| `web` | `NodePackage` | `apps/web` | `apps/web/package.json` | `private=true`, `workspace=acme-monorepo`, `tsconfig=apps/web/tsconfig.json` |

The root is a workspace root because its `package.json` has `workspaces`; a member is a package folder
matched by one of the patterns (`packages/*`, `apps/*`). Files are owned by the innermost project, so the
root owns only `package.json`, `tsconfig.json`, `tsconfig.base.json` and `EXPECTED.md`.

## 3. Project references (`ProjectReference`, level `Confirmed`)

A dependency whose name is the name of a Node project in the repository is a project reference (whatever the
version spec says, including `workspace:*`).

| Source | Target | Evidence (rule, location) | Attributes |
|---|---|---|---|
| `@acme/core` | `@acme/shared` | `package.json.dependencies` `packages/core/package.json:6:5`; `tsconfig.references` `packages/core/tsconfig.json:6:20` | `versionSpec=workspace:*`, `dependencyType=dependencies` |
| `web` | `@acme/core` | `package.json.dependencies` `apps/web/package.json:5:5` | `versionSpec=^1.0.0`, `dependencyType=dependencies` |

**Negative case — solution-style tsconfig:** the root `tsconfig.json` has `"files": []` and only
`references`. It groups projects and is **not** a dependency: no relationship from `acme-monorepo`.

## 4. Package references (`PackageReference` → npm, level `Confirmed`)

| Project | Package | Rule | Location | Attributes |
|---|---|---|---|---|
| `acme-monorepo` | `typescript` | `package.json.devDependencies` | `package.json:13:5` | `versionSpec=^5.6.0`, `dependencyType=devDependencies`, `devOnly=true` |
| `@acme/core` | `lodash` | `package.json.dependencies` | `packages/core/package.json:7:5` | `versionSpec=^4.17.21`, `dependencyType=dependencies` |
| `web` | `react` | `package.json.dependencies` | `apps/web/package.json:6:5` | `versionSpec=^19.0.0`, `dependencyType=dependencies` |
| `web` | `vitest` | `package.json.devDependencies` | `apps/web/package.json:9:5` | `versionSpec=^3.0.0`, `dependencyType=devDependencies`, `devOnly=true` |

Stage 2 therefore yields 4 projects, 4 npm external dependencies and 6 relationships.

## 5. Diagnostics of the project-system analysis

| Severity | Code | Location | About |
|---|---|---|---|
| `Info` | `node.scripts.lifecycle-not-executed` | `package.json:10:5` | lifecycle script `postinstall` is listed, never run |

No warnings or errors. `build` is not a lifecycle script.

## 6. Modules (ANALYSIS-010)

Directories are `Directory` modules (qualified name = path relative to the project root), source files are
`FileModule`s (qualified name = path relative to the project root) whose parent is their directory module.

| Project | Directory modules | File modules |
|---|---|---|
| `@acme/core` | `src`, `src/orders` | `src/index.ts`, `src/orders/order.ts`, `src/orders/order-service.ts` |
| `@acme/shared` | `src` | `src/index.ts`, `src/money.ts`, `src/logging.ts` |
| `web` | `src`, `src/pages` | `src/App.tsx`, `src/App.test.tsx`, `src/main.tsx`, `src/pages/settings.tsx` |

## 7. Entities (top-level declarations)

Symbol key `{file path relative to project}#{name}`; non-exported declarations use `#~{name}`; default
exports use `#default`. Only classes, interfaces, type aliases, enums, function declarations and **exported**
variable declarations are entities (an exported `const` with an arrow-function initializer is a `Function`).

| Project | Entity | Kind | Visibility | Symbol key | Declaration |
|---|---|---|---|---|---|
| `@acme/core` | `Order` | `Interface` | `Exported` | `src/orders/order.ts#Order` | `packages/core/src/orders/order.ts:1:18` |
| `@acme/core` | `OrderId` | `TypeAlias` | `Exported` | `src/orders/order.ts#OrderId` | `packages/core/src/orders/order.ts:6:13` |
| `@acme/core` | `OrderService` | `Class` | `Exported` | `src/orders/order-service.ts#OrderService` | `packages/core/src/orders/order-service.ts:9:14` |
| `@acme/core` | `internalHelper` | `Function` | `NotExported` | `src/orders/order-service.ts#~internalHelper` | `packages/core/src/orders/order-service.ts:16:10` |
| `@acme/shared` | `formatMoney` | `Function` | `Exported` | `src/money.ts#formatMoney` | `packages/shared/src/money.ts:1:17` |
| `@acme/shared` | `log` | `Function` | `Exported` | `src/logging.ts#log` | `packages/shared/src/logging.ts:1:14` |
| `web` | `App` | `Function` | `Exported` | `src/App.tsx#App` | `apps/web/src/App.tsx:6:17` |
| `web` | `Settings` | `Function` | `Exported` | `src/pages/settings.tsx#default` | `apps/web/src/pages/settings.tsx:1:25` |

`text` (not exported), `service` and `settings` (local or not exported) are not entities.

## 8. Imports (`Imports`, file module → file module or npm dependency)

Rule ids: `ts.{syntax}.{resolution}` with syntax `import`, `export` (`export … from`), `require`,
`dynamic-import` and resolution `relative`, `path-alias` (`tsconfig` `paths`, also inherited through
`extends`), `workspace` (package of the repository, resolved through `exports["."]`/`types`/`main` of its
`package.json`), `package` (declared in the importing project's or the workspace root's `package.json`),
`package-undeclared`.

| # | Source file | Target | Rule | Level | Location |
|---|---|---|---|---|---|
| 1 | `packages/core/src/index.ts` | `packages/core/src/orders/order-service.ts` | `ts.export.relative` | `Confirmed` | `1:1` |
| 2 | `packages/core/src/index.ts` | `packages/core/src/orders/order.ts` | `ts.export.relative` | `Confirmed` | `2:1` |
| 3 | `packages/core/src/orders/order-service.ts` | `packages/core/src/orders/order.ts` | `ts.import.relative` | `Confirmed` | `1:1` |
| 4 | `packages/core/src/orders/order-service.ts` | `packages/shared/src/index.ts` | `ts.import.workspace` | `Confirmed` | `2:1` |
| 5 | `packages/core/src/orders/order-service.ts` | npm `lodash` (specifier `lodash/groupBy`) | `ts.import.package` | `Syntactic` | `3:1` |
| 6 | `packages/core/src/orders/order-service.ts` | `packages/shared/src/logging.ts` | `ts.import.path-alias` | `Confirmed` | `4:1` |
| 7 | `packages/shared/src/index.ts` | `packages/shared/src/money.ts` | `ts.export.relative` | `Confirmed` | `1:1` |
| 8 | `apps/web/src/App.tsx` | npm `react` | `ts.import.package` | `Syntactic` | `1:1` |
| 9 | `apps/web/src/App.tsx` | `packages/core/src/index.ts` | `ts.import.workspace` | `Confirmed` | `2:1` |
| 10 | `apps/web/src/App.tsx` | npm `chart.js` | `ts.import.package-undeclared` | `Heuristic` | `3:1` |
| 11 | `apps/web/src/main.tsx` | `apps/web/src/App.tsx` | `ts.import.relative` | `Confirmed` | `1:1` |
| 12 | `apps/web/src/main.tsx` | `apps/web/src/pages/settings.tsx` | `ts.dynamic-import.relative` | `Confirmed` | `3:18` |
| 13 | `apps/web/src/App.test.tsx` | npm `vitest` | `ts.import.package` | `Syntactic` | `1:1` |
| 14 | `apps/web/src/App.test.tsx` | `apps/web/src/App.tsx` | `ts.import.relative` | `Confirmed` | `2:1` |

`chart.js` becomes an npm external dependency although no manifest declares it.

**Negative cases:** the commented import and the import inside a string literal in `order-service.ts`
(lines 6–7) produce nothing. `./missing` in `App.tsx` does not exist: no relationship, but a `Warning`
`ts.import.unresolved` at `apps/web/src/App.tsx:4:1`. No other diagnostics.
