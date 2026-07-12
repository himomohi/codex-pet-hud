#!/bin/bash
set -euo pipefail

TAG="${1:?usage: build-macos.sh vX.Y.Z}"
VERSION="${TAG#v}"
[[ "$TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Invalid release tag: $TAG" >&2; exit 1; }

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SOURCE="$ROOT/platforms/macos/CodexPetLimitRings.swift"
SETTINGS="$ROOT/shared/settings"
WORK="$ROOT/artifacts/release/macos"
DIST="$ROOT/dist"
APP="$WORK/Codex Pet HUD.app"
EXECUTABLE="$APP/Contents/MacOS/CodexPetLimitRings"
SDK="$(xcrun --sdk macosx --show-sdk-path)"

rm -rf "$WORK"
mkdir -p "$WORK/arm64" "$WORK/x86_64" "$APP/Contents/MacOS" "$APP/Contents/Resources" "$DIST"

for arch in arm64 x86_64; do
  xcrun --sdk macosx swiftc "$SOURCE" \
    -target "$arch-apple-macos13.0" \
    -sdk "$SDK" \
    -framework AppKit \
    -framework WebKit \
    -lsqlite3 \
    -O \
    -o "$WORK/$arch/CodexPetLimitRings"
done

lipo -create "$WORK/arm64/CodexPetLimitRings" "$WORK/x86_64/CodexPetLimitRings" -output "$EXECUTABLE"
cp -R "$SETTINGS" "$APP/Contents/Resources/settings"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>CodexPetLimitRings</string>
  <key>CFBundleIdentifier</key><string>io.github.himomohi.codex-pet-hud</string>
  <key>CFBundleName</key><string>Codex Pet HUD</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSUIElement</key><true/>
</dict></plist>
PLIST

chmod +x "$EXECUTABLE"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"
lipo -archs "$EXECUTABLE" | grep -q 'arm64 x86_64\|x86_64 arm64'

ARCHIVE="$DIST/Codex-Pet-HUD-$TAG-macOS-universal.zip"
rm -f "$ARCHIVE"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ARCHIVE"
echo "$ARCHIVE"
