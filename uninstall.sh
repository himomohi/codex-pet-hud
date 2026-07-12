#!/bin/zsh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
exec "$ROOT/platforms/macos/scripts/uninstall.sh" "$@"
