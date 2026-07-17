#!/usr/bin/env bash
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
cd "$ROOT"

while IFS= read -r -d '' path; do
  case "$path" in
    .env|*/.env|*.p12|*.mobileprovision|*.provisionprofile|*.key)
      echo "Forbidden sensitive file: $path" >&2
      exit 1
      ;;
  esac
done < <(git ls-files -z)

if git grep -I -n -E \
  '(sk-[A-Za-z0-9_-]{20,}|OPENAI_API_KEY[[:space:]]*=[[:space:]]*[^[:space:]]+|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----)' \
  -- . ':(exclude)scripts/release/scan-secrets.sh'; then
  echo "Potential secret found in tracked source" >&2
  exit 1
fi

echo "Tracked source secret scan passed"
