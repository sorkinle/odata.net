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
| C# annotations, capture setup, and capture tests | Fork [`sorkinle/odata.net`](https://github.com/sorkinle/odata.net/tree/spike/specgate-spatial-coordinated), branch `spike/specgate-spatial-coordinated` |
| Binding and saved C# reference for each slice | `specgate-capture/slices/<slice>/` on the same fork branch |
| Rust test adapters | [`OData/odata-rs`](https://github.com/OData/odata-rs), in the slice PR branch |
| SpecGate | [`schgoo/specgate`](https://github.com/schgoo/specgate), at the commit in the version table |

API ledgers, slice plans, and full reports are kept in a separate review
workspace. They are not published in either repository.

## Reproduce with GitHub Copilot

The skill `.github/skills/specgate-spatial-repro` in this repository runs the
steps on this page. It sets up the repositories, builds SpecGate from source,
and runs replay. On request, it also runs discovery and a fresh capture with
the test gate. It then compares the results with the expected results below.

1. Clone this fork branch into an empty parent folder, as in step 2.
2. Start GitHub Copilot CLI in the `odata.net` clone. Copilot loads the skill
   from `.github/skills/`.
3. Ask, for example: "Use the specgate-spatial-repro skill to replay
   https://github.com/OData/odata-rs/pull/80." Add "with discovery and
   capture" for a full run.

The scripts need PowerShell 7 and the prerequisites below. You can also run
them without Copilot. See the skill's `SKILL.md` for the commands. They write
all results to `<parent>\specgate-out\` and do not change tracked files.
The skill selects the published binding and reference for the PR from this
branch. Reviewers do not need a separate workspace or a slice folder.

## Versions by slice

Later slices build on earlier code. Use the commits from the slice row, not
the current branch heads, to reproduce an earlier slice.

| Slice | Folder | odata-rs commit | odata.net fork commit | SpecGate commit |
| --- | --- | --- | --- | --- |
| 01 CoordinateSystem | `slices/01-coordinate-system` | `030ffb1e134cb6dc666d26c2e373656f68bc2105`, branch `spike/specgate-coordinate-system`, draft PR [#80](https://github.com/OData/odata-rs/pull/80). | [`e51470cce1805f569011be375b425b3b5e3bc82d`](https://github.com/sorkinle/odata.net/commit/e51470cce1805f569011be375b425b3b5e3bc82d) | `4a904c276e24177c84cf4fb7182c93e811137f9d` |

For a new slice, write "Pending" until its changes are committed, then
replace it with the commit hash. The odata.net fork commit
contains the annotations, capture setup, binding, and saved reference for the
slice. Later commits that change only this page do not change the evidence.

SpecGate has no `v0.6.0` tag. The pinned commit reports version `0.6.0` in
capture manifests. The odata-rs test dependency uses the same commit.

## Prerequisites

- Git.
- Read access to [`OData/odata-rs`](https://github.com/OData/odata-rs). The
  repository is private. The fork `sorkinle/odata.net` and SpecGate are public.
- PowerShell 7.2 or later, only for the Copilot skill scripts.
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

1. Clone SpecGate and check out the pinned commit:

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
   same commit into a local folder. The fork projects reference version
   `0.6.0-g4a904c27`, which is not on nuget.org. Replay does not need it.

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
  odata.net\   fork, branch spike/specgate-spatial-coordinated
  odata-rs\    slice PR branch
```

```powershell
git clone --branch spike/specgate-spatial-coordinated https://github.com/sorkinle/odata.net odata.net
git clone https://github.com/OData/odata-rs odata-rs
```

In `odata-rs`, check out the slice PR branch. In both clones, check out the
commits from the version table to reproduce a specific slice.

## 3. Replay the saved C# reference

The odata-rs test dependency gets `specgate` from Git. The pinned SpecGate CLI
accepts only path or registry sources for that dependency during replay. Until
this is fixed, add a temporary Cargo patch in the parent folder. It points the
Git dependency at the same commit in your SpecGate clone:

```powershell
New-Item -ItemType Directory <parent>\.cargo -Force
@"
[patch."https://github.com/schgoo/specgate"]
specgate = { path = '<specgate>\rust\crates\specgate' }
"@ | Set-Content <parent>\.cargo\config.toml
```

The patch changes `odata-rs\Cargo.lock`. Remove the patch and restore the lock
file after replay:

```powershell
Remove-Item <parent>\.cargo -Recurse -Force
git -C <parent>\odata-rs restore Cargo.lock
```

Run these commands from `odata.net\specgate-capture\slices\<slice>`. Run each
command separately. Stop if validation or replay fails.

```powershell
specgate validate bundle reference
specgate replay reference --binding specgate.yaml --impl rust --out <candidate.otlp.json> --force
specgate validate linked <candidate.otlp.json> reference\registry.ctsc.json
specgate compare reference\reference.otlp.json <candidate.otlp.json> --registry reference\registry.ctsc.json
```

Write the candidate trace outside the clone. `compare` returns exit code `1`
when it finds a difference. Compare its output with the expected result for
the slice:

| Slice | Expected `compare` result |
| --- | --- |
| 01 CoordinateSystem | `equivalent: false` with 44 mismatch lines. In 20 factory results, the `Id` and `Name` fields differ because the adapter emits empty placeholders (40 lines). No `EpsgId` field differs. Four `to_string` results differ in text, for example `GeographyCoordinateSystem(EpsgId=1)` and `Geography SRID 1`. |

A new validation error or a different set of mismatches is a new finding.

## 4. Optional: record a fresh C# reference

A fresh reference is valid only when all configured .NET 8 tests pass. A
failed, skipped, or missing test result blocks the reference, even when
SpecGate writes a valid bundle.

The slice `specgate.yaml` names the component and the C# test project. For
Slice 1, the component is `spatial.coordinate_system` and the project is
`..\..\Microsoft.Spatial.CoordinateSystem.Capture.csproj`.

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
   `specgate-capture-Microsoft.Spatial-*` in the system temporary folder.
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
   behavior or the capture changed. Review it before you replace the saved
   reference.

For capture details, see [README.md](README.md).

## Add a later slice

1. Base the C# changes on the current head of
   `spike/specgate-spatial-coordinated`. Base the odata-rs branch on the
   previous slice branch.
2. Add `specgate-capture/slices/<slice>/specgate.yaml` with paths relative to
   that file.
3. Capture with the test gate above. Save the validated bundle in
   `specgate-capture/slices/<slice>/reference/`.
4. Add a row to each table on this page. Put the canonical Rust PR URL as a
   Markdown link in the `odata-rs commit` column. Record the commits after
   the slice is committed.
