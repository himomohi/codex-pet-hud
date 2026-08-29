[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^v\d+\.\d+\.\d+$')]
  [string]$Tag,

  [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $Root

function Invoke-Checked {
  param(
    [Parameter(Mandatory = $true)][string]$Command,
    [Parameter()][string[]]$Arguments = @()
  )

  & $Command @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "Command failed ($LASTEXITCODE): $Command $($Arguments -join ' ')"
  }
}

function Get-GnuBash {
  if (-not $IsWindows) {
    $command = Get-Command bash -ErrorAction Stop
    return $command.Source
  }

  $candidates = [System.Collections.Generic.List[string]]::new()
  $gitCommand = Get-Command git -ErrorAction Stop
  $gitRoot = Split-Path (Split-Path $gitCommand.Source -Parent) -Parent
  $candidates.Add((Join-Path $gitRoot 'bin\bash.exe'))
  $candidates.Add((Join-Path $gitRoot 'usr\bin\bash.exe'))
  $candidates.Add('C:\Program Files\Git\bin\bash.exe')

  foreach ($candidate in $candidates | Select-Object -Unique) {
    if (-not (Test-Path -LiteralPath $candidate)) { continue }
    $version = & $candidate --version 2>$null | Select-Object -First 1
    if ($version -match 'GNU bash') { return $candidate }
  }

  throw 'GNU Bash was not found. Install Git for Windows or provide bash on PATH.'
}

function Get-RepositoryName {
  $name = & gh repo view --json nameWithOwner --jq '.nameWithOwner'
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($name)) {
    throw 'Unable to resolve the GitHub repository.'
  }
  return $name.Trim()
}

function Get-DefaultBranch {
  $branch = & gh repo view --json defaultBranchRef --jq '.defaultBranchRef.name'
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branch)) {
    throw 'Unable to resolve the default branch.'
  }
  return $branch.Trim()
}

function Assert-PublicRelease {
  param(
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$ExpectedCommit
  )

  $releaseJson = & gh api "repos/$Repository/releases/latest"
  if ($LASTEXITCODE -ne 0) { throw 'Unable to read the latest GitHub Release.' }
  $release = $releaseJson | ConvertFrom-Json

  if ($release.tag_name -ne $Tag) { throw "Latest release is $($release.tag_name), expected $Tag." }
  if ($release.draft -or $release.prerelease) { throw 'Published release must not be draft or prerelease.' }

  $expectedAssets = @(
    "Codex-Pet-HUD-$Tag-macOS-universal.zip",
    "Codex-Pet-HUD-$Tag-Windows-x64.zip",
    "Codex-Pet-HUD-$Tag-Windows-arm64.zip",
    'SHA256SUMS.txt'
  ) | Sort-Object
  $actualAssets = @($release.assets | ForEach-Object { $_.name }) | Sort-Object
  $assetDifference = Compare-Object -ReferenceObject $expectedAssets -DifferenceObject $actualAssets
  if ($assetDifference) {
    throw "Release assets differ from the required set: $($assetDifference | Out-String)"
  }

  foreach ($asset in $release.assets) {
    if ($asset.state -ne 'uploaded') { throw "Release asset is not uploaded: $($asset.name)" }
    if ($asset.digest -notmatch '^sha256:[0-9a-f]{64}$') { throw "Release asset lacks a SHA-256 digest: $($asset.name)" }
  }

  $remoteDereference = 'refs/tags/{0}^{{}}' -f $Tag
  $remoteTag = & git ls-remote origin $remoteDereference
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($remoteTag)) {
    throw "Unable to dereference remote tag $Tag."
  }
  $remoteCommit = ($remoteTag -split '\s+')[0]
  if ($remoteCommit -ne $ExpectedCommit) {
    throw "Remote tag $Tag points to $remoteCommit, expected $ExpectedCommit."
  }

  Write-Output "release_url=$($release.html_url)"
  Write-Output "release_assets=$($actualAssets.Count)"
  Write-Output "release_tag_commit=$remoteCommit"
}

$repository = Get-RepositoryName

if ($VerifyOnly) {
  Invoke-Checked git @('fetch', 'origin', '--tags')
  $tagCommit = & git rev-list -n 1 $Tag
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tagCommit)) {
    throw "Tag does not exist: $Tag"
  }
  Assert-PublicRelease -Repository $repository -ExpectedCommit $tagCommit.Trim()
  Write-Output "Release verification passed for $Tag"
  exit 0
}

