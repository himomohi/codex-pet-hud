#!/bin/bash
set -euo pipefail

DIR="${1:-dist}"
cd "$DIR"
shopt -s nullglob
archives=(Codex-Pet-HUD-v*.zip)
[[ ${#archives[@]} -eq 3 ]] || { echo "Expected exactly 3 release archives, found ${#archives[@]}" >&2; exit 1; }

for archive in "${archives[@]}"; do
  listing="$(unzip -Z1 "$archive")"
  if grep -Eiq '(^|/)(\.git|\.codex|auth\.json|\.env|logs?|bin|obj)(/|$)|\.(p12|cer|mobileprovision)$|/Users/' <<<"$listing"; then
    echo "Forbidden release content in $archive" >&2
    exit 1
  fi
done

sha256sum "${archives[@]}" > SHA256SUMS.txt
sha256sum -c SHA256SUMS.txt
