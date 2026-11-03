# CheeseGit

A free, cross-platform (Windows, macOS, Linux X11/Wayland) git GUI client: .NET 10 + Avalonia 12 + Native AOT, as a
single process. It replaces the original Tauri + React app, which broke down on real repositories. That app is gone
from the tree; its last state is commit `5764ed0` (`git show 5764ed0:src-tauri/tests/git_provider.rs` and the like) if
its behaviour or look needs checking. Its users still run 0.6.2, which is why release versions must be above it.
The Phase 0 `spikes/` were removed too (last state: commit `20067c1`), and the rewrite plan and per-phase results were
folded into the current docs (last state: `docs/` at commit `19e73a8`).

Read these first. They hold the design, the reasons behind it and the measurements:
- `docs/architecture.md`: projects, the git engine rules and why, state and refresh, graph, diff view, askpass, UI.
- `docs/features.md`: how every screen and flow behaves (staging, diffs incl. images and LFS, history, branches, remotes, conflicts, stash).
- `docs/testing.md`: the suites, config profiles, fixtures, the graph corpus, large-repository tests, snapshots.
- `docs/performance.md`: measurements and package sizes.
- `docs/release.md`: packages per OS, updates, migration from the old app, CI, how to make a release.
- `docs/roadmap.md`: what is not verified yet (Windows, macOS, CI), known limitations, what comes next.

## Where things are

| Path | What | Status |
|---|---|---|
| `src/` | The app (`CheeseGit.slnx`) | `CheeseGit.Core` (engine: `GitRunner`, parsers, `GitRepository`, `RepositorySession`, graph), `CheeseGit.Core.Tests`, `CheeseGit.Fixtures` (fixture repos), `CheeseGit.Cli` (`cheesegit-core` harness), `CheeseGit.App` (Avalonia app: `ViewModels/`, `Views/`, custom-drawn `Controls/` such as `DiffView`), `CheeseGit.App.Tests` (headless view-model tests against real repos), `CheeseGit.IconGen` (build-time tool: `Assets/icon.png` → the Windows `.ico`; not in the solution, built by the app). |
| `src/packaging/` | Package metadata | Linux desktop file, AppStream metainfo, nfpm (deb/rpm), Flatpak, AUR and winget templates; `fill.sh` fills in versions and checksums. |
| `.github/workflows/` | CI | Tests on three OSes with the size budget (`ci.yml`); the release (`release.yml`). Releases go to GitHub, which the updater reads. |
| `docs/` | Documentation | Describes the app as it is: keep it up to date when facts change, and record new measurements, decisions and open items in the matching file rather than as dated history. `docs/screenshots/` holds the README's images. |

**Current state:** every feature in `docs/features.md` is built and tested on Linux, and the Windows installer was
checked by hand. Packaging, in-app updates and the CI workflows are built. Open: CI has never run, Windows and macOS test
runs, macOS packaging, signing, store submissions, and moving old-app users over (see `docs/roadmap.md`).

## Commands

Prerequisites: .NET 10 SDK and git on PATH (the minimum supported git is 2.34).

```sh
cd src
dotnet build
dotnet test                                                     # engine and app tests, "hostile" config profile
CHEESEGIT_TEST_PROFILE=crlf dotnet test                         # one other profile (see TestEnvironment.cs)
./test-matrix.sh                                                # both suites, every profile; required before committing engine changes
CHEESEGIT_LARGE_TESTS=1 [CHEESEGIT_LARGE_REPO=<repo>] dotnet test CheeseGit.App.Tests --filter LargeRepoTests --logger "console;verbosity=detailed"
CHEESEGIT_GRAPH_REVIEW=<dir> dotnet test CheeseGit.App.Tests --filter GraphCorpusImageTests   # labelled graph corpus renders
CHEESEGIT_UPDATE_GRAPH_CORPUS=1 dotnet test CheeseGit.App.Tests --filter GraphCorpusImageTests # rewrite the reviewed images (review first)
dotnet publish CheeseGit.Cli -c Release -r linux-x64 -o <out>   # Native AOT harness
<out>/cheesegit-core status|log|graph|watch|bench <repo>
<out>/cheesegit-core fixture <kind> <dir> [-n commits]          # e.g. HostilePaths, PausedMerge, LargeHistory
dotnet publish CheeseGit.App -c Release -r linux-x64 -o <out>   # Native AOT app
CHEESEGIT_SNAPSHOT=<file.png> <out>/CheeseGit <repo>
./package.sh <version>                                          # Linux: AppImage (needs squashfs-tools), tarball, deb, rpm; macOS: pkg, zip, dmg
.\package.ps1 -Version <v> [-Rid win-x64]                        # Windows only (AOT): Setup.exe, Portable.zip, .nupkg
./check-size.sh <publish-dir> <rid>                             # AOT publish folder against size-budget.txt
```

