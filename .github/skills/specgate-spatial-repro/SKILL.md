---
name: specgate-spatial-repro
description: "Reproduces the SpecGate parity evidence for the odata-rs spatial slices. Sets up SpecGate from source, the annotated odata.net fork branch, and the odata-rs slice branch, then runs discover, capture, and replay for a slice and compares the results with the published expectations. Use when asked to reproduce, replay, recapture, or verify Microsoft.Spatial SpecGate evidence for an odata-rs spatial PR."
---

# Reproduce the SpecGate spatial parity evidence

This skill reproduces the evidence for one spatial slice. It compares the C#
behavior of Microsoft.Spatial in OData.NET with the Rust behavior in
`odata-rs`. The shared guide is `specgate-capture/REPRODUCE.md` in this
repository. That guide is the source of truth for commits and expected
results. This skill automates its steps.

A replay shows how the Rust code behaves on the recorded C# inputs. It does
not prove parity. Report the observed results. Do not claim parity.

## Rules

- Do not change tracked files in any clone. All output goes to
  `<parent>\specgate-out\<slice>\`, outside the clones.
- Do not edit the saved reference in `specgate-capture/slices/<slice>/reference/`.
- Do not change Rust or C# code to make results match. A difference from the
  expected result is a finding. Report it.
- Do not push, commit, or open pull requests.
- Run each script once per step. If a script fails, report its output and
  the log files. Do not retry with other options unless the user agrees.
- Use PowerShell 7 (`pwsh`). The scripts do not support Windows PowerShell 5.1.

## Inputs

Ask the user only for inputs that you cannot find:

| Input | Default |
| --- | --- |
| Parent folder | The parent of the `odata.net` clone that contains this skill. |
| Slice | The only folder under `specgate-capture/slices/`, or ask if there are more. |
| odata-rs source | The odata-rs commit for the slice in the version table of `REPRODUCE.md`. If the table says "Pending", ask for the odata-rs branch, pull request, or local path. |
| Tasks | Replay. Run discover and capture only when the user asks, or asks for full reproduction. |

## Folder layout

The slice bindings use relative paths. All repositories must be siblings:

```text
<parent>\
  odata.net\      fork sorkinle/odata.net, branch spike/specgate-spatial-coordinated
  odata-rs\       odata-rs at the slice commit or branch
  specgate\       SpecGate source build (created by step 3)
  specgate-feed\  local NuGet feed for SpecGate.Annotations (created by step 3)
  specgate-out\   results (created by steps 4 to 6)
```

The scripts are in `.github/skills/specgate-spatial-repro/scripts/` in the
`odata.net` clone. In the commands below, `$skill` is that folder.

## Step 1: Set up the repositories

1. If this skill was loaded from a personal skills folder and no `odata.net`
   clone exists, clone the fork into the parent folder:

   ```powershell
   git clone --branch spike/specgate-spatial-coordinated https://github.com/sorkinle/odata.net <parent>\odata.net
   ```

   Then use the scripts from that clone.
2. In `odata.net`, read `specgate-capture/REPRODUCE.md`. Find the row for
   the slice in the versions table. Check out the odata.net fork commit from
   that row in detached mode. If you check out a commit, tell the user.
   Run `git status --short` first. Stop if the clone has changes.
3. If `<parent>\odata-rs` does not exist, clone `https://github.com/OData/odata-rs`
   into it. Check out the odata-rs commit from the row, or the branch or
   pull request that the user gave. For a pull request, use
   `git fetch origin pull/<number>/head` and check out `FETCH_HEAD`.
4. Check that `odata-rs\Cargo.toml` has a `specgate` dependency with a `git`
   URL and a `rev`. If it does not, the odata-rs checkout does not contain
   the slice. Stop and tell the user.

## Step 2: Check the tools

```powershell
pwsh -File $skill\scripts\Test-Prerequisites.ps1
```

Add `-ForCapture` if the user wants discover or capture. If a required tool
is missing, tell the user which one and stop. Do not install tools without
the user's agreement.

## Step 3: Install SpecGate from source

```powershell
pwsh -File $skill\scripts\Install-SpecGate.ps1
```

The script reads the pinned commit from `odata-rs\Cargo.toml`. It clones
SpecGate into `<parent>\specgate`, builds the CLI and the C# tools, and packs
`SpecGate.Annotations` into `<parent>\specgate-feed`. The build takes several
minutes. Use `-SpecGateRoot` and `-FeedPath` for other folders. If the script
warns that NuGet already caches the annotations package, tell the user.

The script does not change `PATH`. The other scripts call the CLI by its
full path.

## Step 4: Replay the saved reference

```powershell
pwsh -File $skill\scripts\Invoke-SliceReplay.ps1 -Slice <slice>
```

The script validates the saved bundle, replays it against odata-rs,
validates the candidate trace against the registry, and compares the traces.
For the replay only, it adds a temporary `<parent>\.cargo\config.toml` patch,
because the pinned CLI rejects the Git-sourced `specgate` dependency. It then
removes the patch and restores `odata-rs\Cargo.lock`. It stops if a
`<parent>\.cargo\config.toml` already exists.

Exit code `1` means that compare found differences. That is the expected
result for slices with known differences. Exit code `2` means that a step
failed before compare finished.

Compare `summary.json` and `compare.log` in the output folder with the
expected result for the slice in `REPRODUCE.md`. Report:

- whether the mismatch count and the operations and fields match the expected result
- any mismatch that the expected result does not list
- any expected mismatch that is absent

## Step 5: Run discovery (optional)

```powershell
pwsh -File $skill\scripts\Invoke-SliceDiscover.ps1 -Slice <slice>
```

The script runs `specgate discover` on the annotated C# sources and validates
the registry. It then checks that the registry has the same content as the
saved reference registry. A difference means that the annotations changed
after the reference was recorded. Report it.

## Step 6: Record a fresh C# reference (optional)

```powershell
pwsh -File $skill\scripts\Invoke-SliceCapture.ps1 -Slice <slice>
```

The script runs the C# capture tests on .NET 8, runs `specgate capture`,
copies the per-test `.trx` results while capture runs, and enforces the test
gate: each configured test must have exactly one `Passed` result. It then
validates the fresh bundle and compares it with the saved reference.

A fresh reference is valid only if the test gate passed. A valid bundle
alone does not show that the tests passed. If the gate fails, report the
lines in `test-gate.log`. If the fresh reference differs from the saved
reference, report the differences from `compare-with-saved-reference.log`.

## Step 7: Report

Give the user a short report with:

- the commits used: odata.net fork, odata-rs, and SpecGate (from `summary.json`)
- whether odata-rs had uncommitted changes (`odataRsDirty`)
- the tools: Rust, .NET SDK, and .NET runtime versions
- for each step that ran: the result, and the output folder
- every difference from the expected result in `REPRODUCE.md`
- the steps that did not run, and why

Do not describe a replay with differences as parity. Do not describe a
skipped step as passed.
