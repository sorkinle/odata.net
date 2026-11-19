#Requires -Version 7.2

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\scripts\SpecGateRepro.psm1') -Force

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "specgate-reference-selection-$([guid]::NewGuid().ToString('N'))"
$captureRoot = Join-Path $testRoot 'specgate-capture'
$layout = [pscustomobject]@{ CaptureRoot = $captureRoot }

function New-TestReference([string] $Name) {
    $sliceDir = Join-Path $captureRoot "slices\$Name"
    $referenceDir = Join-Path $sliceDir 'reference'
    New-Item -ItemType Directory -Path $referenceDir -Force | Out-Null
    @'
components:
  spatial.fixture:
    implementations:
      csharp:
        tests:
          projects:
            - ../../capture.csproj
        csharp:
          framework: net8.0
'@ | Set-Content -LiteralPath (Join-Path $sliceDir 'specgate.yaml') -Encoding utf8
    '{"registry":{"id":"urn:fixture","version":"0.1.0"}}' |
        Set-Content -LiteralPath (Join-Path $referenceDir 'manifest.json') -Encoding utf8
    '{}' | Set-Content -LiteralPath (Join-Path $referenceDir 'reference.otlp.json') -Encoding utf8
    '{}' | Set-Content -LiteralPath (Join-Path $referenceDir 'registry.ctsc.json') -Encoding utf8
}

function Assert-Equal([string] $Expected, [string] $Actual, [string] $Case) {
    if ($Actual -cne $Expected) {
        throw "${Case}: expected '$Expected', got '$Actual'."
    }
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

try {
    New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null
    Assert-Fails { Get-SliceInfo -Layout $layout } 'No published references' 'Missing references'

    New-TestReference '01-first'
    Assert-Equal '01-first' (Get-SliceInfo -Layout $layout).Name 'Select the only complete reference'
    Assert-Equal '01-first' (Get-SliceInfo -Layout $layout -Slice '01-first').Name 'Explicit slice remains supported'

    $guide = Join-Path $captureRoot 'REPRODUCE.md'
    @'
| Slice | Folder | odata-rs commit | odata.net fork commit | SpecGate commit |
| --- | --- | --- | --- | --- |
| First | `slices/01-first` | PR [#80](https://github.com/OData/odata-rs/pull/80) | x | y |
| Second | `slices/02-second` | PR [#801](https://github.com/OData/odata-rs/pull/801) | x | y |
'@ | Set-Content -LiteralPath $guide -Encoding utf8
    Assert-Fails { Get-SliceInfo -Layout $layout -PullRequest 'https://github.com/OData/odata-rs/pull/99' } `
        'found 0' 'Unknown PR does not select the only reference'

    New-TestReference '02-second'
    Assert-Equal '01-first' (Get-SliceInfo -Layout $layout -PullRequest 'https://github.com/OData/odata-rs/pull/80#discussion').Name `
        'Select PR 80 without matching PR 801'
    Assert-Equal '01-first' (Get-SliceInfo -Layout $layout -PullRequest 'https://github.com/OData/odata-rs/pull/80/files').Name `
        'Select a PR from its files page'
    Assert-Equal '01-first' (Get-SliceInfo -Layout $layout -PullRequest '#80').Name 'Select a PR by number'
    Assert-Equal '02-second' (Get-SliceInfo -Layout $layout -PullRequest 'https://github.com/OData/odata-rs/pull/801/').Name `
        'Select PR 801'
    Assert-Fails { Get-SliceInfo -Layout $layout } 'Provide the target Rust PR URL' 'Ambiguous references'
    Assert-Fails { Get-SliceInfo -Layout $layout -PullRequest 'https://github.com/other/odata-rs/pull/80' } `
        'Expected an OData/odata-rs pull request URL' 'Reject a different repository'
    Assert-Fails { Get-SliceInfo -Layout $layout -Slice '..\other' } 'Invalid slice name' 'Reject a slice path'
    Assert-Fails { Get-SliceInfo -Layout $layout -Slice '01-first' -PullRequest 'https://github.com/OData/odata-rs/pull/80' } `
        'either -PullRequest or -Slice' 'Reject conflicting selectors'

    Remove-Item -LiteralPath (Join-Path $captureRoot 'slices\02-second\reference\reference.otlp.json')
    Assert-Fails { Get-SliceInfo -Layout $layout -PullRequest 'https://github.com/OData/odata-rs/pull/801' } `
        'published reference is incomplete' 'Reject an incomplete selected reference'
    Assert-Equal '01-first' (Get-SliceInfo -Layout $layout).Name 'Ignore incomplete references for automatic selection'

    Add-Content -LiteralPath $guide -Value '| Duplicate | `slices/02-second` | PR [#80](https://github.com/OData/odata-rs/pull/80) | x | y |'
    Assert-Fails { Get-SliceInfo -Layout $layout -PullRequest 'https://github.com/OData/odata-rs/pull/80' } `
        'found 2' 'Reject duplicate PR mappings'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
