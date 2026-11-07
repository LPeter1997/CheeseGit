# Packaging, updates and releases

## Decisions

- **Releases live on GitHub** (`github.com/LPeter1997/CheeseGit`): the in-app updater reads them, and GitHub Actions
  has runners for every RID. The working remote is Codeberg; GitHub gets `master` for CI and releases.
- **Versions must be above 0.6.2**, the old Tauri app's last release in the same repository. Velopack ignores
  releases without its feed files, but the distro builds' notice compares tags and would offer the old app. Both
  package scripts require a version, and the release workflow rejects anything ≤ 0.6.2.
- **The Velopack channel is the RID** (`win-x64`, `osx-arm64`, `linux-x64`, …): every architecture shares one release
  without file name clashes, and an installed copy keeps updating from its own channel.
- **Linux app id `io.github.LPeter1997.CheeseGit`** for the Flatpak, metainfo, desktop files, icons and the windows'
  WM_CLASS / Wayland app id. The AppImage's own desktop entry keeps the old app's `cheesegit.desktop` name, so it
  replaces that entry.
- **git is not bundled**, except in the Flatpak, whose runtime has none.
- **The AOT compiler has a security floor** (`src/Directory.Build.targets`): an SDK picks the ILCompiler it was built
  with, and that pack also supplies the runtime compiled into the binary. A vulnerability advisory on it fails restore
  (NU1903, warnings are errors), so an SDK older than the fix gets the patched version. Raise the floor for a new
  advisory, or leave it once every SDK in use bundles the fix: it does nothing on those.

## Packages

| OS | Script | Output |
|---|---|---|
| Windows | `package.ps1 -Version x.y.z [-Rid win-arm64]` | `CheeseGit-<rid>-Setup.exe` (per user, no admin), `-Portable.zip`, `.nupkg` (+ delta) |
| macOS | `package.sh x.y.z` | Velopack `.pkg`, portable zip, `.nupkg`, and a `.dmg` with an Applications link |
| Linux | `package.sh x.y.z` | AppImage + `.nupkg` (in-app updates); distro tarball, `.deb`, `.rpm` (nfpm, or its container through podman/docker) |

- Native AOT does not cross-compile between operating systems; Windows needs the MSVC linker ("Desktop development
  with C++"). There is no single `.exe`: Skia, HarfBuzz, ANGLE and oniguruma stay native DLLs next to the binary.
- `PREVIOUS=<dir>` / `-Previous <dir>` (the channel's last release, from `vpk download github`) makes vpk build a delta.
- The distro tarball, `.deb`, `.rpm`, AUR and Flatpak are built with `-p:CheeseGitUpdater=false`, which compiles the
  updater out (`NO_UPDATER`). The packages install to `/usr/lib/cheesegit` with `/usr/bin/cheesegit` as a symlink,
  carry the licence and declare git ≥ 2.34. `desktop-file-validate` and `appstreamcli validate --pedantic` pass.
- `packaging/fill.sh` fills the Flatpak manifest, AUR `PKGBUILD` and winget manifests with the version and checksums.
- **Flatpak:** the distro tarball on `org.freedesktop.Platform` 26.08 with git 2.56 built in (the runtime has ssh,
  ssh-agent and gpg). Home and removable media are readable, plus the ssh-agent socket and gnupg. External merge
  tools and hooks that need host programs do not run inside it.
- **Icons:** `Assets/icon.png` is the only icon in the repository; `CheeseGit.IconGen` writes the `.ico` (16–64 px
  DIBs, 256 px PNG) and `.icns` at build time.

## Updates

| Copy | Mode | Behaviour |
|---|---|---|
| Setup.exe, portable zip, AppImage, macOS package (Velopack) | In-app | Banner: **Update now** (download, apply, restart), **On exit** (download now, apply after the app exits), **Skip** (remembered), × (ask again next start) |
| deb, rpm, AUR, Flatpak | Notice | The banner says a version exists and links to its release page |
| Dev builds, publish folders | None | No checks |

- The updater reads GitHub releases with no token; pre-releases and drafts are not offered.
- The startup check is silent on failure (offline, rate limits) and can be turned off in Settings; "Check for
  Updates" in the options menu reports errors and "up to date".
- Release notes travel with the update (`vpk pack --releaseNotes`; the GitHub release body for the notice), and the
  new version shows them once as **What's new**. Notes for a version that never got installed are dropped.
- `CHEESEGIT_UPDATE_SOURCE=<folder or URL of vpk output>` replaces the GitHub feed, to try an update before publishing.
  A local round trip (Linux AppImage, "On exit") is verified:

  ```sh
  OUT=/tmp/u/v1 ./package.sh 0.7.0                     # the copy to update (AppImage, or install Setup.exe)
  mkdir /tmp/u/prev && cp /tmp/u/v1/releases/{releases.*.json,RELEASES-*,assets.*.json,*-full.nupkg} /tmp/u/prev/
  OUT=/tmp/u/v2 PREVIOUS=/tmp/u/prev NOTES=notes.md ./package.sh 0.7.1
  CHEESEGIT_UPDATE_SOURCE=/tmp/u/v2/releases <installed 0.7.0>   # click Update now or On exit
  ```

## Migration from the old app

- Without a `settings.json`, the old app's `state.json` (Tauri's config folder for `com.cheesegit.desktop`) gives the
  open tabs (missing folders dropped), active tab, last parent folder, theme and merge tool, once.
- The AppImage asks once to add itself to the applications menu (the old app's desktop entry feature).
- The old app's updater reads `latest.json` from the latest release and can only install its own (signed) artifacts.
  The release workflow copies the previous `latest.json` into each new release, so old installs keep finding 0.6.2
  instead of failing to check. They are not moved to the new app (see `roadmap.md`).

## CI

- `.github/workflows/ci.yml`: tests on Ubuntu 22.04, Windows and macOS, and a real AOT publish per OS with the size
  check. Runs on pushes to `master`/`rewrite` and pull requests that touch `src/`.
- `.github/workflows/release.yml` (manual): checks the version, builds the six RIDs on their own runners, fills the
  manifests (kept as an artifact), and creates the release with every file at once. Optional secrets, listed in its
  header, enable macOS signing and notarization, Windows signing and winget updates. Without them, packages are
  unsigned.

## Making a release

1. `release.yml` must be on GitHub's default branch (`master`) for the Run workflow button to appear.
2. Actions → release → Run workflow: a version above the last one, the release notes as base64
   (`base64 -w0 notes.md`), and "pre-release" for a test that installed copies will not pick up.
3. Updates only show up from the second release on. Install a release from GitHub and update it on each OS before
   relying on it.
4. winget: submit the first manifest by hand from the release's `manifests` artifact; with `WINGET_TOKEN`, later
   releases update it.
