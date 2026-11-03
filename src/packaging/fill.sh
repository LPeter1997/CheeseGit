#!/bin/sh
# Fills the Flatpak manifest, the AUR PKGBUILD and the winget manifests with a version and the checksums of
# the release files in <dir>, and writes them to <dir>/manifests. package.sh runs it on one machine's
# output; the release workflow runs it again once every architecture's files are together. A file that is
# missing keeps its @SHA256_…@ placeholder, and that is reported.
#   packaging/fill.sh <version> <dir>
set -e
here=$(cd "$(dirname "$0")" && pwd)
version=$1
dir=$2
[ -n "$version" ] && [ -d "$dir" ] || { echo "usage: fill.sh <version> <dir>" >&2; exit 2; }
sha() {
    if [ -e "$dir/$1" ]; then
        if command -v sha256sum > /dev/null; then sha256sum "$dir/$1" | cut -d' ' -f1; else shasum -a 256 "$dir/$1" | cut -d' ' -f1; fi
    fi
}
linux_x64=$(sha "cheesegit-$version-linux-x64.tar.gz")
linux_arm64=$(sha "cheesegit-$version-linux-arm64.tar.gz")
# winget wants upper-case hex.
win_x64=$(sha CheeseGit-win-x64-Setup.exe | tr a-f A-F)
win_arm64=$(sha CheeseGit-win-arm64-Setup.exe | tr a-f A-F)

fill() { # <template> <output>
    sed -e "s|@VERSION@|$version|g" -e "s|@DATE@|$(date -u +%Y-%m-%d)|g" \
        -e "${linux_x64:+s|@SHA256_X64@|$linux_x64|g}" -e "${linux_arm64:+s|@SHA256_ARM64@|$linux_arm64|g}" \
        -e "${win_x64:+s|@SHA256_WIN_X64@|$win_x64|g}" -e "${win_arm64:+s|@SHA256_WIN_ARM64@|$win_arm64|g}" \
        "$1" > "$2"
    if grep -q '@SHA256_' "$2"; then echo "$(basename "$2"): some checksums are missing (their files are not in $dir)"; fi
}
out="$dir/manifests"
mkdir -p "$out/winget"
fill "$here/flatpak/io.github.LPeter1997.CheeseGit.yml.in" "$out/io.github.LPeter1997.CheeseGit.yml"
fill "$here/aur/PKGBUILD.in" "$out/PKGBUILD"
for f in "$here"/winget/*.yaml.in; do
    fill "$f" "$out/winget/$(basename "$f" .in)"
done
