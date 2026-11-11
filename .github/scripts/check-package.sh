#!/usr/bin/env bash
# Checks the packed library before anyone installs it. Used by ci.yml and release.yml.
# Usage: check-package.sh <directory with the .nupkg>
set -euo pipefail

dir="${1:-./artifacts}"
pkg=$(ls "$dir"/Hlibz.McpDocs.AspNetCore.[0-9]*.nupkg)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

fail() { echo "::error::$1"; exit 1; }

# The README and icon nuget.org shows: at the package root and referenced by the nuspec,
# since dropping PackageIcon or PackageReadmeFile still packs.
unzip -l "$pkg" > "$work/contents.txt"
cat "$work/contents.txt"
grep -qE " README\.md$" "$work/contents.txt" || fail "README.md is not at the package root."
grep -qE " icon\.png$" "$work/contents.txt" || fail "icon.png is not at the package root."
unzip -p "$pkg" '*.nuspec' > "$work/package.nuspec"
grep -q "<readme>README.md</readme>" "$work/package.nuspec" || fail "The nuspec does not reference README.md."
grep -q "<icon>icon.png</icon>" "$work/package.nuspec" || fail "The nuspec does not reference icon.png."

# The page is the package: an EmbeddedResource that stops matching its file still packs, and
# then every request for the page or its script fails.
unzip -p "$pkg" 'lib/*/Hlibz.McpDocs.AspNetCore.dll' > "$work/lib.dll"
for asset in index.html app.js app.css print.css; do
  grep -aq "McpDocs.$asset" "$work/lib.dll" || fail "McpDocs.$asset is not embedded in the assembly."
done

echo "Package checks passed: $(basename "$pkg")"
