#!/usr/bin/env bash
set -euo pipefail

TAG=${1:?"Usage: $0 vX.Y.Z"}
[[ "$TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Invalid release tag: $TAG" >&2; exit 1; }

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=${TAG#v}
CHANGES=$(mktemp)
trap 'rm -f "$CHANGES"' EXIT

awk -v version="$VERSION" '
  index($0, "## [" version "]") == 1 { found = 1; next }
  found && /^## \[/ { exit }
  found && /^\[[^]]+\]: / { exit }
  found {
    if ($0 == "### Added") print "### ✨ Features"
    else if ($0 == "### Changed") print "### 🔧 Improvements"
    else if ($0 == "### Fixed") print "### 🐛 Bug fixes"
    else if ($0 == "### Security") print "### 🔒 Security"
    else print
  }
  END { if (!found) exit 1 }
' "$ROOT/CHANGELOG.md" > "$CHANGES" || {
  echo "Missing CHANGELOG.md section for $TAG" >&2
  exit 1
}
grep -q '^### ' "$CHANGES" || { echo "No categorized changes for $TAG" >&2; exit 1; }

{
  echo "## $TAG · Codex Pet HUD"
  echo
  echo "## What's changed"
  cat "$CHANGES"
  cat <<'NOTES'
Download the archive for your platform and verify it with `SHA256SUMS.txt`.

> [!WARNING]
> These preview builds are not Developer ID notarized or Authenticode signed. macOS Gatekeeper or Windows SmartScreen may display an unknown-developer warning. Download only from this official repository.

The live usage integration reads the existing Codex access token in memory and calls an undocumented internal endpoint. Tokens, logs, and Codex data are never included in release assets.

See the [full changelog](https://github.com/himomohi/codex-pet-hud/blob/main/CHANGELOG.md) and [project website](https://himomohi.github.io/codex-pet-hud/).
NOTES
}
