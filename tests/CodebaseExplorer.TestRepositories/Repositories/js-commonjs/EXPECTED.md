# Fixture `js-commonjs` — expected analysis results

Oracle for ANALYSIS-009 (§1–§3) and ANALYSIS-010 (§4–§6): a single CommonJS package without TypeScript.
Conventions as in `ts-monorepo-workspaces/EXPECTED.md` (locations `line:column`, 1-based, UTF-16 columns;
LF, byte-exact).

## 1. Files

`package.json` (`ProjectManifest`, marker `NodePackage`), `lib/config.json` (`Configuration`),
`lib/index.js`, `lib/routes/index.js`, `lib/util/helper.js` (`Source`), `test/index.test.js` (`Test`),
`EXPECTED.md` (`Documentation`). Total: 7 files.

## 2. Project

| Project | Kind | Root | Manifest | Primary language | Properties |
|---|---|---|---|---|---|
| `legacy-tool` | `NodePackage` | `.` | `package.json` | `javascript` | `version=2.0.0`, `scripts=test` |

No `lifecycleScripts` property (`test` is not a lifecycle script) and no diagnostics.

## 3. Package references (npm, `Confirmed`)

| Package | Rule | Location | Attributes |
|---|---|---|---|
| `express` | `package.json.dependencies` | `package.json:9:5` | `versionSpec=^4.19.0`, `dependencyType=dependencies` |
| `mocha` | `package.json.devDependencies` | `package.json:12:5` | `versionSpec=^10.0.0`, `dependencyType=devDependencies`, `devOnly=true` |

## 4. Modules

Directory modules `lib`, `lib/routes`, `lib/util`, `test`; file modules `lib/index.js`,
`lib/routes/index.js`, `lib/util/helper.js`, `test/index.test.js`. `lib/config.json` is not a module.

## 5. Entities

CommonJS exports: `module.exports = X`, `module.exports = { a, b }` and `exports.name = X` mark the named
top-level declarations as exported.

| Entity | Kind | Visibility | Symbol key | Declaration |
|---|---|---|---|---|
| `start` | `Function` | `Exported` | `lib/index.js#start` | `lib/index.js:5:10` |
| `createRouter` | `Function` | `Exported` | `lib/routes/index.js#createRouter` | `lib/routes/index.js:3:10` |
| `Helper` | `Class` | `Exported` | `lib/util/helper.js#Helper` | `lib/util/helper.js:1:7` |

## 6. Imports

| Source file | Target | Rule | Level | Location |
|---|---|---|---|---|
| `lib/index.js` | npm `express` | `ts.require.package` | `Syntactic` | `1:17` |
| `lib/index.js` | `lib/routes/index.js` (directory → `index.js`) | `ts.require.relative` | `Confirmed` | `2:26` |
| `lib/routes/index.js` | `lib/util/helper.js` (explicit extension) | `ts.require.relative` | `Confirmed` | `1:16` |
| `test/index.test.js` | `lib/index.js` (directory → `index.js`) | `ts.require.relative` | `Confirmed` | `3:19` |

**Negative cases:** `require('./config.json')` resolves to a file that is not a source module: no
relationship, no diagnostic. `require('node:assert')` and `require('path')` are Node built-in modules: no
relationship, no diagnostic. `require(name)` (`lib/routes/index.js:5:18`) has a computed argument: no
relationship and one `Info` diagnostic `ts.import.computed`.
