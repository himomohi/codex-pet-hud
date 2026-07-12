param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "win-arm64" } else { "win-x64" })
)

$Script = Join-Path $PSScriptRoot "platforms\windows\scripts\install.ps1"
& $Script -Runtime $Runtime
exit $LASTEXITCODE
