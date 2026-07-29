param(
  [Parameter(Mandatory = $true)][ValidateSet("win-x64", "win-arm64")][string]$Runtime,
  [Parameter(Mandatory = $true)][string]$Tag
)

$ErrorActionPreference = "Stop"
if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw "Invalid release tag: $Tag" }

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$Project = Join-Path $Root "platforms\windows\CodexPetLimitRings.Windows\CodexPetLimitRings.Windows.csproj"
$LayoutTests = Join-Path $Root "platforms\windows\tests\LayoutTests\LayoutTests.csproj"
$Work = Join-Path $Root "artifacts\release\windows-$Runtime"
$App = Join-Path $Work "app"
$Dist = Join-Path $Root "dist"
$Version = $Tag.TrimStart('v')

Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
New-Item $App -ItemType Directory -Force | Out-Null
New-Item $Dist -ItemType Directory -Force | Out-Null

dotnet run --project $LayoutTests -c Release
if ($LASTEXITCODE -ne 0) { throw "Windows HUD layout tests failed." }

dotnet publish $Project `
  -c Release `
  -r $Runtime `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:ContinuousIntegrationBuild=true `
  -p:Version=$Version `
  -o $App
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed for $Runtime." }
if (-not (Test-Path (Join-Path $App "CodexPetLimitRings.exe"))) { throw "Windows executable is missing for $Runtime." }

Copy-Item (Join-Path $Root "LICENSE") (Join-Path $Work "LICENSE.txt")
Copy-Item (Join-Path $Root "scripts\release\windows\Install.ps1") (Join-Path $Work "Install.ps1")
Copy-Item (Join-Path $Root "scripts\release\windows\Uninstall.ps1") (Join-Path $Work "Uninstall.ps1")
@"
Codex Pet HUD $Tag — Windows $Runtime Unsigned Preview

Run Install.ps1 to install for the current user. No administrator access is required.
Windows may show a SmartScreen warning because this preview is not Authenticode-signed.
Download only from https://github.com/himomohi/codex-pet-hud/releases
"@ | Set-Content (Join-Path $Work "README.txt") -Encoding UTF8

$Archive = Join-Path $Dist "Codex-Pet-HUD-$Tag-Windows-$($Runtime.Replace('win-', '')).zip"
Remove-Item $Archive -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $Work "*") -DestinationPath $Archive -CompressionLevel Optimal
Write-Output $Archive
