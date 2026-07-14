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

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK가 필요합니다. https://dotnet.microsoft.com/download/dotnet/8.0 에서 설치한 뒤 다시 실행하세요."
}

Remove-Item $Artifact -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $Project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -o $Artifact
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed." }

Get-Process CodexPetLimitRings -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item $InstallRoot -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $Artifact "*") $InstallRoot -Recurse -Force
New-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "CodexPetLimitRings" -Value ('"' + $Executable + '"') -PropertyType String -Force | Out-Null
$Process = Start-Process $Executable -PassThru
Start-Sleep -Seconds 1
if ($Process.HasExited) {
    throw "Codex Pet HUD가 시작 직후 종료되었습니다. $env:LOCALAPPDATA\CodexPetLimitRings\Logs\runtime.log를 확인하세요."
}
Write-Host "Installed Codex Pet HUD: $Executable"
