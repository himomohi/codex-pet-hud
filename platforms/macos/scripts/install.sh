#!/bin/zsh
set -euo pipefail

LABEL="io.github.himomohi.codex-pet-hud"
LEGACY_LABEL="com.appcaster.codex-pet-limit-rings"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
MAC_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$MAC_ROOT/../.." && pwd)"
SOURCE="$MAC_ROOT/CodexPetLimitRings.swift"
SHARED_SETTINGS="$REPO_ROOT/shared/settings"
LOG_DIR="$MAC_ROOT/logs"
ACTIVE_PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
LEGACY_PLIST="$HOME/Library/LaunchAgents/$LEGACY_LABEL.plist"
PLIST_TEMPLATE="$MAC_ROOT/launchagents/$LABEL.plist.in"
BIN="$MAC_ROOT/bin/CodexPetLimitRings"
APP="$MAC_ROOT/bin/CodexPetLimitRings.app"
APP_BIN="$APP/Contents/MacOS/CodexPetLimitRings"
APP_RESOURCES="$APP/Contents/Resources"

if [[ -d "$REPO_ROOT/logs" && ! -e "$LOG_DIR" ]]; then
  mv "$REPO_ROOT/logs" "$LOG_DIR"
fi

mkdir -p "$MAC_ROOT/bin" "$LOG_DIR" "$HOME/Library/LaunchAgents"
swiftc "$SOURCE" -o "$BIN" -lsqlite3
chmod +x "$BIN"
rm -rf "$MAC_ROOT/bin/settings"
cp -R "$SHARED_SETTINGS" "$MAC_ROOT/bin/settings"
chmod -R u+rwX,go+rX "$MAC_ROOT/bin/settings"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP_RESOURCES"
cp "$BIN" "$APP_BIN"
chmod +x "$APP_BIN"
cp -R "$SHARED_SETTINGS" "$APP_RESOURCES/settings"
chmod -R u+rwX,go+rX "$APP_RESOURCES/settings"
cat > "$APP/Contents/Info.plist" <<APPPLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key>
  <string>CodexPetLimitRings</string>
  <key>CFBundleIdentifier</key>
  <string>$LABEL</string>
  <key>CFBundleName</key>
  <string>Codex Pet HUD</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>LSUIElement</key>
  <true/>
</dict>
</plist>
APPPLIST
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true

resolved_plist="$(mktemp -t codex-pet-limit-rings.XXXXXX.plist)"
trap 'rm -f "$resolved_plist"' EXIT
sed \
  -e "s|__APP_BIN__|$APP_BIN|g" \
  -e "s|__STDOUT_LOG__|$LOG_DIR/stdout.log|g" \
  -e "s|__STDERR_LOG__|$LOG_DIR/stderr.log|g" \
  "$PLIST_TEMPLATE" > "$resolved_plist"

uid="$(id -u)"
launchctl bootout "gui/$uid" "$ACTIVE_PLIST" 2>/dev/null || true
launchctl bootout "gui/$uid/$LABEL" 2>/dev/null || true
launchctl bootout "gui/$uid" "$LEGACY_PLIST" 2>/dev/null || true
launchctl bootout "gui/$uid/$LEGACY_LABEL" 2>/dev/null || true
rm -f "$LEGACY_PLIST"
cp "$resolved_plist" "$ACTIVE_PLIST"
launchctl bootstrap "gui/$uid" "$ACTIVE_PLIST"
launchctl kickstart -k "gui/$uid/$LABEL"
sleep 2
launchctl print "gui/$uid/$LABEL" | grep -E 'state =|pid =|job state ='

rm -rf "$REPO_ROOT/bin/CodexPetLimitRings.app" "$REPO_ROOT/bin/assets"
rmdir "$REPO_ROOT/bin" 2>/dev/null || true
