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
| GeoJSON | `slices\07-geojson` | [#92](https://github.com/OData/odata-rs/pull/92), `28e1b36b754ce26b21fc0d8850b4787ab7711d0a` | `a327becd3d38f1c581a26ad44782b722bc90c2af` | `4a904c276e24177c84cf4fb7182c93e811137f9d` |

Each slice's source branch adds its own row. For a new slice, write
"Pending" until its changes are committed, then
replace it with the commit hash. The odata.net fork commit
contains the annotations, capture setup, binding, and saved reference for the
slice. Later commits that change only this page do not change the evidence.

SpecGate has no `v0.6.0` tag. The pinned commit reports version `0.6.0` in
capture manifests. `specgate-pin.json` records the commit and the
`SpecGate.Annotations` version. The odata-rs test dependency must use the
same commit; the install script checks it.

## Slice 7 GeoJSON boundary

The `07-geojson` reference records component `spatial.geojson`.
Rust replay and linked validation pass. Strict comparison returns
`equivalent: false` with 49 mismatch entries.
Select the published reference with
`-PullRequest https://github.com/OData/odata-rs/pull/92`.
Explicit `-Slice 07-geojson` selection also works.
The Rust evidence commit is
`28e1b36b754ce26b21fc0d8850b4787ab7711d0a`.
The original clean C# capture ran at
`5a0513e01280f8acdc368c8263a3f924b86c0b1c`; the later binding and guide
changes do not change its annotations, tests, or saved reference.

Direct discovery of the formatter methods failed because `Geography`,
`ISpatial`, and `IGeoJsonWriter` had no registered component owner.
The interface types cannot carry `SpecEvent`. The user approved the typed
capture adapters in `src\Microsoft.Spatial\GeoJsonCaptureAdapter.cs`.
The original formatter methods and behavior are unchanged.

The four adapter operations call the real public APIs:

- `read_geography` and `read_geometry` rebuild a materialized dictionary,
  call `Read<T>`, and observe the returned value through its native `SendTo`.
  The `concreteLineString` input selects a base or concrete LineString result
  type. A null result has `HasValue=false` and no invented shape events.
- `write_materialized` reconstructs a completed value from recorded spatial
  events, calls `Write`, and records the actual materialized output tree.
  The reconstruction is setup, not evidence that Rust can construct every
  reference value.
- `write_events` sends recorded spatial calls to the pipeline returned by
  `CreateWriter`. It records the attempted callback sequence, including a
  callback that throws. A nonzero `failAtCallback` injects one
  `InvalidOperationException`. The adapter catches only that exception
  instance and returns explicit failure fields with the observed prefix.
  It does not roll back output or claim that the callback completed.

Materialized inputs use parallel primitive arrays: `Parents`, `Names`,
`Kinds`, `Numbers`, `Integers`, and `Texts`. A root has parent `-1`.
Children follow their parent. Objects retain member names; arrays retain
element order. Kinds distinguish objects, arrays, null, strings, booleans,
`int`, `long`, `single`, `double`, and `decimal`. Decimal text is an invariant
representation of the actual decimal value, not JSON text. The adapter does
not parse JSON or select fixtures by name.

Spatial inputs retain topology and ordered calls, CRS, shape kinds, primary
coordinates, Z, and M. Output records are adapter observations of native
values and events. They are not native formatter, writer-interface, or
pipeline identity APIs. Materialized node traversal preserves the observed
dictionary enumeration order; JSON callback order is a separate observation.
Do not treat an object-member ordering difference alone as changed geometry.

The first gated reference contains 17 scenarios and 169 operations:
60 geography reads, 57 geometry reads, 30 materialized writes, and 22 event
writes. There are 133 result completions and 36 error completions.
`SpecException` retains the unmodified error message, not exception type
or inner metadata. Native tests check those types separately.

The configured .NET 8 suite contains 33 tests. Fourteen unchanged formatter
round-trip tests remain native-only coverage. The non-finite test and the
adapter-metadata test also have no captured operations. Thus 33 passing tests
do not mean 33 recorded scenarios.

Rust adapters are test-only code in
`crates\odata-spatial\tests\specgate_geojson.rs`. The facade package already
depends on the native GeoJSON codec, spatial core, and pinned SpecGate.
No Rust product behavior or signature was changed.

Rust reconstructs the recorded tree as native `Value`, then calls
`to_geography` or `to_geometry`. Its number model is binary64 only.
Recorded CLR `int`, `long`, `single`, and `decimal` values therefore use a
binary64 projection. The tested numeric values are exactly representable;
this is not evidence for native CLR numeric-type identity or untested
decimal rounding. The `concreteLineString` input remains recorded but has
no native effect because Rust has no matching generic formatter cast API.

Materialized Rust writes use real `SpatialBuilder` values and
`from_geography` or `from_geometry`. Raw writes drive `local_writer_pipeline`
and record real `EventSink` calls. Injected sink failures retain the native
`odata_spatial_core::error::SpatialError` type name and unmodified Display
text, including its cause chain. The adapters do not fabricate CLR errors,
normalize messages, invent coordinates, or coerce rejected shapes.

Replay contains the same 17 scenarios and 169 operations. Of those
operations, 136 have identical recorded completions. Strict comparison
reports 49 mismatch entries across 33 operations:

- Fifteen reads return a C# result but a Rust error: three geography range
  cases, two depth-29 cases, two changed-child-CRS cases, four short/open-ring
  cases, and four singleton LineString base/concrete cases.
- Two explicit-null-CRS errors differ in message. C# records
  `Object reference not set to an instance of an object.`; Rust records
  `Cannot read the value 'null' as a JSON object.`
- Sixteen injected sink failures differ in both `FailureType` and
  `FailureMessage`. Their attempted callback prefixes and failure flags
  match. Rust retains the cause chain; C# records its injected exception
  message only.

All 30 materialized writes match. The six successful raw-writer observations
match, including M-only null Z and singleton LineString output.
The 49 entries are observed differences, not approved exceptions.

Recorded C# behavior includes the singleton LineString base/concrete result
distinction, short and open rings, changed child CRS, coordinates outside
validator ranges, and spatial depth 29. Explicit null CRS throws
`NullReferenceException`. These observations are not accepted Rust
differences. Non-finite ordinates remain native-only because the pinned
trace writer encodes non-finite f64 as JSON null.

FullGlobe writer rejection, arbitrary formatter subclassing, interface
identity, cross-topology interleaving, and reset/reuse after a sink failure
are not recorded. Do not infer direct native API equivalence from this
adapter reference.

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
| `07-geojson` | Exit `1`, `equivalent: false`, 49 mismatch entries: 15 read outcome kinds, two null-CRS error messages, 16 sink-failure type names, and 16 sink-failure messages. |

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