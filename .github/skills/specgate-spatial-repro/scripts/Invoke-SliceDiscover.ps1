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

Writes registry.ctsc.json and logs to -OutDir (default
<parent>\specgate-out\<slice>\discover). Exit code 0 means that the registry
is valid and matches; 1 means that it differs; 2 means that a step failed.
#>
[CmdletBinding()]
param(
    [string] $Slice,
    [string] $PullRequest,
    [string] $OutDir,
    [string] $SpecGateRoot,
    [string] $FeedPath,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SpecGateRepro.psm1') -Force

$layout = Get-ReproLayout -SpecGateRoot $SpecGateRoot -FeedPath $FeedPath
Assert-SpecGateInstalled -Layout $layout
$sliceInfo = Get-SliceInfo -Layout $layout -Slice $Slice -PullRequest $PullRequest
$Slice = $sliceInfo.Name
Set-AnnotationsFeed -Layout $layout

if (-not $OutDir) { $OutDir = Join-Path $layout.Parent "specgate-out\$Slice\discover" }
$OutDir = New-OutputDirectory -Path $OutDir -Force:$Force
$registry = Join-Path $OutDir 'registry.ctsc.json'
$exe = $layout.SpecGateExe

$code = Invoke-Logged $exe @('discover', '--component', $sliceInfo.Component, '--binding', 'specgate.yaml', '--impl', 'csharp',
    '--registry-id', $sliceInfo.RegistryId, '--registry-version', $sliceInfo.RegistryVersion, '-o', $registry) $sliceInfo.Dir (Join-Path $OutDir 'discover.log')
if ($code -ne 0) { Write-Host 'Discovery failed.'; exit 2 }

$code = Invoke-Logged $exe @('validate', 'registry', $registry) $sliceInfo.Dir (Join-Path $OutDir 'validate-registry.log')
if ($code -ne 0) { Write-Host 'Registry validation failed.'; exit 2 }

# Compare parsed JSON, so that whitespace and key layout do not count as differences.
$discovered = Get-Content $registry -Raw | ConvertFrom-Json -Depth 100 | ConvertTo-Json -Depth 100 -Compress
$saved = Get-Content $sliceInfo.ReferenceRegistry -Raw | ConvertFrom-Json -Depth 100 | ConvertTo-Json -Depth 100 -Compress
if ($discovered -ne $saved) {
    Write-Host "The discovered registry differs from $($sliceInfo.ReferenceRegistry)."
    exit 1
}
Write-Host "The discovered registry matches the saved reference registry. Results: $OutDir"
