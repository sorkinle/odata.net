#Requires -Version 7.2
<#
.SYNOPSIS
Saves the bundle of a passed capture run as a slice's first reference.

.DESCRIPTION
Use this after Invoke-SliceCapture.ps1 -Bootstrap. The script copies
manifest.json, reference.otlp.json, and registry.ctsc.json from
<RunDir>\capture to specgate-capture\slices\<slice>\reference.

It refuses to save when:
- <RunDir>\capture-result.json is missing, or does not record a passed test
  gate and a valid bundle.
- A bundle file changed after the capture run.
- The slice already has a reference folder.

Review the bundle before you save it. Then commit the reference folder on
the slice's source branch.

Exit code 0 means that the reference was saved; 2 means that it was not.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $RunDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap {
    Write-Error -ErrorRecord $_ -ErrorAction Continue
    exit 2
}
Import-Module (Join-Path $PSScriptRoot 'SpecGateRepro.psm1') -Force

$layout = Get-ReproLayout
$RunDir = (Resolve-Path -LiteralPath $RunDir).Path
$slice = Save-SliceReferenceFromRun -Layout $layout -RunDir $RunDir
Write-Host "Saved the reference for $($slice.Name) to $($slice.ReferenceDir)."
