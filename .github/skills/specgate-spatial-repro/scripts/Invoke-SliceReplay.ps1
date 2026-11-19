#Requires -Version 7.2
<#
.SYNOPSIS
Replays a slice's saved C# reference against the odata-rs candidate and compares the results.

.DESCRIPTION
Runs, in order: validate bundle, replay, validate linked, and compare. Writes
each command's output, the candidate trace, and summary.json to -OutDir
(default <parent>\specgate-out\<slice>\replay).

Use -PullRequest to select the reference published for an odata-rs PR. If
there is one complete published reference, it is selected when both
-PullRequest and -Slice are omitted.

The pinned SpecGate CLI rejects a Git-sourced specgate dependency during
replay. For the replay only, this script adds a temporary
<parent>\.cargo\config.toml that patches the odata-rs specgate dependency to
the same commit in the local SpecGate clone. It removes the patch and restores
odata-rs\Cargo.lock afterward, also when replay fails.

Exit codes: 0 when compare reports equivalent, 1 when compare reports
differences, 2 when a step fails before compare finishes.
#>
[CmdletBinding()]
param(
    [string] $Slice,
    [string] $PullRequest,
    [string] $OutDir,
    [string] $SpecGateRoot,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SpecGateRepro.psm1') -Force

$layout = Get-ReproLayout -SpecGateRoot $SpecGateRoot
Assert-SpecGateInstalled -Layout $layout
$sliceInfo = Get-SliceInfo -Layout $layout -Slice $Slice -PullRequest $PullRequest
$Slice = $sliceInfo.Name
$pinned = Get-PinnedSpecGateDependency -Layout $layout

$specgateHead = (& git -C $layout.SpecGateRoot rev-parse HEAD)
if ($specgateHead -ne $pinned.Rev) {
    throw "SpecGate clone is at $specgateHead, but odata-rs pins $($pinned.Rev). Install the pinned commit first."
}

if (-not $OutDir) { $OutDir = Join-Path $layout.Parent "specgate-out\$Slice\replay" }
$OutDir = New-OutputDirectory -Path $OutDir -Force:$Force
$candidate = Join-Path $OutDir 'candidate.otlp.json'
$exe = $layout.SpecGateExe
$work = $sliceInfo.Dir

$code = Invoke-Logged $exe @('validate', 'bundle', 'reference') $work (Join-Path $OutDir 'validate-bundle.log')
if ($code -ne 0) { Write-Host 'Bundle validation failed.'; exit 2 }

$cargoDir = Join-Path $layout.Parent '.cargo'
$cargoConfig = Join-Path $cargoDir 'config.toml'
if (Test-Path $cargoConfig) {
    throw "$cargoConfig already exists. Move it away first; this script does not merge Cargo configuration."
}
$lockFile = Join-Path $layout.OdataRs 'Cargo.lock'
$lockBackup = Join-Path $OutDir 'Cargo.lock.before-replay'
Copy-Item $lockFile $lockBackup
$lockHash = (Get-FileHash $lockFile).Hash
$createdCargoDir = -not (Test-Path $cargoDir)

$patchedCrate = (Join-Path $layout.SpecGateRoot 'rust\crates\specgate') -replace '\\', '/'
$patch = @"
# Temporary patch written by Invoke-SliceReplay.ps1. It is removed after replay.
[patch."$($pinned.GitUrl)"]
specgate = { path = '$patchedCrate' }
"@

try {
    New-Item -ItemType Directory -Path $cargoDir -Force | Out-Null
    Set-Content -Path $cargoConfig -Value $patch -Encoding utf8
    $code = Invoke-Logged $exe @('replay', 'reference', '--binding', 'specgate.yaml', '--impl', 'rust', '--out', $candidate, '--force') $work (Join-Path $OutDir 'replay.log')
}
finally {
    Remove-Item $cargoConfig -Force -ErrorAction SilentlyContinue
    if ($createdCargoDir) { Remove-Item $cargoDir -Recurse -Force -ErrorAction SilentlyContinue }
    Copy-Item $lockBackup $lockFile -Force
    if ((Get-FileHash $lockFile).Hash -ne $lockHash) { throw "Could not restore $lockFile." }
    Remove-Item $lockBackup -Force
    Write-Host 'Removed the temporary Cargo patch and restored odata-rs\Cargo.lock.'
}
if ($code -ne 0) { Write-Host 'Replay failed.'; exit 2 }

$code = Invoke-Logged $exe @('validate', 'linked', $candidate, 'reference\registry.ctsc.json') $work (Join-Path $OutDir 'validate-linked.log')
if ($code -ne 0) { Write-Host 'Linked validation failed.'; exit 2 }

$compareLog = Join-Path $OutDir 'compare.log'
$compareCode = Invoke-Logged $exe @('compare', 'reference\reference.otlp.json', $candidate, '--registry', 'reference\registry.ctsc.json') $work $compareLog
$lines = Get-Content $compareLog
$equivalentLine = $lines | Where-Object { $_ -match '^equivalent:\s*(true|false)' } | Select-Object -First 1
if (-not $equivalentLine -or $compareCode -gt 1) { Write-Host 'Compare did not finish.'; exit 2 }

# Group mismatch lines by operation and result field, for example "geography result.Id".
$groups = $lines | Where-Object { $_ -like 'mismatch *' } | ForEach-Object {
    if ($_ -match 'operation\[[^\]]*::([^\]]+)\].*?\.events\[\d+\]\.([^:]+):') { "$($Matches[1]) $($Matches[2])" }
    else { 'other' }
} | Group-Object | Sort-Object Name | ForEach-Object { [ordered]@{ key = $_.Name; count = $_.Count } }

$summary = [ordered]@{
    slice          = $Slice
    component      = $sliceInfo.Component
    specgateCommit = $pinned.Rev
    odataRsCommit  = (& git -C $layout.OdataRs rev-parse HEAD)
    odataRsDirty   = [bool](& git -C $layout.OdataRs status --porcelain)
    forkCommit     = (& git -C $layout.ForkRoot rev-parse HEAD)
    equivalent     = ($equivalentLine -match 'true')
    mismatchLines  = @($lines | Where-Object { $_ -like 'mismatch *' }).Count
    mismatches     = @($groups)
}
$summary | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutDir 'summary.json') -Encoding utf8

Write-Host ''
Write-Host "equivalent: $($summary.equivalent); mismatch lines: $($summary.mismatchLines)"
$groups | ForEach-Object { Write-Host ("  {0,4}  {1}" -f $_.count, $_.key) }
Write-Host "Results: $OutDir"
exit $compareCode
