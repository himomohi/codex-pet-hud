# Shared contracts

These schemas document the small data boundary shared by the native macOS and Windows applications. Runtime code stays platform-native; only settings, Codex pet state, usage response fields, and fixtures are shared.

The HUD must remain hidden unless `electron-avatar-overlay-open` is explicitly `true`, the bounds include a valid `mascot` or `anchor`, and the native shell confirms that the matching Codex pet window is actually visible. Tokens and full Codex history are never copied into this directory or into HUD settings.
