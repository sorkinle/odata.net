#Requires -Version 7.2
# Shared helpers for the specgate-spatial-repro scripts.
#
# Expected folder layout (the slice bindings use relative paths):
#   <parent>\odata.net   this fork, at the slice's source branch
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

function Get-SpecGatePin {
    # specgate-capture\specgate-pin.json is the single record of the SpecGate commit
    # and the SpecGate.Annotations version that every slice must use.
    param([Parameter(Mandatory)] $Layout)
    $path = Join-Path $Layout.CaptureRoot 'specgate-pin.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "SpecGate pin file not found: $path."
    }
    $pin = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $commit = [string] $pin.specgateCommit
    $version = [string] $pin.annotationsVersion
    if ($commit -notmatch '^[0-9a-f]{40}$') { throw "specgateCommit in $path must be a 40-character commit hash." }
    if ($version -notmatch "-g$($commit.Substring(0, 8))$") {
        throw "annotationsVersion $version in $path does not name commit $($commit.Substring(0, 8))."
    }
    [pscustomobject]@{ Commit = $commit; AnnotationsVersion = $version; Path = $path }
}

function Get-AnnotationsVersion {
    param([Parameter(Mandatory)] $Layout)
    (Get-SpecGatePin -Layout $Layout).AnnotationsVersion
}

function Assert-AnnotationsVersion {
    # Fails unless every SpecGate.Annotations reference in the given projects uses the
    # pinned version, and at least one project references the package.
    param(
        [Parameter(Mandatory)] $Layout,
        [Parameter(Mandatory)] [string[]] $Projects
    )
    $expected = Get-AnnotationsVersion -Layout $Layout
    $found = 0
    foreach ($project in $Projects) {
        foreach ($match in Select-String -LiteralPath $project -Pattern 'Include="SpecGate\.Annotations"\s+Version="([^"]*)"') {
            $found++
            $actual = $match.Matches[0].Groups[1].Value
            if ($actual -ne $expected) {
                throw "$project references SpecGate.Annotations $actual, but specgate-pin.json pins $expected."
            }
        }
    }
    if ($found -eq 0) {
        throw "No SpecGate.Annotations reference found in: $($Projects -join ', ')."
    }
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

function Get-PublishedSliceName {
    param(
        [Parameter(Mandatory)] $Layout,
        [string] $Slice,
        [string] $PullRequest
    )

    if ($Slice -and $PullRequest) {
        throw 'Use either -PullRequest or -Slice, not both.'
    }
    if ($Slice) {
        if ($Slice -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
            throw "Invalid slice name: $Slice."
        }
        return $Slice
    }

    $slicesDir = Join-Path $Layout.CaptureRoot 'slices'
    if (-not (Test-Path -LiteralPath $slicesDir -PathType Container)) {
        throw "No published references found in $slicesDir. A binding and captured reference are required for replay."
    }

    if ($PullRequest) {
        if ($PullRequest -match '^#?([1-9][0-9]*)$') {
            $prNumber = $Matches[1]
        }
        else {
            [uri] $prUri = $null
            if (-not [uri]::TryCreate($PullRequest, [UriKind]::Absolute, [ref] $prUri) -or
                $prUri.Scheme -ne 'https' -or $prUri.Host -ne 'github.com') {
                throw "Expected an OData/odata-rs pull request URL or number; received $PullRequest."
            }
            $pathMatch = [regex]::Match($prUri.AbsolutePath, '^/OData/odata-rs/pull/([1-9][0-9]*)(?:/.*)?$',
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            if (-not $pathMatch.Success) {
                throw "Expected an OData/odata-rs pull request URL or number; received $PullRequest."
            }
            $prNumber = $pathMatch.Groups[1].Value
        }
        $prPath = "/OData/odata-rs/pull/$prNumber"
        $guide = Join-Path $Layout.CaptureRoot 'REPRODUCE.md'
        if (-not (Test-Path -LiteralPath $guide -PathType Leaf)) {
            throw "Cannot find the reference version table at $guide."
        }

        $matches = @(
            foreach ($line in Get-Content -LiteralPath $guide) {
                $columns = $line -split '\|'
                if ($columns.Count -lt 6 -or
                    $columns[2] -notmatch '^\s*`slices[\\/]([A-Za-z0-9][A-Za-z0-9._-]*)`\s*$') {
                    continue
                }
                $name = $Matches[1]
                $links = [regex]::Matches($columns[3], '\]\((https://github\.com/OData/odata-rs/pull/[1-9][0-9]*/?(?:[?#][^)]*)?)\)',
                    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
                foreach ($link in $links) {
                    $publishedUri = [uri] $link.Groups[1].Value
                    if ($publishedUri.AbsolutePath.TrimEnd('/') -ieq $prPath) {
                        $name
                        break
                    }
                }
            }
        )
        if ($matches.Count -ne 1) {
            throw "Expected one published reference for $PullRequest in $guide; found $($matches.Count). Do not select a reference by guesswork."
        }
        return $matches[0]
    }

    $available = @(
        Get-ChildItem -LiteralPath $slicesDir -Directory | Where-Object {
            (Test-Path -LiteralPath (Join-Path $_.FullName 'specgate.yaml') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'reference\manifest.json') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'reference\reference.otlp.json') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'reference\registry.ctsc.json') -PathType Leaf)
        }
    )
    if ($available.Count -ne 1) {
        throw "Found $($available.Count) complete published references in $slicesDir. Provide the target Rust PR URL with -PullRequest."
    }
    return $available[0].Name
}

