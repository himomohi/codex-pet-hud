$ErrorActionPreference = "Stop"
$InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\CodexPetLimitRings"
Get-Process CodexPetLimitRings -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "CodexPetLimitRings" -ErrorAction SilentlyContinue
Remove-Item $InstallRoot -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Uninstalled Codex Pet HUD. Settings were preserved."
