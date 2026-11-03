# Architecture

CheeseGit is one Native AOT process: .NET 10, Avalonia 12, and the system git (2.34 or newer) driven through its
command line. There is no libgit2: LibGit2Sharp is unproven under Native AOT, and the CLI keeps everything the user
configured working (identity, signing, hooks, credential helpers, `core.autocrlf`, filters, LFS).

The design exists because the previous app (Tauri + React, up to 0.6.2) broke on real repositories for three
structural reasons: the UI kept its own copy of the repository state, refreshed by polling and patched with optimistic
updates; the git layer parsed output that the user's config and locale could change; and the graph assigned commits to
branches with a heuristic that dropped commits and edges. Each section below says what replaced that.

## Projects

| Project | What it is |
|---|---|
| `CheeseGit.Core` | The engine, with no UI dependency. `Git/`: `GitRunner`, parsers, `CatFileProcess`, `GitPath`, askpass. `Repository/`: `GitRepository` (every git operation), `RepositorySession`, `HistoryView`, `PathspecCover`, the watcher. `Graph/`: the commit DAG, its loader, `GraphLayout`, `LayoutValidator`. `Diff/`: diff parser, line patches, inline highlights. `Platform/`: merge tools. |
| `CheeseGit.App` | The Avalonia app. `ViewModels/` (CommunityToolkit.Mvvm), `Views/`, custom-drawn `Controls/` (`DiffView`, `GraphCell`, …), `Syntax/` (TextMate), `Theming/`, `Services/` (settings, updates, desktop entry). |
| `CheeseGit.Core.Tests`, `CheeseGit.App.Tests` | Tests against real git in temp repositories; see `testing.md`. |
| `CheeseGit.Fixtures` | Deterministic fixture repositories and the graph corpus. |
| `CheeseGit.Cli` | `cheesegit-core`: a Native AOT harness (`status`, `log`, `graph`, `watch`, `bench`, `fixture`). |
| `CheeseGit.IconGen` | Build-time tool: `Assets/icon.png` → `.ico` and `.icns`. Not in the solution; the app's build runs it. |

## Git engine

**Every git call goes through `GitRunner`.** It:
- pins `LC_ALL=C` and removes environment variables that redirect git (`GIT_DIR`, `GIT_INDEX_FILE`,
  `GIT_EXTERNAL_DIFF`, …);
- overrides presentation config with `-c`: every `color.*` off, `core.quotepath=false`, `diff.noprefix=false`,
  `diff.mnemonicPrefix=false`, `diff.relative=false`, `log.showSignature=false`, `status.relativePaths=false`,
  `core.pager=cat`, and more. Diff commands also get `--no-ext-diff --no-textconv`;
- sets `GIT_OPTIONAL_LOCKS=0` for reads (`GitAccess.Read`), so background reads never take `index.lock`;
- sets `GIT_TERMINAL_PROMPT=0`, and `GIT_ASKPASS`/`SSH_ASKPASS` to the app itself (see "Askpass");
- sets `GIT_LITERAL_PATHSPECS=1` by default (`literalPathspecs: false` turns it off per call);
- streams stdout and stderr, can be cancelled (kills the process tree), and records every call in the command log
  with its operation, exit code and duration.

**Success and state come from exit codes and repository state, never from message text.** After a merge, the engine
reads `MERGE_HEAD` (through `rev-parse --git-path`) and the unmerged index entries. Failures are typed
(`GitErrorCode.NotFullyMerged`, `AlreadyPushed`, …). Stderr is for display only.

**Bytes, not strings.** Output is NUL-separated (`-z`, `%x00`) and parsed as bytes. Paths are opaque bytes with a
display form; file contents are `byte[]`.

**Arguments.** Refs are full names (`refs/heads/x`) after `--end-of-options`, so a branch named like a file or an
option is never ambiguous. Path lists go through stdin (`--pathspec-from-file=- --pathspec-file-nul`,
`update-index --index-info`, `checkout-index --stdin`, `cat-file`), never on the command line. .NET encodes argv as
UTF-8, so a path that is not valid UTF-8 cannot be an argument at all: where a command has no stdin form (`diff`,
`diff-tree`, `ls-files`), `GitPath.ToPathspec()` builds a `:(glob)` pattern with `?` for each invalid byte and the
result is filtered by exact bytes.

