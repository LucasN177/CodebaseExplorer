# Git Integration — Codebase Explorer

> Status: **accepted (specification)** · Last reviewed: 2026-11-17
> Implementation status: **not implemented** (module seam `AddGitModule()` exists).

## 1. Scope

| In MVP | Post-MVP |
|---|---|
| Detect whether a folder is a Git working tree (incl. worktrees and `.git` files) | Architecture snapshots per commit and comparison |
| HEAD commit, branch / detached state, dirty state, remotes (sanitized) | Branch comparison |
| Bounded history walk: per-file commit count, distinct author count, first/last change, lines added/deleted | Change coupling (files changed together) |
| Clone GitHub/GitLab (and generic) remotes over **HTTPS**, public or with personal access token | SSH (OPD-02), OAuth device flow, API browsing |
| Fetch + checkout of a selected branch for repositories cloned by the app | Pull/merge for user-managed repositories (never — the user's own Git client owns their working trees) |

## 2. Ports (Application)

```csharp
// Application/Git — illustrative, finalized in GIT-001
public interface IGitRepositoryReader
{
    Task<GitRepositoryInfo?> TryOpenAsync(string path, CancellationToken ct);           // null if not a repo
    Task<GitSnapshotInfo> ReadSnapshotAsync(string path, CancellationToken ct);         // HEAD, branch, remotes, dirty
    Task<IReadOnlyList<GitFileActivity>> ReadFileActivityAsync(
        string path, GitHistoryWindow window, IProgress<GitProgress>? progress, CancellationToken ct);
}

public interface IGitRemoteCloner
{
    Task<CloneResult> CloneAsync(CloneRequest request, IProgress<GitProgress>? progress, CancellationToken ct);
    Task<CloneResult> UpdateAsync(string localPath, string branch, IProgress<GitProgress>? progress, CancellationToken ct);
}
```

`GitHistoryWindow` bounds the walk by maximum commit count (default 5,000) and/or since-date
(default: 365 days). Rename following is not performed in the MVP.

## 3. Adapter choice

**Initial adapter: LibGit2Sharp** (0.32.0 verified on nuget.org; targets net8.0, usable from net10.0).

Reasons:

- In-process, no dependency on an installed `git` executable.
- **libgit2 does not execute hooks, `core.fsmonitor`, credential helpers or other
  repository-configured commands** — important because imported local repositories are untrusted
  (SECURITY.md). This property must be re-confirmed in GIT-006.
- Mature read APIs for commits, trees, diffs and refs.

Known limitations / verification items (task GIT-006):

| Item | Status |
|---|---|
| SSH transport | Not supported by LibGit2Sharp's bundled libgit2 build (to be re-verified). → OPD-02 |
| Native binaries for win-x64, osx-arm64, linux-x64 | Provided via LibGit2Sharp.NativeBinaries (to be verified for all target RIDs, incl. glibc/musl expectations on Linux) |
| Shallow clone (`depth`) support | To be verified; if unavailable, full clones are used and documented |
| Performance of per-file history statistics on large histories | To be measured (QA-002); fallback: walk commits once and accumulate diffs per file in a single pass |
| Credentials | Token passed via credentials callback; never written to `.git/config` or remote URLs |

**Possible second adapter: Git CLI** (only if OPD-02 decides for SSH). If used, every invocation
must pass hardening options: `-c core.hooksPath=<empty dir>`, `-c core.fsmonitor=false`,
`-c protocol.file.allow=never`, `GIT_TERMINAL_PROMPT=0`, no `--recurse-submodules`, and only on
repositories cloned by the app or explicitly trusted by the user. This adapter requires its own ADR.

## 4. Remote URLs and providers

- Accepted URL forms: `https://<host>/<owner>/<repo>(.git)`; GitHub (`github.com`), GitLab
  (`gitlab.com` and self-hosted hosts entered by the user). Other HTTPS Git hosts are accepted as
  `OtherGitRemote` without provider-specific behavior.
- `file://`, `ssh://`, `git://` and scp-like URLs are rejected in the MVP.
- Credentials embedded in URLs (`https://user:token@host/...`) are stripped before storage and
  logging; the user is warned.
- Clone target: `<data>/clones/<host>/<owner>/<repo>`; if it exists, the user chooses update or
  re-clone.
- **Submodules are not cloned** in the MVP (security and size); their presence is reported.
- LFS pointers are analyzed as small text files; LFS content is not downloaded.

## 5. Data flow

```mermaid
sequenceDiagram
    participant UC as ImportRepository use case
    participant Clone as IGitRemoteCloner
    participant Read as IGitRepositoryReader
    participant An as IRepositoryAnalyzer
    participant St as Storage

    opt remote URL
        UC->>Clone: CloneAsync(url, target, token?)
    end
    UC->>Read: TryOpenAsync(path)
    alt is Git repository
        UC->>Read: ReadSnapshotAsync(path)
        UC->>An: AnalyzeAsync(...)
        UC->>Read: ReadFileActivityAsync(path, window)
        UC->>St: Save run + model + GitSnapshot + GitChanges
    else not a Git repository
        UC->>An: AnalyzeAsync(...)
        UC->>St: Save run + model (no Git data)
    end
```

Git activity is mapped to `Files` by relative path and aggregated to projects/modules at query time.

## 6. Privacy

- Author names and e-mails are read transiently to count distinct authors and are **not stored**
  in the MVP.
- Remote URLs are stored sanitized.
- Tokens are never logged or persisted until OPD-03 (secret storage) is decided.

## 7. Testing

- `GitRepositoryBuilder` test helper (task GIT-002) creates temporary repositories with scripted
  commits, branches and renames — no committed nested `.git` folders.
- Clone tests use a local bare repository served via the `file://` transport **inside tests only**
  (the product rejects `file://`), or are marked as network tests excluded from default runs.
- Hostile-config fixture: a repository whose `.git/config` defines `core.fsmonitor` and hooks;
  the test asserts that reading metadata executes nothing (marker file is never created).
