param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "win-arm64" } else { "win-x64" })
)

$ErrorActionPreference = "Stop"
$WindowsRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $WindowsRoot "CodexPetLimitRings.Windows\CodexPetLimitRings.Windows.csproj"
$Artifact = Join-Path $WindowsRoot "artifacts\$Runtime"
$InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\CodexPetLimitRings"
$Executable = Join-Path $InstallRoot "CodexPetLimitRings.exe"
$RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK가 필요합니다. https://dotnet.microsoft.com/download/dotnet/8.0 에서 설치한 뒤 다시 실행하세요."
}

Remove-Item $Artifact -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $Project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -o $Artifact
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed." }

$RunningProcesses = @(Get-Process CodexPetLimitRings -ErrorAction SilentlyContinue)
if ($RunningProcesses) {
    $RunningProcesses | Stop-Process -Force
    foreach ($RunningProcess in $RunningProcesses) {
        $RunningProcess.WaitForExit(5000)
    }
}
Remove-ItemProperty $RunKey -Name "CodexPetLimitRings" -ErrorAction SilentlyContinue
New-Item $InstallRoot -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $Artifact "*") $InstallRoot -Recurse -Force
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
        throw "Codex Pet HUD가 설치되었지만 Windows가 첫 실행을 취소했습니다(오류 1223). 자동 시작은 등록되지 않았습니다. Windows 보안 또는 조직 정책을 확인한 뒤 install.ps1을 다시 실행하세요."
    }
    throw
}
Start-Sleep -Seconds 1
if ($Process.HasExited) {
    throw "Codex Pet HUD가 시작 직후 종료되었습니다. $env:LOCALAPPDATA\CodexPetLimitRings\Logs\runtime.log를 확인하세요."
}
New-ItemProperty $RunKey -Name "CodexPetLimitRings" -Value ('"' + $Executable + '"') -PropertyType String -Force | Out-Null
Write-Host "Installed Codex Pet HUD: $Executable"
