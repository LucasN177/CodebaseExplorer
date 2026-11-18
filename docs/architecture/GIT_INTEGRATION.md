# Git Integration — Codebase Explorer

> Status: **accepted (specification)** · Last reviewed: 2026-11-18
> Implementation status: **in progress** — adapter verified (GIT-006, §3.1).

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
// Application/Git — implemented (GIT-001): IGitRepositoryReader, GitRepositoryInfo, GitSnapshotInfo,
// GitRemoteInfo, GitRemoteUrls, GitErrorCodes; adapter Git/LibGit2/LibGit2RepositoryReader.
public interface IGitRepositoryReader
{
    // Discovers the working tree containing the folder (the folder or a parent; linked worktrees and .git files).
    // git.not-a-repository (NotFound) for plain folders and bare repositories, git.read-failed (Failure) otherwise.
    Task<Result<GitRepositoryInfo>> OpenAsync(string path, CancellationToken ct);
    // HEAD (null if unborn), branch (null if detached), uncommitted changes (incl. untracked, not ignored), remotes.
    Task<Result<GitSnapshotInfo>> ReadSnapshotAsync(GitRepositoryInfo repository, CancellationToken ct);
    // Illustrative, finalized in GIT-004:
    Task<IReadOnlyList<GitFileActivity>> ReadFileActivityAsync(
        GitRepositoryInfo repository, GitHistoryWindow window, IProgress<GitProgress>? progress, CancellationToken ct);
}

// GitRepositoryInfo(RootPath, WorkingDirectory, RootPrefix, IsLinkedWorktree, IsShallow): RootPrefix is the analyzed
// folder relative to the working tree ("" if equal) — Git paths are mapped to the model's files through it.

// Illustrative, finalized in GIT-003:
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

### 3.1 Verification results (spike GIT-006, 2026-11-18)

Verified with LibGit2Sharp **0.32.0** (MIT) and LibGit2Sharp.NativeBinaries **2.0.324** (libgit2 commit `5853918`,
GPL-2.0 with linking exception) on Windows 11 x64; proof code: `LibGit2SharpVerificationTests` (IntegrationTests,
runs on all three CI OSes, so native loading is verified there too).

| Item | Verified fact |
|---|---|
| Native binaries | The package ships `runtimes/{win-x64, win-arm64, win-x86, osx-x64, osx-arm64, linux-x64, linux-arm64, linux-arm, linux-musl-x64, linux-musl-arm64, linux-musl-arm, linux-ppc64le}/native` — all target RIDs, glibc and musl. Loading verified locally on win-x64; macOS/Linux through the CI test run. |
| Built-in features | `Threads, Https, Ssh, NSec`. |
| SSH transport | **Correction:** the native build *does* support SSH — not with libssh2, but by **starting the system `ssh` executable** (an `ssh://` clone attempt failed with OpenSSH's own "Host key verification failed." message). This means libgit2 spawns a process for SSH URLs. The product therefore rejects `ssh://` and scp-like URLs before they reach libgit2 (§4) and never fetches in user-managed repositories. OPD-02 stays open; its options are unchanged, but "LibGit2Sharp without SSH" now means "SSH URLs rejected by the app", and an SSH option could use this built-in path instead of a Git CLI adapter. |
| Hooks, `core.fsmonitor`, filter/diff drivers, `credential.helper`, `core.sshCommand` | **Not executed** by open, status, refs/remotes, commit walk, diff, commit, branch checkout and path checkout, with all of them configured to write marker files (`LocalOperations_RunNoHookFsmonitorOrFilterDriver`). libgit2 has no credential-helper support at all; credentials come only from the callback. |
| HTTPS clone (public) | Works for GitHub (`libgit2/libgit2sharp`) and GitLab (`gitlab.com`), verified manually 2026-11-18; automated as the opt-in network test `ShallowHttpsClone_OfPublicRepository`. |
| HTTPS clone with token | Supported via `FetchOptions.CredentialsProvider` (`UsernamePasswordCredentials`); the token never appears in `.git/config`. **Not verified against a private repository** (no test token available) — opt-in test `HttpsClone_WithToken_UsesTheCredentialsCallbackOnly` reads `CODEBASE_EXPLORER_TEST_GIT_URL`/`…_TOKEN`. |
| Shallow clone | `FetchOptions.Depth` works over HTTPS (`Repository.Info.IsShallow`); the **local transport rejects it** ("shallow fetch is not supported by the local transport"). **The product clones full history:** a shallow clone would make the activity statistics (§2 `GitHistoryWindow`) meaningless. |
| History walk performance | `libgit2/libgit2sharp` (2,837 commits, 2,952 paths, Release build, Windows 11, local SSD): commit walk with tree-to-tree changes only **1.4 s**; with per-file line counts (`Patch`, no rename detection) **26.5 s**; with libgit2's default rename detection 32 s; `git log --numstat` for comparison 6.3 s. Line counting dominates. Consequence for GIT-004: single pass over the window, no rename detection (matches `git log --no-renames --numstat` exactly; renames count as delete + add), diffs against the first parent, merge commits skipped, the walk split across threads with one `Repository` instance each. The benchmark on the performance fixture follows with QA-002. |
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
