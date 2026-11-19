#Requires -Version 7.2
<#
.SYNOPSIS
Runs SpecGate discovery on the annotated C# sources of a slice.

.DESCRIPTION
Runs specgate discover for the slice component and validates the discovered
registry. It then checks that the registry has the same content as the
registry in the slice's saved reference. A difference means that the
annotations changed after the reference was recorded.

Use -PullRequest to select the published reference for an odata-rs PR. If
there is one complete reference, it is selected when both -PullRequest and
-Slice are omitted.

Use -Bootstrap with -Slice, -RegistryId and -RegistryVersion to inspect the
registry of a slice that has no saved reference yet. The slice must not have
a reference folder. Bootstrap discovery has nothing to compare with, so it
only discovers and validates.

Use -Component when the slice binding has several components.

Writes registry.ctsc.json and logs to -OutDir (default
<parent>\specgate-out\<slice>\discover). Exit code 0 means that the registry
is valid and matches (or, with -Bootstrap, is valid); 1 means that it
differs; 2 means that a step failed.
#>
[CmdletBinding()]
param(
    [string] $Slice,
    [string] $PullRequest,
    [string] $Component,
    [switch] $Bootstrap,
    [string] $RegistryId,
    [string] $RegistryVersion,
    [string] $OutDir,
    [string] $SpecGateRoot,
    [string] $FeedPath,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap {
    Write-Error -ErrorRecord $_ -ErrorAction Continue
    exit 2
}
Import-Module (Join-Path $PSScriptRoot 'SpecGateRepro.psm1') -Force

$layout = Get-ReproLayout -SpecGateRoot $SpecGateRoot -FeedPath $FeedPath
Assert-SpecGateInstalled -Layout $layout
if ($Bootstrap) {
    if (-not $Slice -or $PullRequest) { throw '-Bootstrap requires -Slice and does not accept -PullRequest.' }
    if (-not $RegistryId -or -not $RegistryVersion) { throw '-Bootstrap requires -RegistryId and -RegistryVersion.' }
    $sliceInfo = Get-SliceBinding -Layout $layout -Slice $Slice -Component $Component
    Assert-NoSavedReference -SliceBinding $sliceInfo
}
else {
    if ($RegistryId -or $RegistryVersion) {
        throw '-RegistryId and -RegistryVersion are only for -Bootstrap. Otherwise the saved reference supplies them.'
    }
    $sliceInfo = Get-SliceInfo -Layout $layout -Slice $Slice -PullRequest $PullRequest -Component $Component
    $RegistryId = $sliceInfo.RegistryId
    $RegistryVersion = $sliceInfo.RegistryVersion
}
$Slice = $sliceInfo.Name
Set-AnnotationsFeed -Layout $layout

if (-not $OutDir) { $OutDir = Join-Path $layout.Parent "specgate-out\$Slice\discover" }
$OutDir = New-OutputDirectory -Path $OutDir -Force:$Force
$registry = Join-Path $OutDir 'registry.ctsc.json'
$exe = $layout.SpecGateExe

$code = Invoke-Logged $exe @('discover', '--component', $sliceInfo.Component, '--binding', 'specgate.yaml', '--impl', 'csharp',
    '--registry-id', $RegistryId, '--registry-version', $RegistryVersion, '-o', $registry) $sliceInfo.Dir (Join-Path $OutDir 'discover.log')
if ($code -ne 0) { Write-Host 'Discovery failed.'; exit 2 }

$code = Invoke-Logged $exe @('validate', 'registry', $registry) $sliceInfo.Dir (Join-Path $OutDir 'validate-registry.log')
if ($code -ne 0) { Write-Host 'Registry validation failed.'; exit 2 }

if ($Bootstrap) {
    Write-Host "The discovered registry is valid. No saved reference exists to compare with. Results: $OutDir"
    exit 0
}

# Compare parsed JSON, so that whitespace and key layout do not count as differences.
$discovered = Get-Content $registry -Raw | ConvertFrom-Json -Depth 100 | ConvertTo-Json -Depth 100 -Compress
$saved = Get-Content $sliceInfo.ReferenceRegistry -Raw | ConvertFrom-Json -Depth 100 | ConvertTo-Json -Depth 100 -Compress
if ($discovered -ne $saved) {
    Write-Host "The discovered registry differs from $($sliceInfo.ReferenceRegistry)."
    exit 1
}
Write-Host "The discovered registry matches the saved reference registry. Results: $OutDir"