function Read-BindingComponents {
    # Reads the parts of a slice binding that the scripts use. The bindings have a
    # fixed shape:
    #   components:
    #     <component>:
    #       implementations:
    #         <implementation>:
    #           sources: <path>
    #           tests:
    #             projects:
    #               - <path>
    #           csharp:
    #             framework: <tfm>
    # Returns a hashtable: component -> implementation -> @{ Sources; Projects; Framework }.
    param([Parameter(Mandatory)] [string] $Binding)

    $components = [ordered]@{}
    $component = $null
    $implementation = $null
    foreach ($line in Get-Content -LiteralPath $Binding) {
        if ($line -match '^  ([A-Za-z0-9_.\-]+):\s*$') {
            $component = $Matches[1]
            $implementation = $null
            if ($components.Contains($component)) { throw "Component $component appears twice in $Binding." }
            $components[$component] = [ordered]@{}
            continue
        }
        if (-not $component) { continue }
        if ($line -match '^      ([A-Za-z0-9_\-]+):\s*$') {
            $implementation = $Matches[1]
            $components[$component][$implementation] = @{
                Sources   = $null
                Projects  = [Collections.Generic.List[string]]::new()
                Framework = $null
            }
            continue
        }
        if (-not $implementation) { continue }
        $entry = $components[$component][$implementation]
        if ($line -match '^        sources:\s*(\S+)\s*$') { $entry.Sources = $Matches[1]; continue }
        if ($line -match '^\s+-\s+(\S+\.(csproj|toml))\s*$') { $entry.Projects.Add($Matches[1]); continue }
        if ($line -match '^\s+framework:\s*(\S+)\s*$') { $entry.Framework = $Matches[1] }
    }
    $components
}

