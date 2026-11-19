#Requires -Version 7.2
<#
.SYNOPSIS
Records a fresh C# reference for a slice and enforces the test gate.

.DESCRIPTION
1. Checks that the slice projects use the SpecGate.Annotations version in
   specgate-capture\specgate-pin.json.
2. Runs the slice's C# capture tests. All tests must pass.
3. Lists the configured tests.
4. Runs specgate capture into <OutDir>\capture. SpecGate writes one .trx file
   per test in a temporary folder and deletes that folder when capture ends.
   This script copies the .trx files to <OutDir>\test-results while capture runs.
5. Checks that each configured test has exactly one result, with outcome Passed.
6. Validates the bundle and writes <OutDir>\capture-result.json.
7. Compares the bundle with the slice's saved reference. -Bootstrap skips
   this step, because a first capture has no saved reference.

A fresh reference is valid only when step 5 passes. A valid bundle alone
does not show that the tests passed.

Only one capture runs on a machine at a time. Step 4 collects results from
every new specgate-capture-* temporary folder, so a second capture would mix
its results into the gate. A capture fails at once if another one is running.

Use -PullRequest to select the published reference for an odata-rs PR. If
there is one complete reference, it is selected when both -PullRequest and
-Slice are omitted.

Use -Bootstrap with -Slice to record the first reference of a slice. The
slice must not have a reference folder. After the run passes, save its bundle
with Save-SliceReference.ps1.

Use -Component when the slice binding has several components.

Default -OutDir is <parent>\specgate-out\<slice>\capture-run. Exit code 0
means that the gate passed and the reference is equivalent (or, with
-Bootstrap, that the gate and validation passed); 1 means that the gate
passed but the reference differs; 2 means that a step or the gate failed.
#>
[CmdletBinding()]
param(
    [string] $Slice,
    [string] $PullRequest,
    [string] $Component,
    [switch] $Bootstrap,
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
    $sliceInfo = Get-SliceBinding -Layout $layout -Slice $Slice -Component $Component
    Assert-NoSavedReference -SliceBinding $sliceInfo
}
else {
    $sliceInfo = Get-SliceInfo -Layout $layout -Slice $Slice -PullRequest $PullRequest -Component $Component
}
$Slice = $sliceInfo.Name
$project = $sliceInfo.CSharpTestProject
$framework = $sliceInfo.CSharpFramework

# 1. Check the annotations version in the test project and the annotated source project.
$annotatedProjects = @($project)
if ($sliceInfo.CSharpSources -and (Test-Path -LiteralPath $sliceInfo.CSharpSources -PathType Container)) {
    $annotatedProjects += @(Get-ChildItem -LiteralPath $sliceInfo.CSharpSources -Filter '*.csproj' -File | ForEach-Object FullName)
}
Assert-AnnotationsVersion -Layout $layout -Projects $annotatedProjects
Set-AnnotationsFeed -Layout $layout

