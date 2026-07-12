$ErrorActionPreference = "Stop"
$Source = Join-Path $PSScriptRoot "app"
$InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\CodexPetLimitRings"
$Executable = Join-Path $InstallRoot "CodexPetLimitRings.exe"

if (-not (Test-Path (Join-Path $Source "CodexPetLimitRings.exe"))) { throw "Release app payload is missing." }
Get-Process CodexPetLimitRings -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $InstallRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item $InstallRoot -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $Source "*") $InstallRoot -Recurse -Force
New-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "CodexPetLimitRings" -Value ('"' + $Executable + '"') -PropertyType String -Force | Out-Null
Start-Process $Executable
Write-Host "Installed Codex Pet HUD to $InstallRoot"
