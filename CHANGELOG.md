# Changelog

All notable changes to Codex Pet HUD are documented here. This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Planned

- Developer ID signing and notarization for macOS
- Authenticode signing and real-device validation for Windows

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

[Unreleased]: https://github.com/himomohi/codex-pet-hud/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/himomohi/codex-pet-hud/releases/tag/v0.1.0
