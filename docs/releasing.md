# Release handbook

Codex Pet HUD publishes unsigned preview builds from version tags. The tag is the single version source for both native apps.

## Release flow

1. Update `CHANGELOG.md` and move changes from `Unreleased` into a dated version.
2. Verify macOS locally with `./scripts/release/build-macos.sh vX.Y.Z`.
3. Commit and push `main`.
4. Create and push an annotated `vX.Y.Z` tag.
5. The Release workflow builds macOS universal, Windows x64, and Windows ARM64 archives in clean GitHub runners.
6. The publish job rejects forbidden files, writes `SHA256SUMS.txt`, verifies the checksums, and creates the latest GitHub Release marked **Unsigned Preview**.
7. Confirm every asset from the public download page before announcing the release.

## Expected assets

```text
Codex-Pet-HUD-vX.Y.Z-macOS-universal.zip
Codex-Pet-HUD-vX.Y.Z-Windows-x64.zip
Codex-Pet-HUD-vX.Y.Z-Windows-arm64.zip
SHA256SUMS.txt
```

GitHub automatically supplies source archives for the tagged commit.

## Signing gate

The current harness uses an ad-hoc macOS signature and unsigned Windows binaries. Releases must remain marked **Unsigned Preview** until all of these are true:

- macOS Developer ID signing, hardened runtime, notarization, and stapling pass
- Windows Authenticode signing passes `signtool verify /pa`
- Windows x64 and ARM64 UI, DPI, tray, startup, and uninstall behavior pass on real devices
- Both archives are rechecked for `.codex`, `auth.json`, logs, credentials, certificates, and local paths

Do not document Gatekeeper or SmartScreen bypasses as the normal installation path.
