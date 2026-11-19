#Requires -Version 7.2
# Shared helpers for the specgate-spatial-repro scripts.
#
# Expected folder layout (the slice bindings use relative paths):
#   <parent>\odata.net   this fork, branch spike/specgate-spatial-coordinated
#   <parent>\odata-rs    the odata-rs slice branch
#   <parent>\specgate    SpecGate source build (default, can be overridden)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ReproLayout {
    param(
        [string] $SpecGateRoot,
        [string] $FeedPath
    )

    $forkRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $forkRoot) {
        throw "Cannot find the odata.net repository root from $PSScriptRoot."
    }
    $forkRoot = (Resolve-Path $forkRoot).Path
    $parent = Split-Path $forkRoot -Parent

    if (-not $SpecGateRoot) { $SpecGateRoot = Join-Path $parent 'specgate' }
    if (-not $FeedPath) { $FeedPath = Join-Path $parent 'specgate-feed' }
    $SpecGateRoot = [IO.Path]::GetFullPath($SpecGateRoot)

    [pscustomobject]@{
        ForkRoot     = $forkRoot
        Parent       = $parent
        OdataRs      = Join-Path $parent 'odata-rs'
        SpecGateRoot = $SpecGateRoot
        FeedPath     = [IO.Path]::GetFullPath($FeedPath)
        SpecGateExe  = Join-Path $SpecGateRoot 'rust\target\release\specgate.exe'
        CaptureRoot  = Join-Path $forkRoot 'specgate-capture'
    }
}

function Assert-SpecGateInstalled {
    param([Parameter(Mandatory)] $Layout)
    if (-not (Test-Path $Layout.SpecGateExe)) {
        throw "SpecGate CLI not found at $($Layout.SpecGateExe). Run Install-SpecGate.ps1 first."
    }
}

function Get-AnnotationsVersion {
    param([Parameter(Mandatory)] $Layout)
    $csproj = Join-Path $Layout.CaptureRoot 'Microsoft.Spatial.CoordinateSystem.Capture.csproj'
    $match = Select-String -Path $csproj -Pattern 'Include="SpecGate.Annotations"\s+Version="([^"]+)"'
    if (-not $match) { throw "No SpecGate.Annotations reference found in $csproj." }
    $match.Matches[0].Groups[1].Value
}

function Get-PinnedSpecGateDependency {
    # Reads the SpecGate Git dependency that odata-rs pins in its workspace Cargo.toml.
    param([Parameter(Mandatory)] $Layout)
    $cargoToml = Join-Path $Layout.OdataRs 'Cargo.toml'
    if (-not (Test-Path $cargoToml)) {
        throw "odata-rs not found at $($Layout.OdataRs). Clone it beside odata.net."
    }
    $line = Select-String -Path $cargoToml -Pattern '^\s*specgate\s*=\s*\{.*git\s*=\s*"([^"]+)".*rev\s*=\s*"([0-9a-f]{40})"'
    if (-not $line) { throw "No Git-pinned specgate dependency found in $cargoToml. Check out the odata-rs slice branch." }
    [pscustomobject]@{
        GitUrl = $line.Matches[0].Groups[1].Value
        Rev    = $line.Matches[0].Groups[2].Value
    }
}

function Get-SliceInfo {
    param(
        [Parameter(Mandatory)] $Layout,
        [Parameter(Mandatory)] [string] $Slice
    )

    $sliceDir = Join-Path $Layout.CaptureRoot "slices\$Slice"
    $binding = Join-Path $sliceDir 'specgate.yaml'
    if (-not (Test-Path $binding)) {
        $known = (Get-ChildItem (Join-Path $Layout.CaptureRoot 'slices') -Directory).Name -join ', '
        throw "No binding at $binding. Known slices: $known."
    }

    # The slice bindings have a fixed shape: one component, with csharp and rust
    # implementations, each listing its test projects. This parser handles only that shape.
    $component = $null
    $implementation = $null
    $projects = @{}
    $frameworks = @{}
    foreach ($line in Get-Content $binding) {
        if (-not $component -and $line -match '^  ([A-Za-z0-9_.\-]+):\s*$') { $component = $Matches[1]; continue }
        if ($line -match '^      ([A-Za-z0-9_\-]+):\s*$') { $implementation = $Matches[1]; continue }
        if ($implementation -and $line -match '^\s+-\s+(\S+\.(csproj|toml))\s*$') {
            $projects[$implementation] = [IO.Path]::GetFullPath((Join-Path $sliceDir $Matches[1]))
            continue
        }
        if ($implementation -and $line -match '^\s+framework:\s*(\S+)\s*$') { $frameworks[$implementation] = $Matches[1] }
    }
    if (-not $component) { throw "No component found in $binding." }
    if (-not $projects.ContainsKey('csharp')) { throw "No csharp test project found in $binding." }

    $referenceDir = Join-Path $sliceDir 'reference'
    $manifest = Get-Content (Join-Path $referenceDir 'manifest.json') -Raw | ConvertFrom-Json

    $framework = 'net8.0'
    if ($frameworks.ContainsKey('csharp')) { $framework = $frameworks['csharp'] }

    [pscustomobject]@{
        Name              = $Slice
        Dir               = $sliceDir
        Binding           = $binding
        Component         = $component
        CSharpTestProject = $projects['csharp']
        CSharpFramework   = $framework
        ReferenceDir      = $referenceDir
        ReferenceTrace    = Join-Path $referenceDir 'reference.otlp.json'
        ReferenceRegistry = Join-Path $referenceDir 'registry.ctsc.json'
        RegistryId        = $manifest.registry.id
        RegistryVersion   = $manifest.registry.version
    }
}

function Invoke-Logged {
    # Runs a native command in a folder, writes its combined output to a log file,
    # echoes it, and returns the exit code.
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string] $LogPath
    )
    Write-Host "> $([IO.Path]::GetFileName($FilePath)) $($Arguments -join ' ')"
    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $LogPath | Write-Host
        return $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

function Set-AnnotationsFeed {
    # The fork references a SpecGate.Annotations version that is not on nuget.org.
    # Restore finds it in the local feed that Install-SpecGate.ps1 creates.
    param([Parameter(Mandatory)] $Layout)
    $version = Get-AnnotationsVersion -Layout $Layout
    $package = Join-Path $Layout.FeedPath "SpecGate.Annotations.$version.nupkg"
    if (-not (Test-Path $package)) {
        throw "Package $package not found. Run Install-SpecGate.ps1 first."
    }
    $env:RestoreAdditionalProjectSources = $Layout.FeedPath
}

function New-OutputDirectory {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [switch] $Force
    )
    if ((Test-Path $Path) -and (Get-ChildItem $Path -Force | Select-Object -First 1)) {
        if (-not $Force) { throw "Output folder $Path is not empty. Use -Force or choose another -OutDir." }
        Remove-Item $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    (Resolve-Path $Path).Path
}

Export-ModuleMember -Function Get-ReproLayout, Assert-SpecGateInstalled, Get-AnnotationsVersion,
    Get-PinnedSpecGateDependency, Get-SliceInfo, Invoke-Logged, Set-AnnotationsFeed, New-OutputDirectory
