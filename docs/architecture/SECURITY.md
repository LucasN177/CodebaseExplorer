# Security & Privacy — Codebase Explorer

> Status: **accepted (binding)** · Last reviewed: 2026-11-17
> These rules apply to every module and every change. Violations are release blockers.

## 1. Security principles

| # | Principle |
|---|---|
| S1 | **Repository content is untrusted input.** File names, paths, file contents, manifests, Git config and history may be malicious. |
| S2 | **Repository code is never executed.** No build, restore, test, script, MSBuild evaluation of targets/tasks/property functions, package manager invocation, lifecycle hook, Git hook, Roslyn analyzer/source generator from the repository, Python `setup.py`, or anything else that runs repository-defined logic. |
| S3 | **Local-first.** Repository contents and analysis results are stored only in the user's local data directory. |
| S4 | **No outbound network by default.** The only default network activity is a clone/update the user explicitly starts. AI providers are opt-in; cloud providers require explicit configuration and per-repository consent. |
| S5 | **Read-only towards repositories.** The app never writes into a user's repository on its own. It writes into its own data directory (including its own clone directory). The single exception is a documentation export into a folder inside the repository that the user explicitly chose and confirmed (EXPORT-002). |
| S6 | **Least data.** Store only what features need (e.g. Git author counts, not identities). Never log source content or secrets. |

## 2. Threat model (summary)

| Asset | Threat | Mitigation |
|---|---|---|
| User machine | Code execution via malicious repository (MSBuild targets, npm scripts, `setup.py`, Git hooks/fsmonitor, analyzers/source generators) | S2; static parsers only; no `MSBuildWorkspace`/`Microsoft.Build.Locator`; LibGit2Sharp (no hooks); hostile fixtures in tests. |
| User machine | Path traversal / symlink escape reading files outside the repository | `IRepositoryFileReader` resolves real paths and enforces containment in the repository root; symlinks not followed by default and never outside the root. |
| User machine | Parser exploits / resource exhaustion (huge files, deeply nested JSON/XML, billion laughs, pathological source) | Size limits, depth limits, XML with DTD prohibited, timeouts, cancellation, analyzer isolation per project; native parsers (Tree-sitter) only on size-limited input. |
| User machine | Native parser crash (Tree-sitter, libgit2) taking down the app | Input limits. Decided in ANALYSIS-008 (ADR-004 spike result): Tree-sitter runs **in-process** on size-limited input (2 MB), trees are walked iteratively, queries are not used on repository content; no crash was observed on hostile input. A native crash is **not contained** (accepted residual risk; parser host process reconsidered in QA-006). |
| Confidential code | Exfiltration via AI cloud provider | Disabled by default; explicit configuration + per-repository consent; context manifest; retriever secret-file exclusions. |
| Confidential code | Exfiltration via graph web view | Bundled assets only, CSP with `connect-src 'none'`, navigation blocked, no remote content. |
| UI integrity | XSS/HTML injection via names in the graph or AI answers | Labels as text only; Markdown rendering without raw HTML; no remote images. |
| Credentials | Token leakage (logs, URLs, `.git/config`, database) | Tokens via credential callback only; URL sanitization; not persisted until OPD-03; log redaction. |
| Integrity of results | Misleading analysis results | Evidence for every relationship; evidence levels; deterministic analysis; no fabricated results. |

## 3. Rules per module

### Analysis
- Read files exclusively through `IRepositoryFileReader` (containment, size limits, encoding detection).
  **Implemented (REPO-001):** every path segment below the root is checked; symbolic links and
  junctions are refused by default (`SymbolicLinkPolicy.DoNotFollow`); with `FollowWithinRoot` a
  link is followed only if its final target lies inside the root. The size limit is checked before
  and during reading. Repository roots are validated by `IRepositoryRootValidator` (absolute,
  existing directory, not a file-system root or device path/administrative share, no overlap with
  the app data directory; a root that is itself a link is analyzed at its target). Known limit: the
  check-then-open sequence does not defend against a repository modified concurrently during analysis.
- XML: `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersInDocument` set.
  **Implemented:** `Analysis/Parsing/SafeXml` (used for `.slnx`, and for `.csproj`/`.props` from
  ANALYSIS-003); entity-expansion, external-entity and oversized documents are rejected with a
  diagnostic (tests in `SolutionParserTests`).
- JSON/TOML/YAML: depth and size limits; YAML anchors/aliases limited (alias bombs).
- Roslyn: syntax trees and `CSharpCompilation` only; metadata references read from SDK reference
  packs or existing cache files; **never** `MSBuildWorkspace`, never load analyzers/generators,
  never `Assembly.Load*` of repository/package assemblies.
- Do not spawn processes. (Exception requires an ADR; none exists.)

### Git
- LibGit2Sharp adapter. Never run hooks; never invoke `git` CLI without the hardening of
  GIT_INTEGRATION.md §3 and an ADR.
