# User Workflows — Codebase Explorer

> Status: **accepted (target behavior)** · Last reviewed: 2026-11-17
> These workflows describe the **intended** behavior. None of them is implemented yet unless the
> status line of the workflow says otherwise.

Each workflow lists the trigger, steps, expected system behavior, edge cases and the MVP
capabilities ([MVP_SCOPE.md](MVP_SCOPE.md)) it depends on.

---

## WF-01 Import a local repository

**Persona:** Nina (onboarding) · **Capabilities:** MVP-01, MVP-02, MVP-03 · **Status:** core flow (folder picker → progress with stages and cancel → overview → explorer) implemented (UI-002; no Git detection yet), *Recent repositories* with open (no re-analysis), re-analyze and remove implemented (REPO-005), overview page with *Open Architecture Explorer* implemented (UI-004; the app now navigates there after an import), *Clone from URL* and Git detection in M5

1. Nina starts Codebase Explorer. The start page shows *Open local folder*, *Clone from URL* and
   *Recent repositories*.
2. She chooses *Open local folder* and selects the repository root in the native folder picker.
3. The app validates the folder (exists, readable, not a system directory) and shows whether it
   is a Git repository.
4. An analysis run starts. A progress panel shows stages (Scanning files → Detecting projects →
   Analyzing code → Linking → Saving) with counts and a *Cancel* button.
5. On completion the app navigates to the repository overview (languages, projects, external
   dependencies, warnings) with a prominent *Open Architecture Explorer* action.

**Edge cases**

- Folder contains no recognizable projects → overview shows file/language statistics and a hint;
  the explorer shows a folder-level graph.
- Analysis cancelled → partial results are discarded; the previous successful run (if any)
  remains visible.
- Analyzer fails for one project → run finishes as *partially succeeded*; failed items are listed
  with diagnostics; the rest of the model is usable.
- Very large repository → the user is informed that the graph starts aggregated.

## WF-02 Clone and import a remote repository

**Persona:** Max (consultant) · **Capabilities:** MVP-01, MVP-11 · **Status:** planned (M5)

1. Max chooses *Clone from URL* and pastes `https://github.com/org/repo.git` or a GitLab URL.
2. Optional: branch name; for private repositories a personal access token (stored according to
   OPD-03; until decided, the token is used for this clone only and not persisted).
3. The app clones into its clone directory (`<data>/clones/<host>/<owner>/<repo>`), showing
   transfer progress, with cancellation.
4. After cloning, WF-01 continues from step 4.

**Edge cases:** invalid URL, authentication failure, network loss (resumable? — no, restart),
repository already cloned (offer *update* = fetch + checkout of the selected branch, or *re-clone*).

## WF-03 Explore the architecture graph

**Persona:** all · **Capabilities:** MVP-09, MVP-10 · **Status:** project-level graph with a selection panel (UI-003, M1) and layout switching (GRAPH-005) implemented; drill-down, filters, search planned (M3–M4)

1. The explorer opens at the **project level**: one node per project/package, grouped by
   ecosystem or solution folder; edges are aggregated project dependencies with counts.
2. The user pans/zooms, switches layout (hierarchical / force-directed), and filters by language,
   relationship kind and evidence level.
3. Double-click on a project expands it into **modules** (namespaces, folders, Python packages);
   double-click on a module expands it into **entities** (types, functions). Collapse reverses it.
4. Selecting a node or edge opens the **Component Inspector** (WF-04).
5. Breadcrumbs show the current focus path; *Back* restores the previous view state.

**Rules:** The graph never renders more nodes than the node budget (default 500, configurable).
If an expansion would exceed the budget, the app offers a filtered or neighborhood view instead.

## WF-04 Inspect a component and its evidence

**Persona:** Tom (tech lead) · **Capabilities:** MVP-07, MVP-10 · **Status:** step 4 implemented for graph edges in the explorer side panel (UI-007: evidence list, read-only snippet ± 5 lines, *Copy path*); the inspector (steps 2, 3, 5) follows with UI-006; Git activity M5, *Open in editor* M8

