# Performance

Measurements on Linux x86-64 (KDE Plasma), git 2.56, .NET 10, Avalonia 12.1, unless noted. Windows and macOS have
not been measured. For realistic numbers, use a clone of `git/git` (~86k commits) or the large fixtures, through
`cheesegit-core bench` or `LargeRepoTests` (see `testing.md`).

## Engine

| Query | Time |
|---|---|
| process spawn (`rev-parse HEAD`) | 2.2 ms |
| open a repository | 7 ms |
| `status --porcelain=v2` (warm) | 6–10 ms |
| `for-each-ref`, ~1,000 refs | 3–23 ms |
| commit metadata through the persistent `cat-file` | 0.06 ms a commit (a `git show` per commit: 4.7 ms) |
| history topology, 100k commits | 974 ms; **232 ms with a commit-graph** |
| layout, 100k commits | 15 ms |
| metadata for the first 100 rows | 5 ms |

## Daily loop on large working trees

`LargeRepoTests`, headless, JIT. `LargeWorktree`: 100k tracked files, 10k modified, 5k untracked, 100 deleted.
git/git: every tracked text file edited plus 5,000 new files.

| Step | LargeWorktree (15,101 changes) | git/git (9,771 changes) |
|---|---|---|
| open until the lists are filled | 365 ms | 196 ms |
| diff stats (background) | 472 ms | 1.2 s |
| filter on and off | 13 ms | 15 ms |
| select every row | 16 ms | 14 ms |
| 50k-line diff: load / rows | 569 ms / 269 ms | – |
| stage all | 2.1 s (37.7 s before `PathspecCover`) | 1.6 s |
| unstage all | 1.7 s (31.7 s before) | 2.3 s |
| commit everything / undo | 865 ms / 497 ms | 116 ms / 115 ms |

What remains in stage and unstage is git's own work (hashing in `add`, refreshing the index in `reset`).

## History

| Step | 100k-commit fixture, no commit-graph | git/git clone, no commit-graph |
|---|---|---|
| history shown | 1,213 ms (`rev-list` 925 ms) | 1,351 ms (82,634 commits) |
| first screen's text / line totals | 24 ms / 20 ms | 25 ms / 60 ms |
| show all branches | 167 ms | 2 ms |
| select a commit | 28 ms | 49 ms |
| commit, until the history shows it (background) | 1,110 ms | 1,419 ms |
| check out a commit / jump back | | 167 ms / 236 ms |

A history load runs a fixed number of git calls, whatever the number of branches. It once ran a `show-ref` per
branch, which on a machine where every git process took 100–600 ms (likely Windows with antivirus) came to 8–23 s per
load with ~70 branches.

## App

- First frame: ~1.2 s on a small repository, 1.65 s on `LargeWorktree` (AOT, including opening the repository).
- Scrolling 86k history rows: 60 fps under XWayland (vsync-bound, p95 32 ms when scrolling ~15 screens a second);
  native Wayland averages 5.5 ms a frame. Metadata never loads on the UI thread: that alone was the difference
  between p95 18 and 32 ms.
- Memory: ~150 MB for a small repository, ~200 MB with an 86k-commit history.

## Sizes

linux-x64, as of the first release builds:

| Artifact | Size |
|---|---|
| Publish folder | 55.5 MB (~40 MB `CheeseGit`, 11.2 MB `libSkiaSharp.so`, 2.8 MB `libHarfBuzzSharp.so`) |
| AppImage | 26.5 MB |
| Velopack full `.nupkg` (what the updater downloads) | 25.9 MB |
| Velopack delta for a small change | ~0.1–0.3 MB |
| Distro tarball / `.deb` / `.rpm` | 20.9 / 21.7 / 22.4 MB |
| `cheesegit-core` CLI | 3.4 MB |
| Windows Setup.exe / portable zip (win-x64, measured by hand) | ~31 MB / 23 MB |

`size-budget.txt` holds the budgets (linux-x64: 60 MB publish folder) and `check-size.sh` fails CI over budget; other
RIDs are reported until measured. The full TextMate grammar set costs 7.1 MB of executable but 0.8 MB compressed.
