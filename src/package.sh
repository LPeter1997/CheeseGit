#!/bin/sh
# Builds the release for this machine's OS (Native AOT does not cross-compile between operating systems;
# package.ps1 is the Windows side). Prints the sizes tracked in docs/performance.md.
#   ./package.sh <version>        RID=linux-arm64|osx-x64 OUT=dir NOTES=release-notes.md PREVIOUS=dir
#
# PREVIOUS holds the last release of this channel (vpk download github): vpk then builds a delta package
# against it, so updates download only what changed.
#
# Linux: the Velopack AppImage and .nupkg (in-app updates), then the distro tarball, built with the updater
#   compiled out, and from it the .deb and .rpm (nfpm, or its container through podman/docker), plus the
#   Flatpak manifest and PKGBUILD with this tarball's checksum filled in (packaging/fill.sh).
# macOS: the Velopack .pkg, portable zip and .nupkg, and a .dmg of the app bundle.
#
# Signing is configured through vpk's own environment variables, so no secret passes through here:
# VPK_SIGN_APP_IDENTITY, VPK_SIGN_INSTALL_IDENTITY, VPK_NOTARY_PROFILE (macOS; the .dmg is notarized with the
# same profile). The Velopack channel is the RID, so every architecture can share one GitHub release.
set -e
cd "$(dirname "$0")"
here=$(pwd)
version=$1
# No default: the old Tauri app's releases (up to v0.6.2) share the GitHub repository, so a low version
# would make the distro builds' notice offer the old app as an update.
[ -n "$version" ] || { echo "usage: package.sh <version>   (above 0.6.2, the old app's last release)" >&2; exit 2; }
case "$(uname -s)" in
    Linux) os=linux ;;
    Darwin) os=osx ;;
    *) echo "package.sh runs on Linux and macOS; use package.ps1 on Windows" >&2; exit 1 ;;
esac
case "$(uname -m)" in
    x86_64|amd64) arch=x64 ;;
    aarch64|arm64) arch=arm64 ;;
    *) echo "unknown architecture: $(uname -m)" >&2; exit 1 ;;
