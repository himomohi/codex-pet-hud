#!/usr/bin/env bash
set -euo pipefail

TAG=${1:?'Usage: verify-release.sh vX.Y.Z'}
[[ "$TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Invalid release tag: $TAG" >&2; exit 1; }

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=${TAG#v}
SECTION=$(mktemp)
NOTES=$(mktemp)
trap 'rm -f "$SECTION" "$NOTES"' EXIT

[[ $(grep -Ec "^## \[$VERSION\] - [0-9]{4}-[0-9]{2}-[0-9]{2}$" "$ROOT/CHANGELOG.md") -eq 1 ]] || {
  echo "CHANGELOG.md must contain exactly one dated section for $TAG" >&2
  exit 1
}

awk -v version="$VERSION" '
  index($0, "## [" version "] - ") == 1 { found = 1; next }
  found && /^## \[/ { exit }
  found { print }
' "$ROOT/CHANGELOG.md" > "$SECTION"

awk '
  /^### / {
    if ($0 !~ /^### (Added|Changed|Fixed|Security)$/) {
      print "Unsupported CHANGELOG category: " $0 > "/dev/stderr"
      exit 1
    }
    if (heading && !items) {
      print "Empty CHANGELOG category: " heading > "/dev/stderr"
      exit 1
    }
    heading = $0
    items = 0
    categories++
    next
  }
  /^- / {
    if (!heading) {
      print "CHANGELOG item appears before a category" > "/dev/stderr"
      exit 1
    }
    items++
  }
  END {
    if (!categories) {
      print "No release categories found" > "/dev/stderr"
      exit 1
    }
    if (!items) {
      print "Empty CHANGELOG category: " heading > "/dev/stderr"
      exit 1
    }
  }
' "$SECTION"

grep -Fqx "[Unreleased]: https://github.com/himomohi/codex-pet-hud/compare/$TAG...HEAD" "$ROOT/CHANGELOG.md" || {
  echo "The Unreleased comparison link must start at $TAG" >&2
  exit 1
}

"$ROOT/scripts/release/build-notes.sh" "$TAG" > "$NOTES"
grep -Fqx "## $TAG · Codex Pet HUD" "$NOTES"
grep -Fqx "## What's changed" "$NOTES"

node --check "$ROOT/site/assets/site.js"
grep -Fq '/releases/latest' "$ROOT/site/assets/site.js"
if grep -Eq 'v?[0-9]+\.[0-9]+\.[0-9]+' "$ROOT/site/index.html" "$ROOT/site/assets/site.js"; then
  echo "The website must not hardcode a release version" >&2
  exit 1
fi

echo "Release contract verified for $TAG"