**Long path lists.** git matches each pathspec against each index entry: `git add` with 15,101 pathspecs took 37 s in
a 100k-file tree, against 0.4 s for `.`. Lists over 100 paths go through `PathspecCover`, which replaces a directory's
paths with the directory when every changed path under it (rename sources included) is selected, so the command's
effect is unchanged.

**git-dir files** are found with `rev-parse --git-path X`; `.git` is a file in worktrees and submodules. LFS objects
live in the *common* git dir.

**Key queries.**

| Need | Command |
|---|---|
| Status, branch, upstream, ahead/behind, stash count | `status --porcelain=v2 -z --branch --show-stash` |
| History topology | `rev-list --topo-order --parents --timestamp --stdin`, refs on stdin. Not `git log --format`, which parses every commit object: 1,281 ms against 248 ms on git/git. |
| Commit metadata | The persistent `cat-file --batch` process, on a background thread: 0.06 ms a commit, against 4.7 ms for a `git show` per commit |
| Line totals | `diff-tree --stdin`, one process per batch, only for rows on screen |
| Refs | `for-each-ref` with full names |
| Accept both | `merge-file --union` on the index stages (keeps CRLF, the final newline and the encoding) |
| Merge conflict prediction | `merge-tree --write-tree` (git 2.38+) |
| Merge message | `fmt-merge-msg`, as `git merge` uses inside, so `merge.log` and friends stay git's |

Newer git features are detected and gated: `merge-tree --write-tree` (2.38), `check-attr --source` (2.40),
`fetch --prune --dry-run --porcelain` (2.41).

**Commit-graph.** When a history has 20k commits or more and git would use a commit-graph but none exists, the app
offers once per repository to run `git commit-graph write --reachable --changed-paths`. It made the topology load 4–5×
faster (850 → 180 ms on 100k commits).

## State and refresh

- **`RepositorySession` is the only owner of a repository's state.** It holds an immutable `RepoSnapshot` (status,
  HEAD, refs, operation state, stashes, remotes). `SnapshotChanged` fires only when content differs, with the parts
  that changed. View models read snapshots; they never keep a second copy to patch.
- **One serialized queue.** Mutations (`session.RunAsync`) and refreshes share a queue, so a stale refresh can never
  publish after a mutation; a failing mutation still refreshes. A mutation cancels a running background refresh and
  re-reads its parts afterwards (a slow `status` once held up a fetch for 112 s). `RunAsync(blocking: true)` is for
  operations that rewrite the working tree: the repository view refuses input until the session has refreshed and
  the history has laid out the new graph (the dimming fades in after 250 ms, so quick operations do not flash).
- **Targeted invalidation.** A git-dir watcher maps `HEAD`, `index`, `refs/**`, `packed-refs`, `config`, the operation
  state files and the stash reflog to snapshot parts; lock files, `objects/`, `logs/` and `FETCH_HEAD` are ignored. A
  worktree watcher debounces for 300 ms (measured from the first event, so a steady stream still refreshes) and drops
  events under ignored directories. git's own writes cause no further git calls. Polling (every 30 s) is only the
  fallback when watching fails.
- **No optimistic updates.** Operations are fast enough to show a pending state instead.

## Graph

- **Data.** The full topology is loaded once into compact arrays (integer ids, parent lists). Every ref or HEAD change
  reloads it in the background; the list keeps the previous state until the new one arrives.
- **Layout** (`GraphLayout`) draws every commit and every parent edge. Commits on the current branch's first-parent
  chain always sit in lane 0, and so do their edges; the main chain never borrows another lane. Edges into the same
  parent share a lane. Lanes slide one column left when a gap opens (as `git log --graph` does).
- **Long edges become arrow stubs** when they would hold a lane open for more than 150 rows (as gitk does). Drawn
  literally, git/git needs 157 parallel lanes; with stubs and compaction, a median of 6.
