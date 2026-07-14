# Windows

The Windows implementation is a native .NET 8 WPF tray application. It creates two small transparent potion windows so the real Codex pet remains visible and clickable between them.

## Requirements

- Windows 10 1703 or newer, or Windows 11
- .NET 8 SDK for source installation
- Codex must expose `%USERPROFILE%\.codex\.codex-global-state.json` with the state contract documented under `shared/contracts/`

Set `CODEX_HOME` when the Codex data directory is not `%USERPROFILE%\.codex`.

## Install

```powershell
.\install.ps1
```

The installer publishes a self-contained executable, copies it to `%LOCALAPPDATA%\Programs\CodexPetLimitRings`, registers the current-user Run key, and starts it. It does not require administrator privileges.

Runtime diagnostics are written to `%LOCALAPPDATA%\CodexPetLimitRings\Logs\runtime.log` without tokens or response bodies.

Uninstall while preserving settings:

```powershell
.\uninstall.ps1
```

Remove settings and alert history too:

```powershell
.\uninstall.ps1 -Data
```

## Windows verification still required

The source can be cross-compiled from macOS after installing the .NET SDK, but transparent WPF windows, tray behavior, per-monitor DPI, SmartScreen, and the real Windows Codex pet state must be verified on a Windows 10/11 machine before a signed release.
