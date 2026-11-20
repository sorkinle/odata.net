# Reproduce the Microsoft.Spatial parity evidence

This page explains how to replay and recapture the SpecGate evidence for the
odata-rs spatial parity PRs. It applies to every slice PR in the stack. Each
slice adds one folder under `specgate-capture/slices/` and one row to the
version table below.

A replay shows how the Rust candidate behaves on the recorded C# inputs. It
does not prove parity by itself. Each PR description states the observed
differences and the declarations that were not compared.

## Where the parts live

| Part | Location |
| --- | --- |
| Reproduction scripts and this guide | Fork [`sorkinle/odata.net`](https://github.com/sorkinle/odata.net), branch `spike/specgate-spatial-repro-infra`. Each slice's source branch is based on it. |
| C# annotations, capture setup, and capture tests | The slice's source branch on the same fork. The Rust PR description and its report issue name the branch. Slice 1 (PR #80) uses `spike/specgate-spatial-coordinated`. |
| Binding and saved C# reference for each slice | `specgate-capture/slices/<slice>/` on the slice's source branch |
| Pinned SpecGate commit and annotations version | `specgate-capture/specgate-pin.json` |
| Rust test adapters | [`OData/odata-rs`](https://github.com/OData/odata-rs), in the slice PR branch |
| SpecGate | [`schgoo/specgate`](https://github.com/schgoo/specgate), at the commit in the version table |

API ledgers, slice plans, and full reports are kept in a separate review
workspace. They are not published in either repository.

## Reproduce with GitHub Copilot

The skill `.github/skills/specgate-spatial-repro` in this repository runs the
steps on this page. It sets up the repositories, builds SpecGate from source,
and runs replay. On request, it also runs discovery and a fresh capture with
the test gate. It then compares the results with the expected results below.

1. Clone the slice's source branch into an empty parent folder, as in step 2.
2. Start GitHub Copilot CLI in the `odata.net` clone. Copilot loads the skill
   from `.github/skills/`.
3. Ask, for example: "Use the specgate-spatial-repro skill to replay
   https://github.com/OData/odata-rs/pull/80." Add "with discovery and
   capture" for a full run.

The scripts need PowerShell 7 and the prerequisites below. You can also run
them without Copilot. See the skill's `SKILL.md` for the commands. They write
all results to `<parent>\specgate-out\`. They restore the original lockfile
after replay and preserve any post-replay changes in the output folder.
The skill selects the published binding and reference for the PR from the
source branch. Reviewers do not need a separate workspace or a slice folder.

## Versions by slice

Later slices build on earlier code. Use the commits from the slice row, not
the current branch heads, to reproduce an earlier slice.

| Slice | Folder | odata-rs commit | odata.net fork commit | SpecGate commit |
| --- | --- | --- | --- | --- |
| 5: factories | `slices/05-factories` | `58876f54b452ae0fb0a8a26e3476e3e481b1c23e` ([PR #88](https://github.com/OData/odata-rs/pull/88)) | `dd209fde47012aa6fc34b49655a4740e9213f5dc` | `4a904c276e24177c84cf4fb7182c93e811137f9d` |

Each slice's source branch adds its own row. For a new slice, write
"Pending" until its changes are committed, then
replace it with the commit hash. The odata.net fork commit
contains the annotations, capture setup, binding, and saved reference for the
slice. Later commits that change only this page do not change the evidence.

SpecGate has no `v0.6.0` tag. The pinned commit reports version `0.6.0` in
capture manifests. `specgate-pin.json` records the commit and the
`SpecGate.Annotations` version. The odata-rs test dependency must use the
same commit; the install script checks it.

## Prerequisites

- Git.
- Read access to [`OData/odata-rs`](https://github.com/OData/odata-rs). The
  repository is private. The fork `sorkinle/odata.net` and SpecGate are public.
- PowerShell 7.2 or later, for the reproduction scripts and manual replay.
- A Rust toolchain with `cargo`. odata-rs selects its own toolchain through
  `rust-toolchain.toml`. SpecGate needs Rust 1.93 or later.
- [`just`](https://github.com/casey/just), to build the SpecGate C# tools.
- The .NET 10 SDK, to build and run the SpecGate C# tools.
- The .NET 8 runtime and an SDK that builds `net8.0`, to run the C# tests.
  The Slice 1 evidence used SDK `10.0.404` and runtime `8.0.34`, because a
  .NET 8 SDK was not available.

Replay alone runs only Rust code. It needs SpecGate and the Rust toolchain,
but not the .NET 8 runtime.

## 1. Install SpecGate from source

1. Clone SpecGate and check out the commit in `specgate-pin.json`:

   ```powershell
   git clone https://github.com/schgoo/specgate
   cd specgate
   git checkout 4a904c276e24177c84cf4fb7182c93e811137f9d
   ```

2. Build the CLI and the C# tools:

   ```powershell
   cd rust
   cargo build --release -p specgate-cli
   cd ..
   just discovery-tool
   just weaver-tool
   ```

3. Put the C# tools beside the CLI. Then add that folder to `PATH`:

   ```powershell
   Copy-Item artifacts\discovery\*, artifacts\weaver\* rust\target\release\ -Recurse -Force
   $env:PATH = "$PWD\rust\target\release;$env:PATH"
   ```

   As an alternative, set `SPECGATE_DISCOVERY_PATH` to `artifacts\discovery`
   and `SPECGATE_WEAVER_PATH` to `artifacts\weaver`.

4. Check the install. The command prints the usage text:

   ```powershell
   specgate --help
   ```

5. To record a fresh C# reference, also pack `SpecGate.Annotations` from the
   same commit into a local folder. Use the `annotationsVersion` in
   `specgate-pin.json`. That version is not on nuget.org. Replay does not
   need it.

   ```powershell
   dotnet pack csharp\SpecGate.Annotations\SpecGate.Annotations.csproj -c Release -o <feed> -p:Version=0.6.0-g4a904c27 -p:PackageVersion=0.6.0-g4a904c27
   $env:RestoreAdditionalProjectSources = "<feed>"
   ```

   Use a full path for `<feed>`. Set the variable in each shell that runs
   `dotnet test` or `specgate capture`.

## 2. Clone both repositories side by side

The slice bindings use relative paths. Put both clones in one parent folder:

```text
<parent>\
  odata.net\   fork, at the slice's source branch
  odata-rs\    slice PR branch
```

```powershell
git clone --branch <source-branch> https://github.com/sorkinle/odata.net odata.net
git clone https://github.com/OData/odata-rs odata-rs
```

In `odata-rs`, check out the slice PR branch or its commit from the version
table. Keep `odata.net` on the slice's source branch so this guide and the helper
remain available. The replay steps check its binding and reference against
the evidence commit in the version table.

## 3. Replay the saved C# reference

The pinned SpecGate CLI requires a temporary Cargo path patch for the
Git-sourced dependency. The helper below creates the patch, runs the SpecGate
commands, and cleans up even if a command fails. It refuses to replace an
existing `<parent>\.cargo\config.toml` or `<parent>\.cargo\config`.
Do not edit `odata-rs\Cargo.lock` or run other Cargo commands in that checkout
until replay finishes.

Set the four values below for your checkout and PR. Use the fork evidence
commit from the version table. Run the block in
PowerShell 7. The helper selects the PR's published reference and runs its
SpecGate commands from the reference folder.

```powershell
$parent = 'C:\path\to\parent'
$specgateRoot = 'C:\path\to\specgate'
$pullRequest = 'https://github.com/OData/odata-rs/pull/<number>'
$forkEvidence = '<odata.net fork commit from the version table>'

$module = Join-Path $parent 'odata.net\.github\skills\specgate-spatial-repro\scripts\SpecGateRepro.psm1'
Import-Module $module -Force
$layout = Get-ReproLayout -SpecGateRoot $specgateRoot
$pinned = Get-PinnedSpecGateDependency -Layout $layout
$head = git -C $specgateRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -ne $pinned.Rev) {
    throw "SpecGate must be checked out at $($pinned.Rev)."
}
$slice = Get-SliceInfo -Layout $layout -PullRequest $pullRequest
git -C $layout.ForkRoot diff --quiet $forkEvidence -- "specgate-capture\slices\$($slice.Name)"
if ($LASTEXITCODE -ne 0) {
    throw 'The binding or reference changed, or the fork evidence commit is unavailable.'
}
$out = New-OutputDirectory -Path (Join-Path $parent "specgate-out\$($slice.Name)\manual") -Force
$candidate = Join-Path $out 'candidate.otlp.json'

Push-Location $slice.Dir
try {
    Invoke-WithCargoPatch -Layout $layout -Pinned $pinned -OutDir $out -Action {
        specgate validate bundle reference
        if ($LASTEXITCODE -ne 0) { throw 'Bundle validation failed.' }
        specgate replay reference --binding specgate.yaml --impl rust --out $candidate --force
        if ($LASTEXITCODE -ne 0) { throw 'Replay failed.' }
        specgate validate linked $candidate reference\registry.ctsc.json
        if ($LASTEXITCODE -ne 0) { throw 'Linked validation failed.' }
        specgate compare reference\reference.otlp.json $candidate --registry reference\registry.ctsc.json
        if ($LASTEXITCODE -gt 1) { throw 'Compare failed.' }
    }
}
finally {
    Pop-Location
}
```

The helper preserves existing output by creating a new run folder when needed.
It prints the folder path. It removes only its own unchanged Cargo patch and
restores the original `odata-rs\Cargo.lock`. If the lockfile changed during
replay, it saves those contents as `Cargo.lock.after-replay` in the run folder.
The saved file can contain concurrent changes. Inspect it before applying any
changes. If cleanup fails, keep the files named in the error and inspect them
before making changes.

`compare` returns exit code `1` when it finds a difference. Compare its output
with the expected result for the slice:

| Slice | Expected `compare` result |
| --- | --- |
| 5: factories | `equivalent: false`; 25 mismatch lines: 22 `CoordinateSystem.Id`/`Name` placeholders in 11 successful completed-polygon cases and three native error messages. Replay has 14 scenarios and 14 operations; linked validation passes. Default-SRID selection is adapter behavior. No fluent factory API parity is claimed. |

A new validation error or a different set of mismatches is a new finding.

## 4. Optional: record a fresh C# reference

A fresh reference is valid only when all configured .NET 8 tests pass. A
failed, skipped, or missing test result blocks the reference, even when
SpecGate writes a valid bundle.

The slice `specgate.yaml` names the component and the C# test project.

Run only one capture at a time on a machine. Step 3 identifies results by
temporary folder name, so two captures would mix their results. The capture
script enforces this with a machine-wide lock.

1. Set `RestoreAdditionalProjectSources` as described in section 1, step 5. Then run
   the tests in the C# test project. All tests must pass:

   ```powershell
   dotnet test <test-project> --framework net8.0
   ```

2. Run the capture into a new folder. Do not overwrite `reference`:

   ```powershell
   specgate capture --binding specgate.yaml --component <component> --impl csharp --out <fresh-dir> --force
   ```

3. Keep the per-test results while the capture runs. SpecGate writes one
   `.trx` file per test under a new temporary folder named
   `specgate-capture-*` in the system temporary folder.
   SpecGate deletes that folder when capture ends. Copy each new `.trx` file
   to a separate folder before that happens.

4. Check the copied results. Each configured test must have exactly one
   result with outcome `Passed`. To get the configured test names, run
   `dotnet test` with `--list-tests` on the capture project.

5. Validate the fresh bundle. Then compare it with the saved reference:

   ```powershell
   specgate validate bundle <fresh-dir>
   specgate compare reference\reference.otlp.json <fresh-dir>\reference.otlp.json --registry reference\registry.ctsc.json
   ```

   Expected output: `equivalent: true`. Any difference means that the C#
   behavior or the capture changed. Review it. Do not replace a saved
   reference with a fresh capture.

## Add a slice

1. Create the slice's C# source branch from `spike/specgate-spatial-repro-infra`,
   not from another slice's branch. Base the odata-rs branch on the previous
   slice branch.
2. In `src/Microsoft.Spatial/Microsoft.Spatial.csproj`, add
   `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` and a
   `SpecGate.Annotations` package reference with the version in
   `specgate-pin.json`. Add the annotations for the slice.
3. Add a capture project and test file with names unique to the slice. Give
   the project its own `BaseOutputPath`. Reference `SpecGate.Annotations` with
   the pinned version.
4. Add `specgate-capture/slices/<slice>/specgate.yaml` with paths relative to
   that file.
5. Record the first reference with the test gate. Follow "Record the first
   reference of a new slice" in the skill's `SKILL.md`: capture with
   `-Bootstrap`, review the bundle, then save it with `Save-SliceReference.ps1`.
   Do not create or copy reference files by hand.
6. Add a row to each table on this page. Put the canonical Rust PR URL as a
   Markdown link in the `odata-rs commit` column. Record the commits after
   the slice is committed.