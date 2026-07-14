# Changelog

All notable changes to Codex Pet HUD are documented here. This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Planned

- Developer ID signing and notarization for macOS
- Authenticode signing and real-device validation for Windows

## [0.1.3] - 2026-07-15

### Fixed

- Remove the Internet zone marker from the installed Windows executable before its first launch
- Launch the Windows app from its installed directory and register startup only after a successful launch
- Verify that both Windows release archives contain the corrected installer behavior

## [0.1.2] - 2026-07-15

### Fixed

- Keep the Windows HUD tied to the real visible Codex pet window instead of stale saved bounds
- Accept null usage buckets and preserve weekly-only events without terminating the Windows app
- Cancel in-flight usage refreshes when the pet is hidden and report startup or refresh failures in the runtime log
- Fail Windows packaging when publish output or required release payloads are missing

## [0.1.1] - 2026-07-13

### Changed

- Refresh pinned GitHub Actions revisions used by the release and Pages workflows

### Fixed

- Show a weekly usage potion correctly when an event removes the five-hour limit and the API returns only a seven-day primary window

## [0.1.0] - 2026-07-12

### Added

- Native macOS potion HUD linked to the real Codex pet visibility state
- Windows 10/11 WPF preview for x64 and ARM64
- Five-hour and weekly usage potions with reset countdowns
- Click details, manual refresh, and 20/10/5% threshold alerts
- Menu-bar and system-tray settings
- App-owned daily cache cleanup
- Four-language project documentation
- Pixel-art project website, download hub, and release harness

### Security

- No keyboard input capture
- Access tokens remain in memory and are never copied to settings or logs
- Release archives are checked for forbidden private files and include SHA-256 checksums

[Unreleased]: https://github.com/himomohi/codex-pet-hud/compare/v0.1.3...HEAD
[0.1.3]: https://github.com/himomohi/codex-pet-hud/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/himomohi/codex-pet-hud/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/himomohi/codex-pet-hud/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/himomohi/codex-pet-hud/releases/tag/v0.1.0
