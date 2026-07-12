param([switch]$Data)

$ErrorActionPreference = "Stop"
$InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\CodexPetLimitRings"
$DataRoot = Join-Path $env:LOCALAPPDATA "CodexPetLimitRings"

Get-Process CodexPetLimitRings -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "CodexPetLimitRings" -ErrorAction SilentlyContinue
Remove-Item $InstallRoot -Recurse -Force -ErrorAction SilentlyContinue
if ($Data) { Remove-Item $DataRoot -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "Uninstalled Codex Pet HUD"