App environment variables: `CHEESEGIT_SNAPSHOT=<png>` makes the app render itself to a PNG once the repository is
loaded, then exit (settings stay in memory, so your tabs are untouched). With it: `CHEESEGIT_SNAPSHOT_FILE=<path>` selects a
changed file to show its diff, `CHEESEGIT_SNAPSHOT_THEME=Light|Dark|High contrast`, `CHEESEGIT_SNAPSHOT_SPLIT=1`,
`CHEESEGIT_SNAPSHOT_LOG=1` opens the command log, `CHEESEGIT_SNAPSHOT_DIALOG=settings|about|create|clone|confirm|branches|delete-branch|remotes|add-remote|switch-changes|askpass|askpass-host|remote-progress|blocking|error-copied|conflicts|cherry-pick|update|update-notice|whats-new`,
`CHEESEGIT_SNAPSHOT_STASH=<n>` opens the Stash tab and selects entry n (likewise with `CHEESEGIT_SNAPSHOT_FILE`),
`CHEESEGIT_SNAPSHOT_HISTORY=<row>` opens the history and selects that row (`-1`: none; `CHEESEGIT_SNAPSHOT_FILE` then picks
a file in the commit), `CHEESEGIT_SNAPSHOT_SEARCH=<text>` searches it (with `branches`, it is typed into the branch search). Popups are overlaid in snapshot mode, so flyouts show.
`CHEESEGIT_SNAPSHOT_DIALOG=update-on-exit` with `CHEESEGIT_UPDATE_SOURCE=<folder or URL of vpk output>` runs the real updater
(check, download, apply after exit) without a window; in snapshot mode an explicit `CHEESEGIT_CONFIG_DIR` keeps settings there
(so What's new carries to the next run). `CHEESEGIT_UPDATE_SOURCE` also works in normal runs, to try an update before publishing.
`CHEESEGIT_WAYLAND=1` selects native Wayland (also a setting). `CHEESEGIT_CONFIG_DIR` overrides the settings folder.

For realistic numbers, benchmark against a large repo, e.g. a clone of `https://github.com/git/git`
(~86k commits) in a scratch directory, never inside this repo.

## Rules for the git layer

These rules fix the old app's flakiness. Each one exists because of a concrete failure (see `docs/architecture.md`).
- Every git call goes through `GitRunner`. It pins `LC_ALL=C`, overrides presentation config with `-c`
  (`color.ui`, `core.quotepath`, `diff.noprefix`, …) and sets `GIT_OPTIONAL_LOCKS=0` for reads
  (`GitAccess.Read`). Diff commands also get `--no-ext-diff`.
- Decide success and state from **exit codes and repo state**, never from git's human-readable messages.
  Stderr is for display only.
- Use NUL-separated output (`-z`, `%x00`). Treat paths as **bytes**; decode only for display.
- git matches each pathspec against each index entry: 15k pathspecs took 37 s. Long path lists go through
  `PathspecCover`, which replaces fully selected directories with the directory.
- Refs: use full names (`refs/heads/x`) after `--end-of-options`. Paths: pass them through stdin
  (`--pathspec-from-file=- --pathspec-file-nul`, `--index-info`, `cat-file`), never as a long command line.
  .NET encodes argv as UTF-8, so a path that is not valid UTF-8 cannot be an argument: where a command has no
  stdin form, use `GitPath.ToPathspec()` (a `:(glob)` pattern, `literalPathspecs: false`) and filter by bytes.
- Find git-dir files with `rev-parse --git-path X`. `.git` is not always a directory (worktrees, submodules).
- Load history topology with `rev-list --topo-order --parents --timestamp --stdin`. **Not** `git log --format`,
  which is 5× slower because it parses every commit object.
- Read commit metadata through the persistent `cat-file` process, on a background thread. Never run git on the UI thread.
- Every new parser gets tests against real git in a temp repo with a hostile user config (see `TempRepo` in the tests),
  and new repository shapes go into `CheeseGit.Fixtures` so `FixtureTests` checks them under every profile.
- Repository state reaches the UI only through `RepositorySession` snapshots. Mutations go through
  `session.RunAsync`, which serializes them with refreshes; never refresh by polling or copy state into view models.
- Tests run in parallel: never change the process environment in a test (use `TempRepo.RawWithEnv`).
- Credentials never go through files or arguments: git and ssh ask through the askpass bridge (`Askpass`,
  `AskpassServer`). Both executables call `Askpass.TryRunHelper(args)` first thing in `Main`; keep it there.
- `GIT_LITERAL_PATHSPECS=1` (set by default) breaks `stash push --include-untracked`: untracked files are stored but
  left in the tree. Commands without pathspecs that hit this run with `literalPathspecs: false`.

## Rules for the graph

- `LayoutValidator` must pass for every layout. Every parent edge is drawn or explicitly stubbed; nothing is dropped.
- Commits on the current branch's first-parent chain always sit in lane 0, and so do their edges.
- Hiding branches means filtering the DAG by reachability, then laying out again. Never use ownership heuristics.
- A layout change that alters a `GraphCorpus` image needs the new render reviewed (labelled, via
  `CHEESEGIT_GRAPH_REVIEW`) before the image is replaced. New tricky shapes go into `GraphCorpus.Cases`.

## Avalonia 12 and Native AOT

- Avalonia 12 APIs differ from 11 (e.g. `WindowDecorations`, `WindowDecorationProperties.ElementRole` for the
  custom title bar). Check the package's XML docs under `~/.nuget/packages/avalonia/<ver>/ref/` instead of
  guessing from v11 knowledge.
- Compiled bindings only (`x:DataType`). Prefer source-generated libraries such as CommunityToolkit.Mvvm and
  `System.Text.Json` source generation. **Zero AOT/trim warnings.** Verify with a real `dotnet publish`,
  not just a build.
- Theme colours are mutable brushes in the application resources (`ThemeManager`). Reference them with
  `DynamicResource` so theme switches cross-fade.
- Outline icons are `c:Icon` (stroked 16×16 geometries from `App.axaml`), which follow the button's foreground. Not
  `Path` with `$parent[ContentPresenter]`: `$parent` walks the logical tree and left icons outside data templates invisible.
- Mono text uses `Fonts.Mono` (resolved against installed fonts). A comma-separated `FontFamily` fallback list drew nothing.

## Verifying UI changes

Run the app with `CHEESEGIT_SNAPSHOT` and look at the PNG. **Do not capture the desktop** (e.g. `spectacle -a`):
the user works on this machine, and a capture grabs whatever window is focused. For the same reason, never run the app
without `CHEESEGIT_SNAPSHOT`: a normal run puts a window on the user's screen. Behaviour goes in `CheeseGit.App.Tests`.

A locked screen stops the compositor's frames: the PNG then shows a stale frame (e.g. "Opening repository…")
whatever the app did. Check `loginctl show-session <id> -p LockedHint` before blaming the code.

Snapshots render through the compositor (`CreateCompositionVisualSnapshot`), like the screen. `RenderTargetBitmap`
mishandled clips in custom-drawn controls (diff text vanished or was drawn scaled).

## Code style

C#, nullable enabled, file-scoped namespaces, `this.` qualification, records for data, spans and bytes on
hot paths. Comments explain *why* (the measurement or failure behind a choice), not what.

## Git workflow

Work on the `rewrite` branch. Commit locally after each verified step. **Never push.**
