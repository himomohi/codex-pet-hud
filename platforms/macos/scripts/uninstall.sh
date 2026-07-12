#!/bin/zsh
set -euo pipefail

LABEL="io.github.himomohi.codex-pet-hud"
LEGACY_LABEL="com.appcaster.codex-pet-limit-rings"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
MAC_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
LOG_DIR="$MAC_ROOT/logs"
ACTIVE_PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
LEGACY_PLIST="$HOME/Library/LaunchAgents/$LEGACY_LABEL.plist"
uid="$(id -u)"

launchctl bootout "gui/$uid" "$ACTIVE_PLIST" 2>/dev/null || true
launchctl bootout "gui/$uid/$LABEL" 2>/dev/null || true
launchctl bootout "gui/$uid" "$LEGACY_PLIST" 2>/dev/null || true
launchctl bootout "gui/$uid/$LEGACY_LABEL" 2>/dev/null || true
rm -f "$ACTIVE_PLIST" "$LEGACY_PLIST"
rm -f "$MAC_ROOT/bin/CodexPetLimitRings"
rm -rf "$MAC_ROOT/bin/CodexPetLimitRings.app" "$MAC_ROOT/bin/settings"

if [[ "${1:-}" == "--logs" ]]; then
  rm -rf "$LOG_DIR"
fi

echo "Uninstalled $LABEL"
