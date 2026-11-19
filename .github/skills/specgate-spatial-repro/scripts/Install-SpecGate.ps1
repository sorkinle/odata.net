#Requires -Version 7.2
<#
.SYNOPSIS
Builds SpecGate from source at the commit that odata-rs pins.

.DESCRIPTION
Clones SpecGate into -SpecGateRoot (default <parent>\specgate), checks out
the commit from the odata-rs SpecGate dependency, builds the CLI and the C#
discovery and weaver tools, and puts the tools beside the CLI. It then packs
SpecGate.Annotations from the same commit into -FeedPath (default
<parent>\specgate-feed), with the version that the fork projects reference.

An existing clone is reused only if it is already at the pinned commit and
has no tracked changes. The script does not move an existing clone to
another commit.
#>
[CmdletBinding()]
param(
    [string] $SpecGateRoot,
    [string] $FeedPath,
    [string] $Commit,
    [string] $RepositoryUrl = 'https://github.com/schgoo/specgate'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SpecGateRepro.psm1') -Force

function Invoke-Checked([string] $FilePath, [string[]] $Arguments, [string] $WorkingDirectory) {
    Write-Host "> $FilePath $($Arguments -join ' ')"
    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$FilePath $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
    }
}

$layout = Get-ReproLayout -SpecGateRoot $SpecGateRoot -FeedPath $FeedPath
$pinned = Get-PinnedSpecGateDependency -Layout $layout
if (-not $Commit) { $Commit = $pinned.Rev }
if ($Commit -ne $pinned.Rev) {
    Write-Warning "Commit $Commit differs from the odata-rs pin $($pinned.Rev). Replay will not match the published evidence."
}

$annotationsVersion = Get-AnnotationsVersion -Layout $layout
if ($annotationsVersion -notmatch "-g$($Commit.Substring(0, 8))$") {
    throw "The fork references SpecGate.Annotations $annotationsVersion, which does not name commit $($Commit.Substring(0, 8))."
}

$root = $layout.SpecGateRoot
if (-not (Test-Path $root)) {
    Invoke-Checked 'git' @('clone', '--quiet', $RepositoryUrl, $root) $layout.Parent
    Invoke-Checked 'git' @('-c', 'advice.detachedHead=false', 'checkout', '--quiet', $Commit) $root
}
else {
    $head = (& git -C $root rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "$root exists but is not a Git repository." }
    if ($head -ne $Commit) {
        throw "$root is at $head, not $Commit. Use -SpecGateRoot with a new folder, or check out $Commit yourself."
    }
    if (& git -C $root status --porcelain --untracked-files=no) {
        throw "$root has tracked changes. Use a clean clone so the build matches the pinned commit."
    }
    Write-Host "Reusing $root at $Commit."
}

Invoke-Checked 'cargo' @('build', '--release', '-p', 'specgate-cli') (Join-Path $root 'rust')
Invoke-Checked 'just' @('discovery-tool') $root
Invoke-Checked 'just' @('weaver-tool') $root

# The CLI looks for the discovery and weaver tools beside its own executable.
$binDir = Split-Path $layout.SpecGateExe -Parent
Copy-Item (Join-Path $root 'artifacts\discovery\*') $binDir -Recurse -Force
Copy-Item (Join-Path $root 'artifacts\weaver\*') $binDir -Recurse -Force

New-Item -ItemType Directory -Path $layout.FeedPath -Force | Out-Null
$annotationsProject = Join-Path $root 'csharp\SpecGate.Annotations\SpecGate.Annotations.csproj'
Invoke-Checked 'dotnet' @('pack', $annotationsProject, '-c', 'Release', '-o', $layout.FeedPath, '--nologo',
    "-p:Version=$annotationsVersion", "-p:PackageVersion=$annotationsVersion") $root

# NuGet reuses a cached package with the same version, even if it came from another build.
$nugetRoot = (& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
$cached = Join-Path $nugetRoot "specgate.annotations\$annotationsVersion"
if (Test-Path $cached) {
    Write-Warning "NuGet already caches SpecGate.Annotations $annotationsVersion at $cached. Restore uses that copy. Delete it if it was built from another commit."
}

& $layout.SpecGateExe 2>&1 | Select-Object -First 1 | Write-Host
Write-Host ''
Write-Host "SpecGate CLI: $($layout.SpecGateExe)"
Write-Host "SpecGate commit: $Commit"
Write-Host "Annotations feed: $($layout.FeedPath) (SpecGate.Annotations $annotationsVersion)"