1. Tom selects the node `Billing.Application` (project).
2. The inspector shows: kind, ecosystem, language, path, target frameworks, size metrics,
   fan-in/fan-out, external packages, Git activity.
3. Tabs list incoming and outgoing relationships grouped by kind with evidence level badges
   (Confirmed / Syntactic / Heuristic).
4. Selecting a relationship shows its evidence records: analyzer, rule, file path, line range and
   a read-only snippet. *Open in editor* opens the file at that line in the configured external editor.
5. *Show in graph* focuses the related element.

## WF-05 Find a component and its neighborhood

**Persona:** Nina · **Capabilities:** MVP-09 · **Status:** planned (M4)

1. Nina presses `Ctrl/Cmd+K` and types "InvoiceService".
2. Results show matching entities, modules and projects with their container path.
3. Choosing a result opens a **neighborhood view**: the element plus incoming/outgoing
   relationships up to depth *n* (default 1, adjustable 1–3), aggregated at the chosen level.

## WF-06 Assess the impact of a change

**Persona:** Tom · **Capabilities:** MVP-09/10 (direct dependents); full impact analysis post-MVP · **Status:** planned

1. Tom selects `Shared.Contracts`.
2. MVP: the inspector lists direct dependents and the neighborhood view shows them.
3. Post-MVP: *Impact analysis* computes transitive dependents and highlights them with the
   dependency paths that explain why each one is affected.

## WF-07 Understand change activity

**Persona:** Ana (architect) · **Capabilities:** MVP-11 · **Status:** planned (M5)

1. Ana enables the *Git activity* overlay in the explorer.
2. Node color/size reflects commit count within a selectable window (e.g. last 90 days, bounded by
   the analyzed history depth).
3. The inspector shows last change date, commit count and distinct author count for the selection.

## WF-08 Export architecture documentation

**Persona:** Ana · **Capabilities:** MVP-12 · **Status:** planned (M6)

1. Ana chooses *Export → Markdown report*.
2. She selects scope (whole repository / current view), sections, and a target folder outside the
   analyzed repository by default.
3. The app writes `ARCHITECTURE-<repo>-<date>.md` with Mermaid diagrams and analysis metadata
   (commit, run date, analyzer versions, evidence statistics).

## WF-09 Ask the local AI about the codebase

**Persona:** Nina · **Capabilities:** MVP-13 · **Status:** planned (M7)

1. AI is disabled by default. Nina enables it in Settings → AI, selects the *Ollama* provider; the
   app checks the local endpoint and lists installed models.
2. In the AI panel she asks: "Where is authentication handled and which projects depend on it?"
3. The app retrieves relevant analysis facts and source snippets locally, builds a prompt, and
   streams the answer.
4. Every claim that refers to code carries citations (`path:startLine-endLine`). Citations are
   validated against the stored model; invalid ones are marked as *unverified*.
5. Clicking a citation focuses the element in the graph or opens the snippet in the inspector.
6. The answer is labeled *AI-generated*. Nothing in the repository is changed.

## WF-10 Re-analyze after changes

**Persona:** Tom · **Capabilities:** MVP-01, MVP-08 · **Status:** *Re-analyze* on the start page (REPO-005; full run) and the run history page (UI-005) implemented; commit hashes in M5; incremental post-MVP

1. Tom pulls new commits in his own Git client and clicks *Re-analyze*.
2. MVP: a full analysis run replaces the stored model; the run history shows both runs with
   statistics (M2) and commit hashes (from M5).
3. Post-MVP: only changed files are re-analyzed; the app highlights added/removed relationships.

## WF-11 Configure the application

**Status:** partially implemented — theme switch (System/Light/Dark) and display of the local data
directory exist on the Settings page; the theme choice is stored and restored at startup (STORE-005).

Settings areas (target): Appearance (theme), Storage (data directory, cache cleanup), Analysis
(limits, excludes), Git (clone directory, history depth), AI (provider, model, cloud opt-in),
External editor (command template).
