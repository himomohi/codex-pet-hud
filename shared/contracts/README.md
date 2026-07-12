# Shared contracts

These schemas document the small data boundary shared by the native macOS and Windows applications. Runtime code stays platform-native; only settings, Codex pet state, usage response fields, and fixtures are shared.

The HUD must remain hidden unless `electron-avatar-overlay-open` is explicitly `true` and the anchor rectangle is valid. Tokens and full Codex history are never copied into this directory or into HUD settings.