function Get-SliceBinding {
    # Loads a slice binding without requiring a saved reference. When the binding has
    # several components, -Component must name one; the scripts never guess.
    param(
        [Parameter(Mandatory)] $Layout,
        [Parameter(Mandatory)] [string] $Slice,
        [string] $Component
    )

    if ($Slice -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
        throw "Invalid slice name: $Slice."
    }
    $sliceDir = Join-Path $Layout.CaptureRoot "slices\$Slice"
    $binding = Join-Path $sliceDir 'specgate.yaml'
    if (-not (Test-Path -LiteralPath $binding -PathType Leaf)) {
        throw "The slice binding is missing: $binding."
    }

    $components = Read-BindingComponents -Binding $binding
    if ($components.Count -eq 0) { throw "No component found in $binding." }
    if ($Component) {
        if (-not $components.Contains($Component)) {
            throw "Component $Component is not in $binding. Components: $(@($components.Keys) -join ', ')."
        }
    }
    elseif ($components.Count -gt 1) {
        throw "$binding has several components: $(@($components.Keys) -join ', '). Select one with -Component."
    }
    else {
        $Component = @($components.Keys)[0]
    }

    $csharp = $components[$Component]['csharp']
    if (-not $csharp -or $csharp.Projects.Count -eq 0) {
        throw "No csharp test project found for component $Component in $binding."
    }
    if ($csharp.Projects.Count -gt 1) {
        throw "Component $Component lists $($csharp.Projects.Count) csharp test projects in $binding. The capture gate supports one."
    }
    $framework = 'net8.0'
    if ($csharp.Framework) { $framework = $csharp.Framework }
    $sources = $null
    if ($csharp.Sources) { $sources = [IO.Path]::GetFullPath((Join-Path $sliceDir $csharp.Sources)) }

    [pscustomobject]@{
        Name              = $Slice
        Dir               = $sliceDir
        Binding           = $binding
        Component         = $Component
        CSharpTestProject = [IO.Path]::GetFullPath((Join-Path $sliceDir $csharp.Projects[0]))
        CSharpFramework   = $framework
        CSharpSources     = $sources
        ReferenceDir      = Join-Path $sliceDir 'reference'
    }
}

function Assert-NoSavedReference {
    # A first capture must not replace a saved reference, even a partial one.
    param([Parameter(Mandatory)] $SliceBinding)
    $dir = $SliceBinding.ReferenceDir
    if (Test-Path -LiteralPath $dir) {
        throw "$dir already exists. Only a slice without a reference folder can record a first reference, and a saved reference is never overwritten. Run Invoke-SliceCapture.ps1 without -Bootstrap to check a fresh capture against the saved reference."
    }
}

function Get-SliceInfo {
    # Loads a slice binding and its complete saved reference. Discover, capture, and
    # replay use this unless they run with -Bootstrap.
    param(
        [Parameter(Mandatory)] $Layout,
        [string] $Slice,
        [string] $PullRequest,
        [string] $Component
    )

    $Slice = Get-PublishedSliceName -Layout $Layout -Slice $Slice -PullRequest $PullRequest
    $sliceDir = Join-Path $Layout.CaptureRoot "slices\$Slice"
    $referenceDir = Join-Path $sliceDir 'reference'
    $requiredFiles = @(
        (Join-Path $sliceDir 'specgate.yaml'),
        (Join-Path $referenceDir 'manifest.json'),
        (Join-Path $referenceDir 'reference.otlp.json'),
        (Join-Path $referenceDir 'registry.ctsc.json')
    )
    foreach ($path in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "The published reference is incomplete: $path is missing. A binding and captured reference are required. To record a first reference, use -Bootstrap."
        }
    }

    $manifest = Get-Content (Join-Path $referenceDir 'manifest.json') -Raw | ConvertFrom-Json
    # The saved reference records its component. Use it, so that the scripts check
    # the same component that the reference contains.
    $recordedComponent = $null
    if ($manifest.PSObject.Properties['componentId']) { $recordedComponent = [string] $manifest.componentId }
    if ($Component -and $recordedComponent -and $Component -ne $recordedComponent) {
        throw "The saved reference for $Slice records component $recordedComponent, not $Component."
    }
    if (-not $Component) { $Component = $recordedComponent }

    $info = Get-SliceBinding -Layout $Layout -Slice $Slice -Component $Component
    $info | Add-Member -NotePropertyMembers ([ordered]@{
        ReferenceTrace    = Join-Path $referenceDir 'reference.otlp.json'
        ReferenceRegistry = Join-Path $referenceDir 'registry.ctsc.json'
        RegistryId        = $manifest.registry.id
        RegistryVersion   = $manifest.registry.version
    })
    $info
}

