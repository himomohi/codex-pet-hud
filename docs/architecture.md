# Architecture

## Boundary

Codex Pet HUD has two native shells with one shared contract layer:

```text
Codex state + auth + usage API
              |
       shared/contracts
          /         \
 Swift/AppKit      .NET/WPF
    macOS           Windows
```

The platform applications do not share UI source code. This keeps AppKit/launchd behavior out of Windows and WPF/registry behavior out of macOS. `shared/` contains only portable web assets, schemas, and deterministic fixtures.

## Runtime ownership

| Concern | macOS | Windows |
|---|---|---|
| HUD | one transparent AppKit window with potion hit regions | two transparent WPF windows, leaving the pet center uncovered |
| Tray | `NSStatusItem` | `System.Windows.Forms.NotifyIcon` |
| Settings | local WKWebView using `shared/settings` | native WPF settings window |
| Startup | per-user LaunchAgent | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| App data | `~/Library/Application Support/CodexPetLimitRings` | `%LOCALAPPDATA%\CodexPetLimitRings` |
| Cleanup | app-owned HTTP/cache/temp/log data only | app-owned Cache/Temp/Logs only |

## Trigger invariant

Both native apps must show the HUD only when:

1. `electron-avatar-overlay-open` exists and is exactly `true`;
2. `electron-avatar-overlay-bounds` contains the legacy window plus `mascot`/`anchor` rectangles; on macOS, the integrated app's direct `x`/`y` anchor is also accepted;
3. the matching Codex pet window is currently visible, not minimized, and not DWM-cloaked.

When the invariant becomes false, potion windows, detail UI, transient alerts, and usage refresh stop immediately. Tray access remains available for settings and diagnostics.

## Secret handling

The usage access token is read directly from the platform user's `.codex/auth.json` only for the HTTPS request. It is never copied into app settings, shared fixtures, logs, notifications, or build artifacts.

## Windows release gate

`EnableWindowsTargeting` allows compilation on macOS, but a Windows release still requires real Windows 10/11 checks for:

- the current Codex state path and JSON contract;
- WPF transparency and non-activating potion clicks;
- mixed-DPI and negative-coordinate monitors;
- tray startup, balloon notifications, and uninstall;
- code signing and SmartScreen reputation.

Primary references: [Microsoft cross-targeting guidance](https://learn.microsoft.com/dotnet/core/tools/sdk-errors/netsdk1100), [WPF transparency](https://learn.microsoft.com/dotnet/api/system.windows.window.allowstransparency), and [NotifyIcon](https://learn.microsoft.com/dotnet/desktop/winforms/controls/app-icons-to-the-taskbar-with-wf-notifyicon).
