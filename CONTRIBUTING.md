# Contributing to Codex Pet HUD

Thanks for helping the tiny HUD stay focused and reliable.

## Before opening a pull request

1. Keep changes scoped to one behavior or platform concern.
2. Never add keyboard hooks, token logging, or cleanup outside app-owned paths.
3. Preserve the pet visibility trigger and keep the pet center clickable.
4. Run `./install.sh` on macOS or `.\install.ps1` on Windows and verify the HUD with the real Codex pet.
5. Update all affected localized README files when user-facing behavior changes.

Bug reports should include the OS version, Codex version, expected behavior, and sanitized logs. Never attach `auth.json` or access tokens.
