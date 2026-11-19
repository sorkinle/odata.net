#Requires -Version 7.2

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\scripts\SpecGateRepro.psm1') -Force

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "specgate-replay-safety-$([guid]::NewGuid().ToString('N'))"
$pinned = [pscustomobject]@{ GitUrl = 'https://github.com/schgoo/specgate' }

function Assert-Equal([object] $Expected, [object] $Actual, [string] $Case) {
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

function New-TestLayout([string] $Name) {
    $parent = Join-Path $testRoot $Name
    $odataRs = Join-Path $parent 'odata-rs'
    $output = Join-Path $parent 'output'
    New-Item -ItemType Directory -Path $odataRs, $output -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $odataRs 'Cargo.lock'), 'original lockfile')
    [pscustomobject]@{
        Parent       = $parent
        OdataRs      = $odataRs
        SpecGateRoot = Join-Path $parent 'specgate'
        Output       = $output
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $existingOutput = Join-Path $testRoot 'existing-output'
    New-Item -ItemType Directory -Path $existingOutput -Force | Out-Null
    $sentinel = Join-Path $existingOutput 'user-data.txt'
    [IO.File]::WriteAllText($sentinel, 'keep this file')

    Assert-Fails { New-OutputDirectory -Path $existingOutput } 'not empty' 'Reject nonempty output without Force'
    $firstRun = New-OutputDirectory -Path $existingOutput -Force
    Assert-Equal 'keep this file' ([IO.File]::ReadAllText($sentinel)) 'Force preserves existing output'
    Assert-Equal $existingOutput (Split-Path $firstRun -Parent) 'Force selects a new child directory'
    [IO.File]::WriteAllText((Join-Path $firstRun 'result.txt'), 'first run')
    $secondRun = New-OutputDirectory -Path $existingOutput -Force
    if ($firstRun -eq $secondRun -or -not (Test-Path -LiteralPath (Join-Path $firstRun 'result.txt'))) {
        throw 'A repeated run must preserve the previous run and use a different directory.'
    }
    Write-Host 'PASS Repeated Force runs preserve earlier results'

    foreach ($configName in @('config.toml', 'config')) {
        $layout = New-TestLayout "existing-$($configName.Replace('.', '-'))"
        $cargoDir = Join-Path $layout.Parent '.cargo'
        New-Item -ItemType Directory -Path $cargoDir | Out-Null
        $config = Join-Path $cargoDir $configName
        [IO.File]::WriteAllText($config, 'existing user configuration')
        Assert-Fails {
            Invoke-WithCargoPatch -Layout $layout -Pinned $pinned -OutDir $layout.Output -Action {
                throw 'The replay must not start.'
            }
        } 'already exists' "Reject existing $configName"
        Assert-Equal 'existing user configuration' ([IO.File]::ReadAllText($config)) "Preserve existing $configName"
    }

    $layout = New-TestLayout 'successful-replay'
    $lockFile = Join-Path $layout.OdataRs 'Cargo.lock'
    $cargoDir = Join-Path $layout.Parent '.cargo'
    $config = Join-Path $cargoDir 'config.toml'
    $unrelated = Join-Path $cargoDir 'user-data.txt'
    $layout.SpecGateRoot = Join-Path $layout.Parent "spec'gate"
    $code = Invoke-WithCargoPatch -Layout $layout -Pinned $pinned -OutDir $layout.Output -Action {
        if (-not (Test-Path -LiteralPath $config -PathType Leaf)) {
            throw 'The temporary Cargo patch was not created.'
        }
        if ([IO.File]::ReadAllText($config) -notmatch 'path = "[^"]*spec''gate') {
            throw 'The Cargo patch does not quote the SpecGate path correctly.'
        }
        [IO.File]::WriteAllText($lockFile, 'replay lockfile')
        [IO.File]::WriteAllText($unrelated, 'keep this file')
        return 7
    }
    Assert-Equal 7 $code 'Return the replay result'
    Assert-Equal 'original lockfile' ([IO.File]::ReadAllText($lockFile)) 'Restore the original lockfile'
    Assert-Equal 'replay lockfile' ([IO.File]::ReadAllText((Join-Path $layout.Output 'Cargo.lock.after-replay'))) `
        'Preserve the lockfile observed after replay'
    Assert-Equal 'keep this file' ([IO.File]::ReadAllText($unrelated)) 'Preserve files added to the Cargo directory'
    Assert-Equal $false (Test-Path -LiteralPath $config) 'Remove only the temporary Cargo patch'

    $layout = New-TestLayout 'failed-replay'
    $lockFile = Join-Path $layout.OdataRs 'Cargo.lock'
    $cargoDir = Join-Path $layout.Parent '.cargo'
    Assert-Fails {
        Invoke-WithCargoPatch -Layout $layout -Pinned $pinned -OutDir $layout.Output -Action {
            [IO.File]::WriteAllText($lockFile, 'failed replay lockfile')
            throw 'planned replay failure'
        }
    } 'planned replay failure' 'Propagate a replay failure'
    Assert-Equal 'original lockfile' ([IO.File]::ReadAllText($lockFile)) 'Restore the lockfile after failure'
    Assert-Equal $false (Test-Path -LiteralPath $cargoDir) 'Remove the empty temporary Cargo directory'

    $layout = New-TestLayout 'changed-config'
    $lockFile = Join-Path $layout.OdataRs 'Cargo.lock'
    $config = Join-Path $layout.Parent '.cargo\config.toml'
    Assert-Fails {
        Invoke-WithCargoPatch -Layout $layout -Pinned $pinned -OutDir $layout.Output -Action {
            [IO.File]::WriteAllText($config, 'changed by another process')
            [IO.File]::WriteAllText($lockFile, 'changed during replay')
            return 0
        }
    } 'changed during replay' 'Report an externally changed Cargo patch'
    Assert-Equal 'changed by another process' ([IO.File]::ReadAllText($config)) 'Preserve the changed Cargo configuration'
    Assert-Equal 'original lockfile' ([IO.File]::ReadAllText($lockFile)) 'Restore the original lockfile after a cleanup error'
    Assert-Equal 'changed during replay' ([IO.File]::ReadAllText((Join-Path $layout.Output 'Cargo.lock.after-replay'))) `
        'Preserve the lockfile when cleanup reports an error'

    $layout = New-TestLayout 'occupied-lock-snapshot'
    $lockFile = Join-Path $layout.OdataRs 'Cargo.lock'
    $lockAfter = Join-Path $layout.Output 'Cargo.lock.after-replay'
    Assert-Fails {
        Invoke-WithCargoPatch -Layout $layout -Pinned $pinned -OutDir $layout.Output -Action {
            [IO.File]::WriteAllText($lockFile, 'changed during replay')
            [IO.File]::WriteAllText($lockAfter, 'another process owns this file')
        }
    } 'Cargo.lock cleanup failed' 'Report a lockfile snapshot collision'
    Assert-Equal 'changed during replay' ([IO.File]::ReadAllText($lockFile)) 'Do not overwrite an unsaved lockfile'
    Assert-Equal 'original lockfile' ([IO.File]::ReadAllText((Join-Path $layout.Output 'Cargo.lock.before-replay'))) `
        'Retain the original lockfile after a cleanup failure'
    Assert-Equal 'another process owns this file' ([IO.File]::ReadAllText($lockAfter)) 'Preserve the existing snapshot'

    $layout = New-TestLayout 'locked-lockfile'
    $lockFile = Join-Path $layout.OdataRs 'Cargo.lock'
    $held = [pscustomobject]@{ Stream = $null }
    try {
        Assert-Fails {
            Invoke-WithCargoPatch -Layout $layout -Pinned $pinned -OutDir $layout.Output -Action {
                $held.Stream = [IO.File]::Open($lockFile, [IO.FileMode]::Open,
                    [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            }
        } 'Cargo.lock cleanup failed' 'Report an exclusively locked lockfile'
    }
    finally {
        if ($held.Stream) { $held.Stream.Dispose() }
    }
    Assert-Equal 'original lockfile' ([IO.File]::ReadAllText($lockFile)) 'Do not overwrite a locked lockfile'
    Assert-Equal 'original lockfile' ([IO.File]::ReadAllText((Join-Path $layout.Output 'Cargo.lock.before-replay'))) `
        'Retain the backup when the lockfile is locked'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
