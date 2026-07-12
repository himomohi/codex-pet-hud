param([switch]$Data)

$Script = Join-Path $PSScriptRoot "platforms\windows\scripts\uninstall.ps1"
& $Script -Data:$Data
exit $LASTEXITCODE
