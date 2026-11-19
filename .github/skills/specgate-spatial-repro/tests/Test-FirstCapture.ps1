#Requires -Version 7.2
# Tests for the first-capture support: the pin file, component selection, the test
# gate, saving a first reference, and the machine-wide capture lock.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $PSScriptRoot '..\scripts\SpecGateRepro.psm1'
Import-Module $modulePath -Force

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "specgate-first-capture-$([guid]::NewGuid().ToString('N'))"
$captureRoot = Join-Path $testRoot 'specgate-capture'
$layout = [pscustomobject]@{ CaptureRoot = $captureRoot }
$pinnedVersion = '0.6.0-g4a904c27'

function Assert-True([bool] $Condition, [string] $Case) {
    if (-not $Condition) { throw "${Case}: condition is false." }
    Write-Host "PASS $Case"
}

function Assert-Equal([string] $Expected, [string] $Actual, [string] $Case) {
    if ($Actual -cne $Expected) { throw "${Case}: expected '$Expected', got '$Actual'." }
    Write-Host "PASS $Case"
}

function Assert-Fails([scriptblock] $Action, [string] $Expected, [string] $Case) {
    try {
        & $Action | Out-Null
    }
    catch {
        if ($_.Exception.Message -notlike "*$Expected*") {
            throw "${Case}: unexpected error: $($_.Exception.Message)"
        }
        Write-Host "PASS $Case"
        return
    }
    throw "${Case}: expected an error containing '$Expected'."
}

function Set-Pin([string] $Commit, [string] $Version) {
    @{ specgateCommit = $Commit; annotationsVersion = $Version } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $captureRoot 'specgate-pin.json') -Encoding utf8
}

function New-Project([string] $Name, [string[]] $Versions) {
    $path = Join-Path $testRoot $Name
    $references = ($Versions | ForEach-Object { "    <PackageReference Include=`"SpecGate.Annotations`" Version=`"$_`" />" }) -join "`n"
    "<Project>`n  <ItemGroup>`n$references`n  </ItemGroup>`n</Project>" | Set-Content -LiteralPath $path -Encoding utf8
    $path
}

function New-Slice([string] $Name, [string[]] $Components, [int] $TestProjects = 1) {
    $sliceDir = Join-Path $captureRoot "slices\$Name"
    New-Item -ItemType Directory -Path $sliceDir -Force | Out-Null
    $lines = @('components:')
    foreach ($component in $Components) {
        $lines += "  ${component}:", '    implementations:', '      csharp:', '        language: csharp',
            '        sources: ../../../src/Microsoft.Spatial', '        tests:', '          projects:'
        $lines += 1..$TestProjects | ForEach-Object { "            - ../../$component.$_.csproj" }
        $lines += '        csharp:', '          framework: net8.0', '      rust:', '        language: rust', '        tests:',
            '          projects:', '            - ../../../../odata-rs/Cargo.toml'
    }
    $lines | Set-Content -LiteralPath (Join-Path $sliceDir 'specgate.yaml') -Encoding utf8
    $sliceDir
}

function New-PassedRun([string] $Slice, [string] $Component) {
    $runDir = Join-Path $testRoot "run-$([guid]::NewGuid().ToString('N'))"
    $bundleDir = Join-Path $runDir 'capture'
    New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null
    @{ componentId = $Component; registry = @{ path = 'registry.ctsc.json'; id = "urn:ctsc:registry:$Component"; version = '0.1.0' }
        reference = @{ path = 'reference.otlp.json' } } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $bundleDir 'manifest.json') -Encoding utf8
    '{"spans":[]}' | Set-Content -LiteralPath (Join-Path $bundleDir 'reference.otlp.json') -Encoding utf8
    '{"operations":[]}' | Set-Content -LiteralPath (Join-Path $bundleDir 'registry.ctsc.json') -Encoding utf8
    [ordered]@{
        format = 'specgate-spatial-capture-result'; slice = $Slice; component = $Component; bootstrap = $true
        gatePassed = $true; bundleValid = $true; configuredTests = 1
        bundleFiles = Get-BundleFileHashes -BundleDir $bundleDir
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDir 'capture-result.json') -Encoding utf8
    $runDir
}

function Start-LockHolder([string] $Name, [string] $ReadyFile, [string] $StopFile, [switch] $Abandon) {
    # A mutex is reentrant on its owning thread, so the competing owner must be another process.
    $release = if ($Abandon) { '' } else { 'Exit-CaptureLock -Lock $lock' }
    $script = @"
Import-Module '$modulePath' -Force
`$lock = Enter-CaptureLock -Name '$Name'
Set-Content -LiteralPath '$ReadyFile' -Value ready
while (-not (Test-Path -LiteralPath '$StopFile')) { Start-Sleep -Milliseconds 50 }
$release
"@
    $process = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', $script) -PassThru -NoNewWindow
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path -LiteralPath $ReadyFile)) {
        if ($process.HasExited -or (Get-Date) -gt $deadline) { throw 'The lock holder process did not start.' }
        Start-Sleep -Milliseconds 50
    }
    $process
}

