$ErrorActionPreference = "Stop"
$Source = Join-Path $PSScriptRoot "app"
$InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\CodexPetLimitRings"
$Executable = Join-Path $InstallRoot "CodexPetLimitRings.exe"
$RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

if (-not (Test-Path (Join-Path $Source "CodexPetLimitRings.exe"))) { throw "Release app payload is missing." }
Get-Process CodexPetLimitRings -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty $RunKey -Name "CodexPetLimitRings" -ErrorAction SilentlyContinue
Remove-Item $InstallRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item $InstallRoot -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $Source "*") $InstallRoot -Recurse -Force
Unblock-File -LiteralPath $Executable
$StartInfo = New-Object System.Diagnostics.ProcessStartInfo -Property @{
    FileName = $Executable
    WorkingDirectory = $InstallRoot
    UseShellExecute = $true
}
try {
    $Process = [System.Diagnostics.Process]::Start($StartInfo)
} catch [System.ComponentModel.Win32Exception] {
    if ($_.Exception.NativeErrorCode -eq 1223) {
        throw "Codex Pet HUD was installed, but Windows canceled its first launch (error 1223). The startup entry was not registered. Check Windows Security or your organization policy, then run Install.ps1 again."
    }
    throw
}
Start-Sleep -Seconds 1
if ($Process.HasExited) {
    throw "Codex Pet HUD exited during startup. Check $env:LOCALAPPDATA\CodexPetLimitRings\Logs\runtime.log."
}
New-ItemProperty $RunKey -Name "CodexPetLimitRings" -Value ('"' + $Executable + '"') -PropertyType String -Force | Out-Null
Write-Host "Installed Codex Pet HUD to $InstallRoot"