function Get-CaptureGateErrors {
    # Returns one message for each configured test without exactly one Passed result,
    # and for each result that does not belong to a configured test.
    # Names are compared case-sensitively: theory cases such as Parse("point") and
    # Parse("POINT") are different tests.
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ConfiguredTests,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Results
    )
    $errors = [Collections.Generic.List[string]]::new()
    if ($ConfiguredTests.Count -eq 0) { $errors.Add('No configured tests.') }
    foreach ($test in $ConfiguredTests) {
        $matching = @($Results | Where-Object Test -ceq $test)
        if ($matching.Count -eq 0) { $errors.Add("No result: $test") }
        elseif ($matching.Count -gt 1) { $errors.Add("$($matching.Count) results: $test") }
        elseif ($matching[0].Outcome -cne 'Passed') { $errors.Add("$($matching[0].Outcome): $test") }
    }
    foreach ($result in $Results | Where-Object { $ConfiguredTests -cnotcontains $_.Test }) {
        $errors.Add("Unexpected result ($($result.Outcome)): $($result.Test)")
    }
    , $errors.ToArray()
}

function Enter-CaptureLock {
    # Only one capture may run on a machine at a time. The capture script collects
    # .trx files from every new specgate-capture-* temporary folder, so a second
    # capture would mix its test results into the first one's gate.
    param([string] $Name = 'Global\SpecGateSpatialCapture')
    $mutex = [Threading.Mutex]::new($false, $Name)
    $acquired = $false
    try {
        $acquired = $mutex.WaitOne(0)
    }
    catch [Threading.AbandonedMutexException] {
        # A previous capture process ended without releasing the lock. This process now owns it.
        $acquired = $true
    }
    if (-not $acquired) {
        $mutex.Dispose()
        throw 'Another SpecGate capture is running on this machine. Wait for it to finish, then run this capture again.'
    }
    $mutex
}

function Exit-CaptureLock {
    param([Parameter(Mandatory)] [Threading.Mutex] $Lock)
    $Lock.ReleaseMutex()
    $Lock.Dispose()
}

