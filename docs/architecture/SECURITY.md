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
| User machine | Native parser crash (Tree-sitter, libgit2) taking down the app | Input limits; crash containment strategy evaluated in ANALYSIS-008 (in-process with limits vs. out-of-process parser host). |
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
- No submodule recursion; reject `file://`, `ssh://`, `git://` URLs in the MVP.

### Storage
- Database file in the user data directory with user-only permissions where the OS supports it.
- No secrets in tables. Remote URLs sanitized.

### AI
- See AI_ARCHITECTURE.md §5. Provider gate is mandatory for every model call.
- Non-loopback endpoints are remote and require consent.

### Desktop / Graph
- WebView loads bundled assets only; navigation and new windows blocked; CSP enforced;
  messages schema-validated.
- "Open in editor" uses a user-configured command template with the file path passed as a single
  argument (no shell interpolation); only paths inside the repository are allowed.

## 4. Secrets

- Git personal access tokens and cloud AI API keys: **open decision OPD-03** (OS credential store
  vs. library vs. no persistence). Until decided: tokens are held in memory for the duration of
  the operation only.
- Secrets are never logged; logging uses redaction for known patterns (tokens in URLs,
  `Authorization` headers).

## 5. Privacy

- No telemetry in the MVP (OPD-06 decides post-MVP opt-in telemetry; it would never include
  repository content, paths or names).
- Users can delete a repository's data (cascade delete) and the clone directory from the UI.
- Export files are written where the user chooses; default location is outside the analyzed repository.

## 6. Verification

| Check | Where |
|---|---|
| Hostile fixtures asserting that marker files are never created and no child process is spawned | .NET: MSBuild `Exec`, property functions, analyzer packages (QA-003, M3) · npm lifecycle scripts (ANALYSIS-009, M4) · `setup.py` (ANALYSIS-011, M4) · Git hooks and `core.fsmonitor` (GIT-001, M5) |
| Path traversal / symlink escape | Analysis.Tests (REPO-001) |
| XML bombs, deep JSON | Analysis.Tests (ANALYSIS-003, ANALYSIS-009) |
| No network with AI disabled | IntegrationTests with a guarded `HttpMessageHandler` (AI-001) |
| WebView CSP/navigation | Manual checklist + GRAPH-004 tests where automatable |

## 7. Reporting

Security issues are reported privately to the maintainer (contact defined at public release, OPD-01).
