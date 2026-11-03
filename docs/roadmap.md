# Status and roadmap

*As of 2026-11-03.* Everything in `features.md` is built and tested on Linux, and the Windows installer was checked by
hand. This file lists what is not done or not verified, the known limitations, and what comes next.

## Not verified yet

- **CI has never run.** Expect fixes on its first run, especially the Windows and macOS tests.
- **Windows and macOS** have not run the test suites or the benchmarks. Specific risks:
  - process spawn cost and `status` time on Windows (git processes there took 100–600 ms each in one field report);
  - the watcher with case-insensitive paths, and the Windows subset of hostile names;
  - askpass: Unix domain sockets need Windows 10 1803+, and Git for Windows sets its own `core.askPass` (the
    environment variable wins);
  - merge tools' terminal start and PATH lookup;
  - the fetch case-collision fix (`architecture.md`) was simulated, not run on Windows.
- **macOS packaging:** `package.sh`'s macOS branch, the `.dmg`, signing and notarization.
- **Updates:** round trips on Windows and macOS, and "Update now" (apply and restart) anywhere.
- **GNOME** (XWayland and native Wayland); only KDE was tested.

## Release work

- Code signing: an Apple Developer ID; a Windows certificate (SignPath, Azure Trusted Signing).
- Store submissions: the first winget manifest, the AUR package, Flathub (needs screenshots in the metainfo and its
  review).
- **Moving old-app users over.** The old updater only installs its own signed artifact types. Options: one last
  Tauri release whose update banner points to the new download, or signing new-app artifacts for its updater
  (AppImage and the macOS bundle could work; Windows runs an NSIS installer with arguments Velopack's Setup does not
  take). Undecided.

## Known limitations

- An untracked file whose name is not valid UTF-8 cannot be opened by .NET, so its diff is empty.
- Paths with a newline cannot be shown by `ShowFileAsync`: `cat-file --batch -z` needs git 2.36, the minimum is 2.34.
- Syntax highlighting tokenizes each hunk on its own; a hunk that starts inside a block comment is coloured wrongly.
  Reading both full files would fix it.
- .NET's recursive `FileSystemWatcher` adds an inotify watch for every directory, ignored ones included. If
  `max_user_watches` becomes a problem, write a watcher that skips ignored directories (the session already falls
  back to polling when watching fails).
- Every ref change reloads the whole history topology in the background.
- LFS: the history's line totals count pointer lines; LFS transfers show no progress (git-lfs works in hooks and
  filters; `GIT_LFS_PROGRESS` could report it); text files in LFS show the summary, not a text diff.
- Images: pixel count is not bounded (a 32 MB PNG can decode to gigabytes; read the header and use
  `Bitmap.DecodeToWidth`); TIFF, HEIC, AVIF and PSD have no Skia codec.
- Flatpak: external merge tools and hooks that need host programs do not run.

## Next

- **Incremental history reload.** New commits can only come before the loaded ones; prepending them and dropping
  what is no longer reachable would make a reload after a commit nearly free. Check that the layout stays
  deterministic.
- **Non-text diffs:**
  1. An info card for every other binary file: sizes, the type sniffed from magic bytes, **Open** each version in
     the system's app, **Save as…**.
  2. Image comparison modes for same-size images (swipe first, then onion skin and difference), zoom and pan.
  3. Submodules: the old and new commit, the commits between them, dirty state, open as a tab.
  4. Symlinks ("Symlink → target") and type changes as cards.
  5. An opt-in read-only textconv view for files with a `diff.<driver>.textconv`.
  6. SVG rendered (Svg.Skia, after checking AOT warnings and the size budget).
  7. ZIP-based archives as a table of entries (`System.IO.Compression`).
  Rendering PDF, Office files, fonts, audio or video is not planned; Open covers them.
- **Keyboard:** shortcuts to switch and close tabs, fetch, and focus the summary box.
- **Clone** starts with an empty parent folder until one has been used.
- **Native Wayland by default** once GNOME is tested: it was faster than XWayland.
- **Features, by value:** amend, tags, file history and blame, interactive rebase, submodule and worktree awareness,
  hunk staging from history, signing indicators, hide whitespace (`-w`), LFS locks and prune.