if (-not $OutDir) { $OutDir = Join-Path $layout.Parent "specgate-out\$Slice\capture-run" }
$lock = Enter-CaptureLock
try {
    $OutDir = New-OutputDirectory -Path $OutDir -Force:$Force
    $bundleDir = Join-Path $OutDir 'capture'
    $resultsDir = (New-Item -ItemType Directory -Path (Join-Path $OutDir 'test-results') -Force).FullName
    $exe = $layout.SpecGateExe

    # 2. Run the tests.
    $code = Invoke-Logged 'dotnet' @('test', $project, '--framework', $framework, '--nologo') $sliceInfo.Dir (Join-Path $OutDir 'dotnet-test.log')
    if ($code -ne 0) { Write-Host 'The C# tests did not all pass. The capture is blocked.'; exit 2 }

    # 3. List the configured tests. dotnet prints them indented after a header line.
    $code = Invoke-Logged 'dotnet' @('test', $project, '--framework', $framework, '--no-build', '--list-tests', '--nologo') $sliceInfo.Dir (Join-Path $OutDir 'list-tests.log')
    if ($code -ne 0) { Write-Host 'Could not list the configured tests.'; exit 2 }
    $listing = Get-Content (Join-Path $OutDir 'list-tests.log')
    $headerIndex = [Array]::FindIndex([string[]]$listing, [Predicate[string]] { param($l) $l -match 'The following Tests are available' })
    if ($headerIndex -lt 0) { Write-Host 'No test list found in the dotnet output.'; exit 2 }
    $configuredTests = @($listing[($headerIndex + 1)..($listing.Count - 1)] | Where-Object { $_ -match '^\s{4}\S' } | ForEach-Object { $_.Trim() })
    if ($configuredTests.Count -eq 0) { Write-Host 'The test list is empty.'; exit 2 }

    # 4. Capture, and copy each .trx file while SpecGate still has it.
    $tempRoot = [IO.Path]::GetTempPath()
    $tempPattern = 'specgate-capture-*'
    $existingTempDirs = @(Get-ChildItem $tempRoot -Directory -Filter $tempPattern -ErrorAction SilentlyContinue | ForEach-Object FullName)
    $copied = @{}

    function Copy-NewTestResults {
        $captureDirs = Get-ChildItem $tempRoot -Directory -Filter $tempPattern -ErrorAction SilentlyContinue |
            Where-Object { $existingTempDirs -notcontains $_.FullName }
        foreach ($dir in $captureDirs) {
            foreach ($trx in Get-ChildItem $dir.FullName -Recurse -Filter '*.trx' -ErrorAction SilentlyContinue) {
                $stamp = "$($trx.Length)|$($trx.LastWriteTimeUtc.Ticks)"
                if ($copied[$trx.FullName] -eq $stamp) { continue }
                $relative = [IO.Path]::GetRelativePath($tempRoot, $trx.FullName) -replace '[\\/]', '_'
                try {
                    Copy-Item $trx.FullName (Join-Path $resultsDir $relative) -Force
                    $copied[$trx.FullName] = $stamp
                }
                catch {
                    # SpecGate may still be writing the file or may have deleted it. Retry on the next poll.
                }
            }
        }
    }

    $captureLog = Join-Path $OutDir 'capture.log'
    $captureErr = Join-Path $OutDir 'capture.stderr.log'
    $captureArgs = @('capture', '--binding', 'specgate.yaml', '--component', $sliceInfo.Component, '--impl', 'csharp', '--out', $bundleDir, '--force')
    Write-Host "> specgate.exe $($captureArgs -join ' ')"
    $process = Start-Process -FilePath $exe -ArgumentList $captureArgs -WorkingDirectory $sliceInfo.Dir -NoNewWindow -PassThru `
        -RedirectStandardOutput $captureLog -RedirectStandardError $captureErr
    while (-not $process.HasExited) {
        Copy-NewTestResults
        Start-Sleep -Milliseconds 100
    }
    Copy-NewTestResults
    $process.WaitForExit()
    Get-Content $captureLog, $captureErr | Write-Host
    if ($process.ExitCode -ne 0) { Write-Host "Capture failed with exit code $($process.ExitCode)."; exit 2 }

    # 5. Enforce the test gate.
    $results = foreach ($file in Get-ChildItem $resultsDir -Filter '*.trx') {
        [xml] $trx = Get-Content $file.FullName -Raw
        foreach ($result in $trx.TestRun.Results.UnitTestResult) {
            [pscustomobject]@{ Test = $result.testName; Outcome = $result.outcome; File = $file.Name }
        }
    }
    $results = @($results)
    $gateErrors = Get-CaptureGateErrors -ConfiguredTests $configuredTests -Results $results
    $gateLines = @("Configured tests: $($configuredTests.Count)", "Results copied: $($results.Count)") + $gateErrors
    $gateLines | Set-Content (Join-Path $OutDir 'test-gate.log') -Encoding utf8
    $gateLines | Write-Host
    if ($gateErrors.Count -gt 0) { Write-Host 'Test gate failed. Do not use this capture as a reference.'; exit 2 }
    Write-Host 'Test gate passed: each configured test has exactly one Passed result.'

    # 6. Validate the bundle and record the run. Save-SliceReference.ps1 accepts only
    # a run with this record, and only while the bundle files still match it.
    $code = Invoke-Logged $exe @('validate', 'bundle', $bundleDir) $sliceInfo.Dir (Join-Path $OutDir 'validate-bundle.log')
    if ($code -ne 0) { Write-Host 'Bundle validation failed.'; exit 2 }

    $forkCommit = (& git -C $layout.ForkRoot rev-parse HEAD).Trim()
    $forkDirty = [bool] (& git -C $layout.ForkRoot status --porcelain)
    [ordered]@{
        format          = 'specgate-spatial-capture-result'
        slice           = $Slice
        component       = $sliceInfo.Component
        bootstrap       = [bool] $Bootstrap
        gatePassed      = $true
        bundleValid     = $true
        configuredTests = $configuredTests.Count
        forkCommit      = $forkCommit
        forkDirty       = $forkDirty
        annotations     = Get-AnnotationsVersion -Layout $layout
        bundleFiles     = Get-BundleFileHashes -BundleDir $bundleDir
    } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutDir 'capture-result.json') -Encoding utf8
    if ($forkDirty) { Write-Warning "The fork has uncommitted changes. The capture does not match commit $forkCommit." }

    # 7. Compare the bundle with the saved reference.
    if ($Bootstrap) {
        Write-Host "First capture passed the test gate and bundle validation. Results: $OutDir"
        Write-Host "Review the bundle, then save it: Save-SliceReference.ps1 -RunDir $OutDir"
        exit 0
    }
    $compareLog = Join-Path $OutDir 'compare-with-saved-reference.log'
    $code = Invoke-Logged $exe @('compare', 'reference\reference.otlp.json', (Join-Path $bundleDir 'reference.otlp.json'),
        '--registry', 'reference\registry.ctsc.json') $sliceInfo.Dir $compareLog
    if ($code -gt 1) { Write-Host 'Compare did not finish.'; exit 2 }
    if ($code -eq 1) { Write-Host 'The fresh C# reference differs from the saved reference. Review the differences.'; exit 1 }
    Write-Host "The fresh C# reference is equivalent to the saved reference. Results: $OutDir"
}
finally {
    Exit-CaptureLock -Lock $lock
}