try {
    New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null

    # Pin file.
    Assert-Fails { Get-AnnotationsVersion -Layout $layout } 'pin file not found' 'Missing pin file'
    Set-Pin '4a904c276e24177c84cf4fb7182c93e811137f9d' '0.6.0-g12345678'
    Assert-Fails { Get-AnnotationsVersion -Layout $layout } 'does not name commit' 'Pin version must name the pinned commit'
    Set-Pin '4a904c27' $pinnedVersion
    Assert-Fails { Get-AnnotationsVersion -Layout $layout } '40-character' 'Pin commit must be a full hash'
    Set-Pin '4a904c276e24177c84cf4fb7182c93e811137f9d' $pinnedVersion
    Assert-Equal $pinnedVersion (Get-AnnotationsVersion -Layout $layout) 'Read the annotations version from the pin file'

    # Annotations version in slice projects.
    $matching = New-Project 'matching.csproj' @($pinnedVersion)
    $other = New-Project 'other.csproj' @('0.5.0-gdeadbeef')
    $none = New-Project 'none.csproj' @()
    Assert-AnnotationsVersion -Layout $layout -Projects @($matching, $none)
    Write-Host 'PASS Accept projects that use the pinned version'
    Assert-Fails { Assert-AnnotationsVersion -Layout $layout -Projects @($matching, $other) } 'pins 0.6.0-g4a904c27' 'Reject another annotations version'
    Assert-Fails { Assert-AnnotationsVersion -Layout $layout -Projects @($none) } 'No SpecGate.Annotations reference' 'Reject projects without annotations'

    # Binding without a saved reference.
    New-Slice '02-single' @('spatial.single') | Out-Null
    $single = Get-SliceBinding -Layout $layout -Slice '02-single'
    Assert-Equal 'spatial.single' $single.Component 'Load a single-component binding without a reference'
    Assert-Equal 'net8.0' $single.CSharpFramework 'Read the csharp framework'
    Assert-True ($single.CSharpTestProject.EndsWith('spatial.single.1.csproj')) 'Read the csharp test project, not the rust project'
    Assert-True ($single.CSharpSources.EndsWith('src\Microsoft.Spatial')) 'Read the csharp sources path'
    Assert-Fails { Get-SliceInfo -Layout $layout -Slice '02-single' } 'use -Bootstrap' 'Normal mode still requires a saved reference'
    Assert-NoSavedReference -SliceBinding $single
    Write-Host 'PASS Allow a first capture when there is no reference folder'
    New-Item -ItemType Directory -Path $single.ReferenceDir | Out-Null
    Assert-Fails { Assert-NoSavedReference -SliceBinding $single } 'already exists' 'Refuse a first capture when a reference folder exists'
    Remove-Item -LiteralPath $single.ReferenceDir

    # Component selection.
    New-Slice '09-multi' @('spatial.first', 'spatial.second') | Out-Null
    Assert-Fails { Get-SliceBinding -Layout $layout -Slice '09-multi' } 'Select one with -Component' 'Refuse to guess among several components'
    Assert-Equal 'spatial.second' (Get-SliceBinding -Layout $layout -Slice '09-multi' -Component 'spatial.second').Component 'Select a named component'
    Assert-True ((Get-SliceBinding -Layout $layout -Slice '09-multi' -Component 'spatial.second').CSharpTestProject.EndsWith('spatial.second.1.csproj')) `
        'Use the selected component test project'
    Assert-Fails { Get-SliceBinding -Layout $layout -Slice '09-multi' -Component 'spatial.third' } 'is not in' 'Reject an unknown component'
    New-Slice '03-two-projects' @('spatial.two') -TestProjects 2 | Out-Null
    Assert-Fails { Get-SliceBinding -Layout $layout -Slice '03-two-projects' } 'supports one' 'Reject several csharp test projects'

    # The saved reference records the component for normal runs.
    $runDir = New-PassedRun '09-multi' 'spatial.second'
    Save-SliceReferenceFromRun -Layout $layout -RunDir $runDir | Out-Null
    Assert-Equal 'spatial.second' (Get-SliceInfo -Layout $layout -Slice '09-multi').Component 'Use the component recorded in the reference'
    Assert-Fails { Get-SliceInfo -Layout $layout -Slice '09-multi' -Component 'spatial.first' } 'records component spatial.second' `
        'Reject a component that differs from the reference'

    # Test gate.
    $configured = @('A', 'B')
    $passed = @([pscustomobject]@{ Test = 'A'; Outcome = 'Passed' }, [pscustomobject]@{ Test = 'B'; Outcome = 'Passed' })
    Assert-Equal '0' (Get-CaptureGateErrors -ConfiguredTests $configured -Results $passed).Count 'Gate passes with one Passed result per test'
    $errors = Get-CaptureGateErrors -ConfiguredTests $configured -Results @(
        [pscustomobject]@{ Test = 'A'; Outcome = 'NotExecuted' },
        [pscustomobject]@{ Test = 'C'; Outcome = 'Passed' })
    Assert-True ($errors -contains 'NotExecuted: A') 'Gate fails on a skipped test'
    Assert-True ($errors -contains 'No result: B') 'Gate fails on a missing result'
    Assert-True ($errors -contains 'Unexpected result (Passed): C') 'Gate fails on an unexpected result'
    $errors = Get-CaptureGateErrors -ConfiguredTests @('A') -Results @($passed[0], $passed[0])
    Assert-True ($errors -contains '2 results: A') 'Gate fails on a duplicate result'
    $errors = Get-CaptureGateErrors -ConfiguredTests @('A') -Results @([pscustomobject]@{ Test = 'A'; Outcome = 'Failed' })
    Assert-True ($errors -contains 'Failed: A') 'Gate fails on a failed test'
    Assert-True ((Get-CaptureGateErrors -ConfiguredTests @() -Results @()) -contains 'No configured tests.') 'Gate fails with no configured tests'
    $errors = Get-CaptureGateErrors -ConfiguredTests @('Parse("point")', 'Parse("POINT")') -Results @(
        [pscustomobject]@{ Test = 'Parse("point")'; Outcome = 'Passed' })
    Assert-True ($errors -ccontains 'No result: Parse("POINT")') 'Gate treats test names that differ only in case as different tests'

    # Saving a first reference.
    Assert-Fails { Save-SliceReferenceFromRun -Layout $layout -RunDir (Join-Path $testRoot 'no-run') } 'No capture-result.json' 'Refuse a folder without a run record'
    $failedRun = New-PassedRun '02-single' 'spatial.single'
    $record = Get-Content (Join-Path $failedRun 'capture-result.json') -Raw | ConvertFrom-Json
    $record.gatePassed = $false
    $record | ConvertTo-Json | Set-Content (Join-Path $failedRun 'capture-result.json') -Encoding utf8
    Assert-Fails { Save-SliceReferenceFromRun -Layout $layout -RunDir $failedRun } 'did not pass' 'Refuse a run that failed the gate'
    $changedRun = New-PassedRun '02-single' 'spatial.single'
    '{"spans":[1]}' | Set-Content (Join-Path $changedRun 'capture\reference.otlp.json') -Encoding utf8
    Assert-Fails { Save-SliceReferenceFromRun -Layout $layout -RunDir $changedRun } 'changed after the capture run' 'Refuse a bundle changed after the run'
    Assert-True (-not (Test-Path -LiteralPath $single.ReferenceDir)) 'A refused save leaves no reference folder'

    # Simulate a copy that does not match the gated bundle, for example because a
    # bundle file changed after the hash check. Replace the hash function inside the
    # module so that only the staging copy reports a different hash.
    $module = Get-Module SpecGateRepro
    & $module {
        $script:RealGetBundleFileHashes = ${function:Get-BundleFileHashes}
        ${function:script:Get-BundleFileHashes} = {
            param([string] $BundleDir)
            $hashes = & $script:RealGetBundleFileHashes -BundleDir $BundleDir
            if ($BundleDir -like '*.saving-*') { $hashes['reference.otlp.json'] = 'CHANGED' }
            $hashes
        }
    }
    try {
        Assert-Fails { Save-SliceReferenceFromRun -Layout $layout -RunDir (New-PassedRun '02-single' 'spatial.single') } 'does not match the gated bundle' `
            'Refuse a copy that does not match the gated bundle'
    }
    finally {
        & $module { ${function:script:Get-BundleFileHashes} = $script:RealGetBundleFileHashes }
    }
    Assert-True (-not (Test-Path -LiteralPath $single.ReferenceDir)) 'A mismatched copy leaves no reference folder'
    Assert-True (@(Get-ChildItem -LiteralPath (Split-Path $single.ReferenceDir) -Directory -Filter 'reference.saving-*').Count -eq 0) `
        'A mismatched copy leaves no staging folder'

    $goodRun = New-PassedRun '02-single' 'spatial.single'
    Save-SliceReferenceFromRun -Layout $layout -RunDir $goodRun | Out-Null
    $savedHashes = Get-BundleFileHashes -BundleDir $single.ReferenceDir
    $runHashes = Get-BundleFileHashes -BundleDir (Join-Path $goodRun 'capture')
    Assert-Equal ($runHashes.Values -join ',') ($savedHashes.Values -join ',') 'Save copies the bundle files unchanged'
    Assert-Equal 'spatial.single' (Get-SliceInfo -Layout $layout -Slice '02-single').Component 'A saved reference supports normal runs'
    Assert-Fails { Save-SliceReferenceFromRun -Layout $layout -RunDir (New-PassedRun '02-single' 'spatial.single') } 'already exists' `
        'Refuse to overwrite a saved reference'

    # Capture lock.
    $lockName = "Global\SpecGateSpatialCaptureTest-$([guid]::NewGuid().ToString('N'))"
    $ready = Join-Path $testRoot 'lock-ready'
    $stop = Join-Path $testRoot 'lock-stop'
    $holder = Start-LockHolder -Name $lockName -ReadyFile $ready -StopFile $stop
    try {
        Assert-Fails { Enter-CaptureLock -Name $lockName } 'Another SpecGate capture is running' 'Refuse a second capture while one runs'
    }
    finally {
        Set-Content -LiteralPath $stop -Value stop
        $holder.WaitForExit()
    }
    $lock = Enter-CaptureLock -Name $lockName
    Exit-CaptureLock -Lock $lock
    Write-Host 'PASS Allow a capture after the previous one releases the lock'

    Remove-Item -LiteralPath $ready, $stop
    $holder = Start-LockHolder -Name $lockName -ReadyFile $ready -StopFile $stop -Abandon
    Set-Content -LiteralPath $stop -Value stop
    $holder.WaitForExit()
    $lock = Enter-CaptureLock -Name $lockName
    Exit-CaptureLock -Lock $lock
    Write-Host 'PASS Allow a capture after a previous capture process ended without releasing the lock'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
