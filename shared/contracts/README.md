# Shared contracts

These schemas document the small data boundary shared by the native macOS and Windows applications. Runtime code stays platform-native; only settings, Codex pet state, usage response fields, and fixtures are shared.

`potionStyle` is an optional persisted setting shared by both apps. Its default is `classic`; five illustrated styles use the same IDs in both native shells and the settings page. Missing or unknown IDs fall back to Classic while known layout and alert preferences are retained. The schema lists all six supported values. `scripts/test-potion-assets.mjs` checks that both platforms carry the same 15 preview/frame/mask PNGs and that local-page data-URI masks match their original PNGs.

The HUD must remain hidden unless `electron-avatar-overlay-open` is explicitly `true`, the bounds provide a valid anchor, and the native shell confirms that a matching Codex pet surface is actually visible. Both platforms accept the legacy `mascot`/`anchor` rectangles and the integrated app's direct `x`/`y` anchor. Tokens and full Codex history are never copied into this directory or into HUD settings.
