#Requires -Version 7.2
<#
.SYNOPSIS
Records a fresh C# reference for a slice and enforces the test gate.

.DESCRIPTION
1. Runs the slice's C# capture tests. All tests must pass.
2. Lists the configured tests.
3. Runs specgate capture into <OutDir>\capture. SpecGate writes one .trx file
   per test in a temporary folder and deletes that folder when capture ends.
   This script copies the .trx files to <OutDir>\test-results while capture runs.
4. Checks that each configured test has exactly one result, with outcome Passed.
5. Validates the bundle and compares it with the slice's saved reference.

A fresh reference is valid only when step 4 passes. A valid bundle alone
does not show that the tests passed.

Default -OutDir is <parent>\specgate-out\<slice>\capture-run. Exit code 0
means that the gate passed and the reference is equivalent; 1 means that the
gate passed but the reference differs; 2 means that a step or the gate failed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Slice,
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
$sliceInfo = Get-SliceInfo -Layout $layout -Slice $Slice
Set-AnnotationsFeed -Layout $layout

if (-not $OutDir) { $OutDir = Join-Path $layout.Parent "specgate-out\$Slice\capture-run" }
$OutDir = New-OutputDirectory -Path $OutDir -Force:$Force
$bundleDir = Join-Path $OutDir 'capture'
$resultsDir = (New-Item -ItemType Directory -Path (Join-Path $OutDir 'test-results') -Force).FullName
$exe = $layout.SpecGateExe
$project = $sliceInfo.CSharpTestProject
$framework = $sliceInfo.CSharpFramework

# 1. Run the tests.
$code = Invoke-Logged 'dotnet' @('test', $project, '--framework', $framework, '--nologo') $sliceInfo.Dir (Join-Path $OutDir 'dotnet-test.log')
if ($code -ne 0) { Write-Host 'The C# tests did not all pass. The capture is blocked.'; exit 2 }

# 2. List the configured tests. dotnet prints them indented after a header line.
$code = Invoke-Logged 'dotnet' @('test', $project, '--framework', $framework, '--no-build', '--list-tests', '--nologo') $sliceInfo.Dir (Join-Path $OutDir 'list-tests.log')
if ($code -ne 0) { Write-Host 'Could not list the configured tests.'; exit 2 }
$listing = Get-Content (Join-Path $OutDir 'list-tests.log')
$headerIndex = [Array]::FindIndex([string[]]$listing, [Predicate[string]] { param($l) $l -match 'The following Tests are available' })
if ($headerIndex -lt 0) { Write-Host 'No test list found in the dotnet output.'; exit 2 }
$configuredTests = @($listing[($headerIndex + 1)..($listing.Count - 1)] | Where-Object { $_ -match '^\s{4}\S' } | ForEach-Object { $_.Trim() })
if ($configuredTests.Count -eq 0) { Write-Host 'The test list is empty.'; exit 2 }

# 3. Capture, and copy each .trx file while SpecGate still has it.
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

# 4. Enforce the test gate.
$results = foreach ($file in Get-ChildItem $resultsDir -Filter '*.trx') {
    [xml] $trx = Get-Content $file.FullName -Raw
    foreach ($result in $trx.TestRun.Results.UnitTestResult) {
        [pscustomobject]@{ Test = $result.testName; Outcome = $result.outcome; File = $file.Name }
    }
}
$results = @($results)
$gateErrors = New-Object System.Collections.Generic.List[string]
foreach ($test in $configuredTests) {
    $matching = @($results | Where-Object Test -eq $test)
    if ($matching.Count -eq 0) { $gateErrors.Add("No result: $test") }
    elseif ($matching.Count -gt 1) { $gateErrors.Add("$($matching.Count) results: $test") }
    elseif ($matching[0].Outcome -ne 'Passed') { $gateErrors.Add("$($matching[0].Outcome): $test") }
}
foreach ($result in $results | Where-Object { $configuredTests -notcontains $_.Test }) {
    $gateErrors.Add("Unexpected result ($($result.Outcome)): $($result.Test)")
}
$gateLines = @("Configured tests: $($configuredTests.Count)", "Results copied: $($results.Count)") + $gateErrors
$gateLines | Set-Content (Join-Path $OutDir 'test-gate.log') -Encoding utf8
$gateLines | Write-Host
if ($gateErrors.Count -gt 0) { Write-Host 'Test gate failed. Do not use this capture as a reference.'; exit 2 }
Write-Host 'Test gate passed: each configured test has exactly one Passed result.'

# 5. Validate the bundle and compare it with the saved reference.
$code = Invoke-Logged $exe @('validate', 'bundle', $bundleDir) $sliceInfo.Dir (Join-Path $OutDir 'validate-bundle.log')
if ($code -ne 0) { Write-Host 'Bundle validation failed.'; exit 2 }

$compareLog = Join-Path $OutDir 'compare-with-saved-reference.log'
$code = Invoke-Logged $exe @('compare', 'reference\reference.otlp.json', (Join-Path $bundleDir 'reference.otlp.json'),
    '--registry', 'reference\registry.ctsc.json') $sliceInfo.Dir $compareLog
if ($code -gt 1) { Write-Host 'Compare did not finish.'; exit 2 }
if ($code -eq 1) { Write-Host 'The fresh C# reference differs from the saved reference. Review the differences.'; exit 1 }
Write-Host "The fresh C# reference is equivalent to the saved reference. Results: $OutDir"