- **`LayoutValidator`** checks every layout: every parent edge is drawn or explicitly stubbed, nothing is dropped.
- **Hiding branches** (`HistoryView`) filters the DAG to the commits reachable from the visible branches (plus a
  detached HEAD) and lays it out again. No ownership heuristics, and no git calls. The current branch and the local
  branches whose tips lie on its first-parent chain are required: hiding them would hide nothing.
- **Colours.** The default branch (the local branch for the remote's `HEAD`, else that remote branch, else a local
  `main`/`master`) is colour 0 wherever its first-parent chain is drawn, walked in the full history so shared commits
  keep it when it is hidden. The checked-out branch's own commits get a colour of their own. Chain ids wrap instead of
  overflowing.
- **Local-only commits** (reachable from local branches, not from the chosen remote) get a hollow, dashed dot.
- **Drawing.** `GraphCell` draws each row from the layout's segments; the list virtualizes rows; the graph column is
  as wide as the widest lane on screen. Hovering a dot fades everything but its lane chain.

## Diff view

- The diff comes from git as a structured model (hunks, lines, byte-exact content, EOL markers). `--full-index`
  names both blobs, which the image and LFS views read (`ReadDiffSideAsync`).
- `DiffView` is custom-drawn and virtualized, unified or split, with line selection for staging and search. A
  50k-line diff loads in ~0.6 s.
- **Line staging** builds a partial patch (`PartialPatch`): within a change block, selected additions go after the
  last selected deletion, so staging, unstaging and discarding the same lines agree.
- **Syntax highlighting** uses TextMateSharp with the full VS Code grammar set (only 0.8 MB compressed over a curated
  set), mapped to 12 theme categories so colours follow the theme. Grammars load in ~150 ms and tokenize at
  0.2–0.4 ms a line, so diffs tokenize on a background thread and the view repaints every 50 ms. Above 10,000 lines
  there is no colour. Each hunk is tokenized on its own, per side, so a hunk that starts inside a block comment is
  coloured wrongly. Some grammars are patched on load (`PatchedGrammars`; XML comments).

## Askpass

git and ssh ask for credentials, passphrases and host keys in the app's dialogs:
- The app's own executable is `GIT_ASKPASS` and `SSH_ASKPASS` (`SSH_ASKPASS_REQUIRE=force`, OpenSSH 8.4+). Both
  executables call `Askpass.TryRunHelper(args)` first thing in `Main`: started as a helper, it answers before Avalonia
  or Velopack start (41 ms, no window).
- The helper sends the prompt over a Unix domain socket (Windows 10 1803+ has them) in a folder only the user can
  enter (`$XDG_RUNTIME_DIR`, else the temp folder), and must present a random 32-byte token from its environment.
  Socket paths are limited to ~100 bytes.
- The prompt text is the only input, so its kind is read from it: "Username…" is shown as typed, "(yes/no" is a
  host-key question, anything else is a secret.
- Credential helpers (GCM, libsecret, osxkeychain) still answer first. Nothing goes through files or arguments.
  "Remember until CheeseGit closes" keeps answers in memory, and forgets them when an operation using them fails.

## UI

- Compiled bindings only (`x:DataType`), source-generated libraries, zero AOT/trim warnings.
- Theme colours are mutable brushes in the application resources (`ThemeManager`), so a theme switch cross-fades.
  Themes: light, dark, high contrast, or follow the system.
- Frameless window with a custom title bar.
- Every list virtualizes: file lists (100k untracked files), branch lists (1,000+ refs), history, stash.
- The command log is a bottom panel opened from the status bar, grouped by operation: each user action is a row
  (✓/✗, time, label, repository, call count, duration) that expands to its git calls (command, exit code, duration,
  stderr). Background work folds into one muted row per label ("Refresh ×14"). An operation's ✗ comes from the code
  that ran it, not from exit codes, since many calls answer with a non-zero exit on purpose. Filter, errors only,
  export and copy.
- Native Wayland is opt-in (setting or `CHEESEGIT_WAYLAND=1`): it worked fully on KDE and scrolled faster than
  XWayland, but Avalonia still marks it experimental.
- Dates are ISO (`2020-09-13`) past 30 days: the app runs with invariant globalization, where the short date is US.
