# Testing

What broke the previous app was real-world data, so the tests run against real git in real repositories, under
hostile configurations. Commands are in `CLAUDE.md`.

## Suites

- **`CheeseGit.Core.Tests`**: the engine against real git in temp repositories (`TempRepo`). Includes the ported
  behaviour specs of the old app's Rust tests (git provider, merge, revert, conflict resolution, remote branches, undo,
  partial patches), with deliberate differences commented in the tests.
- **`CheeseGit.App.Tests`**: the view models and views, headless (Avalonia.Headless), over real repositories, with
  real pointer and keyboard input where it matters (`LayoutTests`, `FileListInputTests`). Ports of the old e2e
  specs live here (`ConflictTests`, `StashTests`, `CherryPickTests`, …), plus `DailyUseTests`.

Tests run in parallel, so **no test changes the process environment**: every git child would inherit it (this once
produced a flaky `GIT_DIR` race). Use per-call environment (`TempRepo.RawWithEnv`).

The App tests use `AvaloniaTestIsolationLevel.PerAssembly`: recreating the Application per test failed about 1 run in
10 (the new compositor found the render loop owned by the previous dispatcher's thread).

## Config profiles

`CHEESEGIT_TEST_PROFILE` selects a hostile git configuration (`TestEnvironment.cs`); `test-matrix.sh` runs both suites
under every profile, each also under the `de_DE.UTF-8` locale:

| Profile | What it sets |
|---|---|
| `default` | Nothing |
| `hostile` (the default for `dotnet test`) | Colour forced on, `diff.noprefix`, `diff.relative`, `log.showSignature`, `status.short`, `core.pager=less`, `init.defaultBranch=trunk`, ~30 more |
| `crlf`, `crlf-input` | `core.autocrlf=true` / `input` |
| `merge` | `merge.conflictStyle=zdiff3`, `rerere.enabled`, `merge.ff=false` |
| `status` | `status.showUntrackedFiles=no`, `status.renames=false`, `diff.renames=copies`, `core.untrackedCache` |

## Fixtures

`FixtureGenerator` (`CheeseGit.Fixtures`, also `cheesegit-core fixture <kind> <dir>`) builds the shapes that break
git clients, with `git fast-import`, an isolated config, fixed identities and dates, so they are byte-identical under
every profile:
- hostile paths: unicode, spaces, quotes, tab, newline, leading `-`, `:(top)`-like names, Latin-1 and invalid UTF-8
  names (Linux), and every kind of working-tree change;
- line endings (CRLF, LF, mixed, CR-only, no final newline) and Latin-1 and UTF-16 content;
- binaries, type changes, an executable bit change, a symlink;
- a dirty submodule, a linked worktree, LFS pointers, a shallow clone, a detached HEAD;
- octopus, criss-cross and second-root merges, with branch names that clash with files and tags, look like options
  or look like remotes;
- a paused merge (content, modify/delete, add/add and CRLF conflicts);
- 1,000 branches with 200 tags and 300 remote branches; a 100k-commit history; a 100k-file worktree (`LargeWorktree`).

`FixtureTests` runs every fixture through invariants that compare two independent readings of the same state: status
against per-file diffs, numstat against the parsed patch, `rev-list --count` against the loaded DAG (also with each
branch hidden in turn) plus the layout validator, `for-each-ref` against the snapshot, and a stage-all / unstage-all
round trip. New repository shapes go here.

## Graph corpus

`GraphCorpus` (`CheeseGit.Fixtures`) builds small repositories for the tricky shapes: linear, a merged feature,
octopus, criss-cross, orphan roots, ten open branches, a 160-row edge (stubs), hidden merges and nested merges (#59),
repeated merge-backs, a detached HEAD on a hidden branch, local-only commits, branches stacked on main.
- `GraphCorpusTests` (Core) loads each through the engine: the layout validates, the rows are exactly `rev-list` over
  the visible branches, and main's first-parent chain is in lane 0.
- `GraphCorpusImageTests` (App) draws each with `GraphCell.DrawRow` and compares with the reviewed images in
  `CheeseGit.App.Tests/GraphCorpus/` (no text, small tolerance for anti-aliasing). A layout change that alters an
  image needs the labelled render (`CHEESEGIT_GRAPH_REVIEW=<dir>`) reviewed before the image is replaced
  (`CHEESEGIT_UPDATE_GRAPH_CORPUS=1`).

## Large repositories

`LargeRepoTests` (opt-in, `CHEESEGIT_LARGE_TESTS=1`) drives the real view models through the daily loop and the
history on the large fixtures, or on a clone of a real repository (`CHEESEGIT_LARGE_REPO`), and prints each step's
time and every git call over 50 ms. Results are in `performance.md`.

## Snapshots

The app renders itself to a PNG with `CHEESEGIT_SNAPSHOT` (options in `CLAUDE.md`). Snapshots go through the
compositor, like the screen: `RenderTargetBitmap` mishandled clips in custom-drawn controls. Popups go into the
overlay layer in snapshot mode, so flyouts appear. Use them to check UI changes; behaviour goes in the App tests.

## Known traps

- A syntax test that tokenized the first line through a newly loaded grammar sometimes exceeded its 50 ms budget
  under the matrix's load; the tests warm the grammar up first.
- `git fast-import` costs ~100 µs per commit: the 100k fixture takes ~14 s, so read-only tests share a cached copy.
