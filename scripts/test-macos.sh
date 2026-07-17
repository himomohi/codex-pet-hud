#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$(mktemp -d -t codex-pet-state-tests)"
SOURCE="$WORK/main.swift"
BINARY="$WORK/tests"
trap 'rm -f "$SOURCE" "$BINARY"; rmdir "$WORK"' EXIT

awk '1' \
  "$ROOT/platforms/macos/CodexPetLimitRings.swift" \
  "$ROOT/platforms/macos/tests/main.swift" \
  > "$SOURCE"

xcrun --sdk macosx swiftc \
  -D UNIT_TESTING \
  "$SOURCE" \
  -framework AppKit \
  -framework WebKit \
  -lsqlite3 \
  -o "$BINARY"

"$BINARY" \
  "$ROOT/shared/fixtures/pet-visible.json" \
  "$ROOT/shared/fixtures/pet-visible-current.json"
