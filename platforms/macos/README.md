# macOS

The macOS implementation remains a native Swift/AppKit menu-bar application. The repository-root `install.sh` and `uninstall.sh` commands are compatibility wrappers around the scripts in this folder.

```bash
./install.sh
launchctl print gui/$(id -u)/io.github.himomohi.codex-pet-hud
tail -20 platforms/macos/logs/stderr.log
```

Settings and alert state remain under `~/Library/Application Support/CodexPetLimitRings`, so moving the source tree does not reset user preferences.