esac
rid=${RID:-$os-$arch}
arch=${rid#*-}
out=${OUT:-${TMPDIR:-/tmp}/cheesegit-package}
releases="$out/releases"
rm -rf "$out"
mkdir -p "$releases"
[ -n "$PREVIOUS" ] && cp -R "$PREVIOUS"/. "$releases"/
notes=""
[ -n "$NOTES" ] && notes="--releaseNotes $NOTES"

# awk does the formatting under LC_ALL=C: a locale with a decimal comma breaks printf.
size() { [ -e "$2" ] && du -skc "$2" | tail -1 | LC_ALL=C awk -v n="$1" '{ printf "%-40s %6.1f MB\n", n, $1 * 1024 / 1000000 }'; true; }

publish() { # <dir> [msbuild args]
    dir=$1
    shift
    dotnet publish CheeseGit.App -c Release -r "$rid" -o "$dir" -p:Version="$version" --nologo -v q "$@"
}

dotnet tool restore > /dev/null
publish "$out/publish"
size "publish folder ($rid)" "$out/publish"
size "  CheeseGit (AOT binary)" "$out/publish/CheeseGit"

if [ "$os" = linux ]; then
    # shellcheck disable=SC2086 # $notes is empty or two words
    dotnet vpk pack --packId CheeseGit --packVersion "$version" --packTitle CheeseGit --packAuthors "Peter Lenkefi" \
        --packDir "$out/publish" --mainExe CheeseGit --icon CheeseGit.App/Assets/icon.png --categories Development \
        --runtime "$rid" --channel "$rid" --outputDir "$releases" $notes > "$out/vpk.log" 2>&1 \
        || { cat "$out/vpk.log"; exit 1; }

    # The distro tarball: the system's package manager updates it, so the updater is compiled out.
    id=io.github.LPeter1997.CheeseGit
    top="$out/distro/cheesegit-$version"
    publish "$top/lib" -p:CheeseGitUpdater=false
    mkdir -p "$top/share/applications" "$top/share/metainfo" "$top/share/icons/hicolor/512x512/apps" "$top/share/licenses/cheesegit"
    cp packaging/linux/$id.desktop "$top/share/applications/"
    sed "s|<!-- @RELEASES@ -->|<release version=\"$version\" date=\"$(date -u +%Y-%m-%d)\" />|" \
        packaging/linux/$id.metainfo.xml > "$top/share/metainfo/$id.metainfo.xml"
    cp CheeseGit.App/Assets/icon.png "$top/share/icons/hicolor/512x512/apps/$id.png"
    cp ../LICENSE "$top/share/licenses/cheesegit/"
    tarball="$releases/cheesegit-$version-linux-$arch.tar.gz"
    tar -C "$out/distro" -czf "$tarball" "cheesegit-$version"

    # deb and rpm: nfpm itself, else its container image.
    case "$arch" in x64) nfpm_arch=amd64 ;; *) nfpm_arch=arm64 ;; esac
    nfpm_run() {
        if command -v nfpm > /dev/null; then
            (cd "$top" && NFPM_ARCH=$nfpm_arch VERSION=$version nfpm "$@" --config "$here/packaging/linux/nfpm.yaml")
        elif engine=$(command -v podman || command -v docker); then
            "$engine" run --rm -v "$here/packaging:/packaging:ro,z" -v "$out:$out:z" -w "$top" \
                -e NFPM_ARCH=$nfpm_arch -e VERSION="$version" \
                docker.io/goreleaser/nfpm:latest "$@" --config /packaging/linux/nfpm.yaml
        else
            return 127
        fi
    }
    for format in deb rpm; do
        nfpm_run package --packager $format --target "$releases/" > "$out/nfpm-$format.log" 2>&1 \
            || echo "skipped .$format: no nfpm (or podman/docker), or it failed: $out/nfpm-$format.log"
    done

    packaging/fill.sh "$version" "$releases"

    for f in "$releases"/*.AppImage "$releases"/*-full.nupkg "$tarball" "$releases"/*.deb "$releases"/*.rpm; do
        [ -e "$f" ] && size "$(basename "$f")" "$f"
    done
else
    # shellcheck disable=SC2086
    dotnet vpk pack --packId CheeseGit --packVersion "$version" --packTitle CheeseGit --packAuthors "Peter Lenkefi" \
        --packDir "$out/publish" --mainExe CheeseGit --icon CheeseGit.App/obj/CheeseGit.icns \
        --bundleId io.github.LPeter1997.CheeseGit --runtime "$rid" --channel "$rid" --outputDir "$releases" $notes \
        > "$out/vpk.log" 2>&1 || { cat "$out/vpk.log"; exit 1; }

    # The .dmg holds the same (signed) bundle as the portable zip, with an Applications link to drag it to.
    staging="$out/dmg"
    mkdir -p "$staging"
    ditto -x -k "$releases/CheeseGit-$rid-Portable.zip" "$staging"
    ln -s /Applications "$staging/Applications"
    dmg="$releases/CheeseGit-$version-$rid.dmg"
    hdiutil create -volname CheeseGit -srcfolder "$staging" -ov -format UDZO "$dmg" > /dev/null
    if [ -n "$VPK_SIGN_APP_IDENTITY" ]; then
        codesign --sign "$VPK_SIGN_APP_IDENTITY" --timestamp "$dmg"
    fi
    if [ -n "$VPK_NOTARY_PROFILE" ]; then
        xcrun notarytool submit "$dmg" --keychain-profile "$VPK_NOTARY_PROFILE" --wait
        xcrun stapler staple "$dmg"
    fi

    for f in "$releases"/*.pkg "$releases"/*-Portable.zip "$dmg" "$releases"/*-full.nupkg; do
        [ -e "$f" ] && size "$(basename "$f")" "$f"
    done
fi
echo "output: $releases"
