#!/bin/sh
# Compares an AOT publish folder with its budget in size-budget.txt and fails when it is over.
#   ./check-size.sh <publish-dir> <rid>
set -e
cd "$(dirname "$0")"
dir=$1
rid=$2
[ -d "$dir" ] && [ -n "$rid" ] || { echo "usage: check-size.sh <publish-dir> <rid>" >&2; exit 2; }
# du -k works on GNU, BSD (macOS) and Git Bash alike.
kb=$(du -sk "$dir" | cut -f1)
mb=$(LC_ALL=C awk -v k="$kb" 'BEGIN { printf "%.1f", k * 1024 / 1000000 }')
budget=$(awk -v r="$rid" '$1 == r { print $2 }' size-budget.txt)
if [ -z "$budget" ]; then
    echo "$rid: $mb MB (no budget yet; add a line to size-budget.txt)"
    exit 0
fi
echo "$rid: $mb MB of $budget MB"
LC_ALL=C awk -v s="$mb" -v b="$budget" 'BEGIN { exit !(s > b) }' && { echo "::error::$rid publish folder is $mb MB, over its $budget MB budget"; exit 1; }
exit 0