Invoke-Checked gh @('auth', 'status')
$defaultBranch = Get-DefaultBranch
$currentBranch = (& git branch --show-current).Trim()
if ($currentBranch -ne $defaultBranch) {
  throw "Release must run from the default branch $defaultBranch, current branch is $currentBranch."
}
if (-not [string]::IsNullOrWhiteSpace((& git status --porcelain))) {
  throw 'Release requires a clean working tree. Commit the intended scope first.'
}

Invoke-Checked git @('fetch', 'origin', $defaultBranch)
$headCommit = (& git rev-parse HEAD).Trim()
$remoteCommit = (& git rev-parse "origin/$defaultBranch").Trim()
if ($headCommit -ne $remoteCommit) {
  throw "Local HEAD $headCommit does not match origin/$defaultBranch $remoteCommit."
}

& git show-ref --verify --quiet "refs/tags/$Tag"
if ($LASTEXITCODE -eq 0) { throw "Local tag already exists: $Tag" }
$remoteTag = & git ls-remote --tags origin "refs/tags/$Tag"
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect remote tags.' }
if (-not [string]::IsNullOrWhiteSpace($remoteTag)) { throw "Remote tag already exists: $Tag" }
& gh release view $Tag --repo $repository *> $null
if ($LASTEXITCODE -eq 0) { throw "GitHub Release already exists: $Tag" }

$bash = Get-GnuBash
$bashPrefix = if ($IsWindows) { @('--login') } else { @() }
Invoke-Checked -Command $bash -Arguments (@($bashPrefix) + @('scripts/release/scan-secrets.sh'))
Invoke-Checked -Command $bash -Arguments (@($bashPrefix) + @('scripts/release/verify-release.sh', $Tag))
Invoke-Checked dotnet @('build', 'platforms/windows/CodexPetLimitRings.Windows/CodexPetLimitRings.Windows.csproj', '-c', 'Release')
Invoke-Checked dotnet @('run', '--project', 'platforms/windows/tests/LayoutTests/LayoutTests.csproj', '-c', 'Release')

if ($IsWindows) {
  Invoke-Checked pwsh @('-NoProfile', '-File', 'scripts/release/build-windows.ps1', '-Runtime', 'win-x64', '-Tag', $Tag)
  Invoke-Checked pwsh @('-NoProfile', '-File', 'scripts/release/build-windows.ps1', '-Runtime', 'win-arm64', '-Tag', $Tag)
} elseif ($IsMacOS) {
  Invoke-Checked -Command $bash -Arguments (@($bashPrefix) + @('scripts/test-macos.sh'))
  Invoke-Checked -Command $bash -Arguments (@($bashPrefix) + @('scripts/release/build-macos.sh', $Tag))
} else {
  throw 'Release publishing is supported only from Windows or macOS.'
}

Invoke-Checked git @('tag', '-a', $Tag, $headCommit, '-m', "Codex Pet HUD $Tag")
Invoke-Checked git @('push', 'origin', $Tag)

$deadline = [DateTime]::UtcNow.AddMinutes(2)
$run = $null
do {
  $runsJson = & gh run list --repo $repository --workflow release.yml --branch $Tag --limit 10 --json databaseId,headSha,status,conclusion,url,createdAt
  if ($LASTEXITCODE -ne 0) { throw 'Unable to list release workflow runs.' }
  $runs = @($runsJson | ConvertFrom-Json)
  $run = $runs |
    Where-Object { $_.headSha -eq $headCommit } |
    Sort-Object createdAt -Descending |
    Select-Object -First 1
  if ($null -eq $run) { Start-Sleep -Seconds 3 }
} while ($null -eq $run -and [DateTime]::UtcNow -lt $deadline)

if ($null -eq $run) { throw "Release workflow did not start for $Tag." }
Invoke-Checked gh @('run', 'watch', $run.databaseId.ToString(), '--repo', $repository, '--exit-status', '--interval', '10')

$completedJson = & gh run view $run.databaseId --repo $repository --json status,conclusion,url,headSha
if ($LASTEXITCODE -ne 0) { throw 'Unable to read the completed release workflow.' }
$completed = $completedJson | ConvertFrom-Json
if ($completed.status -ne 'completed' -or $completed.conclusion -ne 'success') {
  throw "Release workflow did not succeed: $($completed.url)"
}

Assert-PublicRelease -Repository $repository -ExpectedCommit $headCommit
Write-Output "workflow_url=$($completed.url)"
Write-Output "Release completed end to end for $Tag"