- No submodule recursion; accept only `https://` clone URLs in the MVP (`file://`, `ssh://`, `git://`, `http://`,
  scp-like and local paths are rejected before they reach libgit2, whose SSH transport starts the system
  `ssh` — GIT-006). Fetch, checkout and delete only in app-managed clones below the clones directory
  (enforced by the cloner); a cloned repository is analyzed like any other untrusted folder.

### Storage
- Database file in the user data directory with user-only permissions where the OS supports it
  (implemented in STORE-003: data and backup directories 0700, database files 0600 on Unix; Windows relies on
  the per-user profile ACL; verified by a Storage test on Linux/macOS in CI).
- No secrets in tables. Remote URLs sanitized.

### AI
- See AI_ARCHITECTURE.md §5. Provider gate is mandatory for every model call.
- Non-loopback endpoints are remote and require consent.

### Desktop / Graph
- WebView loads bundled assets only; navigation and new windows blocked; CSP enforced;
  messages schema-validated. Verified in spike GRAPH-001 (Windows): CSP blocks `fetch` and remote
  images, `NavigationStarted.Cancel` and `NewWindowRequested.Handled` block navigation and new
  windows. The WebView2 user data folder lives under the app data directory; DevTools are disabled
  in release builds; the legacy WebView1/EdgeHTML engine is never used. Implemented in GRAPH-004
  (`GraphHostControl`, `GraphViewNavigationPolicy`, `GraphViewBridge`): inbound messages are limited
  to 1,000,000 characters, validated against the current graph and ignored (logged without content)
  when invalid.
- "Open in editor" uses a user-configured command template with the file path passed as a single
  argument (no shell interpolation); only paths inside the repository are allowed.

## 4. Secrets

- Git personal access tokens and cloud AI API keys: **open decision OPD-03** (OS credential store
  vs. library vs. no persistence). Until decided: tokens are held in memory for the duration of
  the operation only.
- Source snippets (UI-007, `GetSourceSnippet`) are read only through `IRepositoryFileReader` (containment, 2 MB limit), only for files of the stored model (other paths are rejected), shown as plain text and never logged or sent anywhere.
- Secrets are never logged; logging uses redaction for known patterns. **Implemented (CORE-007):**
  `LogRedactor` masks every file-log entry (message and exception text) with `***` for credentials in
  URLs (`https://user:token@host`), `Authorization`/`Proxy-Authorization` values, secret parameters
  (`access_token`, `refresh_token`, `token`, `api_key`/`apikey`, `client_secret`, `secret`, `password`,
  `passwd`, `pwd`) and GitHub (`ghp_…`, `github_pat_…`), GitLab (`glpat-…`) and `sk-…` keys; entries are
  cut at 8,000 characters so that no large text such as file content can end up in a log. Redaction is a
  safety net — code still must not log secrets or file contents. The log directory is user-only (0700 on Unix).

## 5. Privacy

- No telemetry in the MVP (OPD-06 decides post-MVP opt-in telemetry; it would never include
  repository content, paths or names).
- Users can delete a repository's data (cascade delete) and the clone directory from the UI.
- Export files are written where the user chooses; default location is outside the analyzed repository.

## 6. Verification

| Check | Where |
|---|---|
| Hostile fixtures asserting that marker files are never created and no child process is spawned | .NET: MSBuild `Exec`, property functions, imported targets, analyzer/generator references, module initializer — **implemented (QA-003)**: fixture `dotnet-hostile`, `HostileDotNetFixtureTests` with the `ZeroExecutionHarness` (marker check + static process-API guard over the modules, TEST_STRATEGY.md §5.1) · npm — **implemented (ANALYSIS-009)**: fixture `node-hostile` (lifecycle scripts, `.npmrc` `script-shell`, node-gyp `binding.gyp` action, `file:` dependency with `postinstall`, module with a top-level side effect, TypeScript language-service plugin), `HostileNodeFixtureTests` · `setup.py` (ANALYSIS-011, M4) · Git — **implemented (GIT-001)**: builder-created repositories whose `.git/config` defines `core.fsmonitor`, `core.sshCommand`, `core.pager`, filter/diff drivers, a credential helper and `remote.*.uploadpack`, plus hooks (`GitRepositoryReaderTests.HostileConfigurationAndHooks_ExecuteNothingAndChangeNothing`; GIT-006 covers commit/checkout too); the process-API guard runs over the Git module |
| Path traversal / symlink escape | Analysis.Tests (REPO-001) |
| XML bombs, deep JSON | Analysis.Tests (ANALYSIS-003; ANALYSIS-009: `SafeJsonTests` depth limit, `NodeProjectSystemAnalyzerTests` deep/oversized/invalid manifests) |
| No network with AI disabled | IntegrationTests with a guarded `HttpMessageHandler` (AI-001) |
| WebView CSP/navigation | `GraphViewNavigationPolicyTests`, `GraphViewBridgeTests` (inbound validation), `GraphViewAssetsTests` (CSP); runtime checked manually on Windows (GRAPH-004) |

## 7. Reporting

Security issues are reported privately to the maintainer (contact defined at public release, OPD-01).
