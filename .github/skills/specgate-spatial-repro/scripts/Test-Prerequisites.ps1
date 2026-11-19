#Requires -Version 7.2
<#
.SYNOPSIS
Checks the tools that the SpecGate spatial reproduction needs.

.DESCRIPTION
Exits with code 1 when a tool that install and replay need is missing. The
.NET 8 runtime is needed only for capture and discovery. It fails the check
only when -ForCapture is set.
#>
[CmdletBinding()]
param(
    [switch] $ForCapture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$results = New-Object System.Collections.Generic.List[object]

function Add-Result([string] $Tool, [bool] $Found, [string] $Detail, [string] $NeededFor) {
    $results.Add([pscustomobject]@{ Tool = $Tool; Found = $Found; Detail = $Detail; NeededFor = $NeededFor })
}

function Get-CommandVersion([string] $Name, [string[]] $VersionArgs) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) { return $null }
    ((& $command.Source @VersionArgs 2>&1) | Select-Object -First 1) -as [string]
}

$git = Get-CommandVersion 'git' @('--version')
Add-Result 'git' ([bool]$git) $git 'all'

$cargo = Get-CommandVersion 'cargo' @('--version')
Add-Result 'cargo' ([bool]$cargo) $cargo 'all (SpecGate needs Rust 1.93 or later)'

$just = Get-CommandVersion 'just' @('--version')
Add-Result 'just' ([bool]$just) $just 'install (builds the SpecGate C# tools)'

$sdks = @()
$runtimes = @()
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $sdks = @(& dotnet --list-sdks 2>$null)
    $runtimes = @(& dotnet --list-runtimes 2>$null | Where-Object { $_ -like 'Microsoft.NETCore.App *' })
}
$sdk10 = @($sdks | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 10 })
Add-Result '.NET 10 SDK or later' ($sdk10.Count -gt 0) (($sdk10 | Select-Object -First 1) -as [string]) 'install, capture'

$runtime8 = @($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 8\.' })
Add-Result '.NET 8 runtime' ($runtime8.Count -gt 0) (($runtime8 | Select-Object -First 1) -as [string]) 'capture and discover (runs the C# tests)'

$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

$required = @('git', 'cargo', 'just', '.NET 10 SDK or later')
if ($ForCapture) { $required += '.NET 8 runtime' }
$missing = @($results | Where-Object { -not $_.Found -and $required -contains $_.Tool })
if ($missing.Count -gt 0) {
    Write-Host "Missing required tools: $(($missing.Tool) -join ', ')"
    exit 1
}
Write-Host 'All required tools are present.'
