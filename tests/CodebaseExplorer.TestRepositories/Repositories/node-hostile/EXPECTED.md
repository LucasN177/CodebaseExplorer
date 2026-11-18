# Fixture `node-hostile` — expected behavior (ANALYSIS-009, QA-003 harness)

A Node workspace whose package-manager hooks and modules would run code if anything installed, built or loaded
them. **Every payload only writes a `MARKER-*.txt` file**; nothing here is destructive. Codebase Explorer must
analyze it statically and never execute any of it (SECURITY.md S2, §6).

## 1. Payloads (each writes a marker if executed)

| File | Payload | Would run when |
|---|---|---|
| `package.json` | `preinstall`, `install`, `postinstall`, `prepare`, `test` scripts → `scripts/write-marker.js` → `MARKER-script-<stage>.txt` | `npm install` / `npm test` |
| `.npmrc` | `script-shell` pointing to the marker script | npm runs any script |
| `packages/native-addon/package.json` + `binding.gyp` | `install` → `node-gyp rebuild`; a gyp action → `MARKER-node-gyp.txt` | installing the package |
| `packages/native-addon/index.js` | top-level side effect → `MARKER-module-load.txt` | the module is loaded (`require`) |
| `vendor/evil/package.json` | `postinstall` → `MARKER-vendor-postinstall.txt` | installing the `file:` dependency |
| `tsconfig.json` | TypeScript language-service plugin `evil-ts-plugin` | an editor's tsserver loads plugins |

## 2. Expected

- The import succeeds. Projects: `node-hostile` (`NodeWorkspaceRoot`, properties
  `lifecycleScripts=install;postinstall;preinstall;prepare`, `scripts=install;postinstall;preinstall;prepare;test`),
  `native-addon` (member, `lifecycleScripts=install`) and `evil-local` (`vendor/evil`, not a member, `lifecycleScripts=postinstall`).
- `native-addon` and `evil-local` are packages of the repository, so the root's dependencies on them are
  `ProjectReference`s (nothing is fetched or linked); `evil-git` is an npm `PackageReference` with
  `versionSpec=git+https://example.invalid/evil.git`.
- One `Info` diagnostic `node.scripts.lifecycle-not-executed` per lifecycle script (6).
- After the analysis the repository copy is byte-identical and no `MARKER-*` file exists in the copy, the test
  output directory or the app's data directory.
