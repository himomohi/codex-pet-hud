# Shared contracts

These schemas document the small data boundary shared by the native macOS and Windows applications. Runtime code stays platform-native; only settings, Codex pet state, usage response fields, and fixtures are shared.

The HUD must remain hidden unless `electron-avatar-overlay-open` is explicitly `true`, the bounds provide a valid anchor, and the native shell confirms that a matching Codex pet surface is actually visible. Both platforms accept the legacy `mascot`/`anchor` rectangles. macOS also accepts the integrated app's direct `x`/`y` anchor; Windows remains on the legacy contract until that format is validated there. Tokens and full Codex history are never copied into this directory or into HUD settings.