function Get-BundleFileHashes {
    # Returns SHA-256 hashes for the manifest and the two files it names. These are
    # the files that a saved reference contains.
    param([Parameter(Mandatory)] [string] $BundleDir)
    $manifestPath = Join-Path $BundleDir 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "No manifest.json in $BundleDir." }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $names = @('manifest.json', [string] $manifest.registry.path, [string] $manifest.reference.path)
    $hashes = [ordered]@{}
    foreach ($name in $names) {
        if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { throw "Unexpected bundle file name in $manifestPath`: $name." }
        $path = Join-Path $BundleDir $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Bundle file missing: $path." }
        $hashes[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    $hashes
}

function Save-SliceReferenceFromRun {
    # Copies a capture run's bundle into the slice's reference folder. Refuses unless
    # the run passed the test gate and bundle validation, the bundle files are
    # unchanged since that run, and the slice has no saved reference.
    param(
        [Parameter(Mandatory)] $Layout,
        [Parameter(Mandatory)] [string] $RunDir
    )
    $resultPath = Join-Path $RunDir 'capture-result.json'
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw "No capture-result.json in $RunDir. Only a run of Invoke-SliceCapture.ps1 that passed the test gate and bundle validation can be saved."
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($result.format -ne 'specgate-spatial-capture-result' -or $result.gatePassed -ne $true -or $result.bundleValid -ne $true) {
        throw "The run in $RunDir did not pass the test gate and bundle validation. It cannot be saved."
    }

    $slice = Get-SliceBinding -Layout $Layout -Slice ([string] $result.slice) -Component ([string] $result.component)
    Assert-NoSavedReference -SliceBinding $slice

    $bundleDir = Join-Path $RunDir 'capture'
    $current = Get-BundleFileHashes -BundleDir $bundleDir
    $recorded = $result.bundleFiles
    $recordedNames = @($recorded.PSObject.Properties.Name)
    if ($recordedNames.Count -ne $current.Count) { throw "The bundle in $bundleDir changed after the capture run." }
    foreach ($name in $current.Keys) {
        if (-not $recorded.PSObject.Properties[$name] -or $recorded.$name -ne $current[$name]) {
            throw "The bundle in $bundleDir changed after the capture run: $name."
        }
    }

    # Copy and verify in a staging folder, then rename it to reference. A failed or
    # mismatched copy must never leave a reference folder that later runs would trust.
    $stagingDir = "$($slice.ReferenceDir).saving-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $stagingDir | Out-Null
    try {
        foreach ($name in $current.Keys) {
            [IO.File]::Copy((Join-Path $bundleDir $name), (Join-Path $stagingDir $name), $false)
        }
        $saved = Get-BundleFileHashes -BundleDir $stagingDir
        foreach ($name in $current.Keys) {
            if ($saved[$name] -ne $current[$name]) { throw "The copy of $name does not match the gated bundle in $bundleDir." }
        }
        # Directory.Move is a rename on the same volume and fails if reference already exists.
        [IO.Directory]::Move($stagingDir, $slice.ReferenceDir)
    }
    finally {
        if (Test-Path -LiteralPath $stagingDir) { Remove-Item -LiteralPath $stagingDir -Recurse -Force }
    }
    $slice
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
    if ((Test-Path -LiteralPath $Path) -and -not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Output path $Path is not a directory."
    }
    if ((Test-Path -LiteralPath $Path) -and (Get-ChildItem -LiteralPath $Path -Force | Select-Object -First 1)) {
        if (-not $Force) { throw "Output folder $Path is not empty. Use -Force or choose another -OutDir." }
        $runName = "run-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$([guid]::NewGuid().ToString('N'))"
        $Path = Join-Path $Path $runName
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    Write-Host "Output folder: $resolvedPath"
    return $resolvedPath
}

function Invoke-WithCargoPatch {
    param(
        [Parameter(Mandatory)] $Layout,
        [Parameter(Mandatory)] $Pinned,
        [Parameter(Mandatory)] [string] $OutDir,
        [Parameter(Mandatory)] [scriptblock] $Action
    )

    $cargoDir = Join-Path $Layout.Parent '.cargo'
    $cargoConfig = Join-Path $cargoDir 'config.toml'
    $legacyConfig = Join-Path $cargoDir 'config'
    foreach ($path in @($cargoConfig, $legacyConfig)) {
        if (Test-Path -LiteralPath $path) {
            throw "$path already exists. This script does not replace or merge Cargo configuration."
        }
    }

    $lockFile = Join-Path $Layout.OdataRs 'Cargo.lock'
    $lockBackup = Join-Path $OutDir 'Cargo.lock.before-replay'
    $lockAfter = Join-Path $OutDir 'Cargo.lock.after-replay'
    [IO.File]::Copy($lockFile, $lockBackup, $false)
    $originalLock = [IO.File]::ReadAllBytes($lockBackup)
    $originalLockHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($originalLock))

    $patchedCrate = (Join-Path $Layout.SpecGateRoot 'rust\crates\specgate') -replace '\\', '/'
    $patch = @"
# Temporary patch written by Invoke-SliceReplay.ps1. It is removed after replay.
[patch."$($Pinned.GitUrl)"]
specgate = { path = "$patchedCrate" }
"@
    $patchBytes = [Text.UTF8Encoding]::new($false).GetBytes($patch)
    $patchHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($patchBytes))
    $createdCargoDir = -not (Test-Path -LiteralPath $cargoDir)
    $createdConfig = $false
    $replayStarted = $false
    $actionError = $null
    $cleanupErrors = [Collections.Generic.List[string]]::new()

    try {
        [void] [IO.Directory]::CreateDirectory($cargoDir)
        $configStream = [IO.File]::Open($cargoConfig, [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::None)
        $createdConfig = $true
        try {
            $configStream.Write($patchBytes, 0, $patchBytes.Length)
        }
        finally {
            $configStream.Dispose()
        }
        if (Test-Path -LiteralPath $legacyConfig) {
            throw "$legacyConfig appeared before replay. This script does not replace or merge Cargo configuration."
        }
        $replayStarted = $true
        & $Action
    }
    catch {
        $actionError = $_
    }
    finally {
        if ($createdConfig) {
            try {
                if (-not (Test-Path -LiteralPath $cargoConfig -PathType Leaf)) {
                    throw "The temporary Cargo patch is missing or is no longer a file: $cargoConfig."
                }
                if ((Get-FileHash -LiteralPath $cargoConfig).Hash -ne $patchHash) {
                    throw "The temporary Cargo patch changed during replay and was preserved: $cargoConfig."
                }
                Remove-Item -LiteralPath $cargoConfig
            }
            catch {
                $cleanupErrors.Add($_.Exception.Message)
            }
        }
        if ($createdCargoDir -and (Test-Path -LiteralPath $cargoDir)) {
            try {
                if (Get-ChildItem -LiteralPath $cargoDir -Force | Select-Object -First 1) {
                    Write-Warning "Leaving $cargoDir because it contains other files."
                }
                else {
                    [IO.Directory]::Delete($cargoDir)
                }
            }
            catch {
                $cleanupErrors.Add("Could not remove the temporary Cargo directory ${cargoDir}: $($_.Exception.Message)")
            }
        }

        $lockRestored = $false
        try {
            # A post-replay lockfile can contain concurrent edits. Save it before restoring
            # the original, and hold the file exclusively through both operations.
            $lockStream = [IO.File]::Open($lockFile, [IO.FileMode]::Open,
                [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            try {
                $currentLockHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($lockStream))
                if ($currentLockHash -ne $originalLockHash) {
                    if (-not $replayStarted) {
                        throw 'Cargo.lock changed before replay started; the current file was not changed.'
                    }
                    $afterStream = [IO.File]::Open($lockAfter, [IO.FileMode]::CreateNew,
                        [IO.FileAccess]::Write, [IO.FileShare]::None)
                    try {
                        $lockStream.Position = 0
                        $lockStream.CopyTo($afterStream)
                    }
                    finally {
                        $afterStream.Dispose()
                    }
                    Write-Host "Saved the lockfile observed after replay: $lockAfter"
                    $lockStream.Position = 0
                    $lockStream.SetLength(0)
                    $lockStream.Write($originalLock, 0, $originalLock.Length)
                    $lockStream.Flush()
                }
                $lockStream.Position = 0
                $restoredHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($lockStream))
                if ($restoredHash -ne $originalLockHash) {
                    throw 'Restored Cargo.lock does not match the original.'
                }
                $lockRestored = $true
            }
            finally {
                $lockStream.Dispose()
            }
        }
        catch {
            $cleanupErrors.Add("Cargo.lock cleanup failed: $($_.Exception.Message) Original backup: $lockBackup.")
        }

        if ($lockRestored) {
            try {
                Remove-Item -LiteralPath $lockBackup
            }
            catch {
                $cleanupErrors.Add("Could not remove the lockfile backup ${lockBackup}: $($_.Exception.Message)")
            }
        }
        if ($cleanupErrors.Count -gt 0) {
            $message = "Cargo cleanup failed:`n- $($cleanupErrors -join "`n- ")"
            if ($actionError) { $message = "Replay failed: $($actionError.Exception.Message)`n$message" }
            throw $message
        }
    }
    if ($actionError) { throw $actionError }
    Write-Host 'Removed the temporary Cargo patch and restored odata-rs\Cargo.lock.'
}

Export-ModuleMember -Function Get-ReproLayout, Assert-SpecGateInstalled, Get-SpecGatePin,
    Get-AnnotationsVersion, Assert-AnnotationsVersion, Get-PinnedSpecGateDependency, Get-SliceBinding,
    Assert-NoSavedReference, Get-SliceInfo, Get-CaptureGateErrors, Enter-CaptureLock, Exit-CaptureLock,
    Get-BundleFileHashes, Save-SliceReferenceFromRun, Invoke-Logged, Set-AnnotationsFeed,
    New-OutputDirectory, Invoke-WithCargoPatch
