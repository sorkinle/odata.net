---
name: specgate-spatial-repro
description: "Reproduces the SpecGate parity evidence for an odata-rs spatial PR. Sets up SpecGate from source, the annotated odata.net branch, and the Rust PR, then selects the published reference and runs replay. Can also run discovery and capture. Use when asked to reproduce, replay, recapture, or verify Microsoft.Spatial SpecGate evidence."
---

# Reproduce the SpecGate spatial parity evidence

This skill reproduces the evidence for one odata-rs spatial PR. It compares
Microsoft.Spatial behavior in OData.NET with Rust behavior in `odata-rs`.
`specgate-capture/REPRODUCE.md` in the annotated branch records the published
references, commits, and expected results.

A replay shows how the Rust code behaves on the recorded C# inputs. It does
not prove parity. Report the observed results. Do not claim parity.

## Rules

- Check out a branch or PR only in a clean clone. Do not retain changes to
  tracked files. All output goes to `<parent>\specgate-out\<slice>\`, outside
  the clones.
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
| Annotated odata.net source | The existing fork checkout, or `sorkinle/odata.net` branch `spike/specgate-spatial-coordinated`. |
| Target Rust PR | The current PR, if known. Otherwise ask for its URL. |
| Parent folder | The parent of the `odata.net` clone that contains this skill. |
| Tasks | Replay. Run discover and capture only when the user asks, or asks for full reproduction. |

Do not ask the user for a slice name or folder. The annotated branch contains
the binding and saved reference. Select them from the `REPRODUCE.md` row for
the target PR. If that row is missing, ambiguous, or incomplete, report the
missing published reference and stop. Do not replay a different component.

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
   clone exists, clone the annotated source branch into the parent folder:

   ```powershell
   git clone --branch <source-branch> <source-repository-url> <parent>\odata.net
   ```

   Use the default fork and branch from the inputs table if the user did not
   supply another. Use the scripts from that clone. If the branch does not
   contain the skill or a published binding and reference, report what is
   missing and stop.
2. In `odata.net`, run `git status --short`. Stop if the clone has changes.
   Use the requested annotated branch. Do not check out its older evidence
   commit: that commit may not contain the skill scripts.
3. If `<parent>\odata-rs` does not exist, clone `https://github.com/OData/odata-rs`
   into it. The repository is private. If the clone reports
   `Repository not found`, tell the user that they need read access to
   `OData/odata-rs`, and stop. Check that the clone is clean before checkout.
   Fetch the target PR with `git fetch origin pull/<number>/head`, then check
   out `FETCH_HEAD`. Do not discard existing changes.
4. Read `specgate-capture/REPRODUCE.md`. Find the versions-table row for the
   target PR URL. The row identifies the reference folder and the fork
   evidence commit. Do not ask the user for that folder. Check that its
   contents have not changed since the evidence commit:

   ```powershell
   git -C <parent>\odata.net diff --quiet <fork-commit> HEAD -- specgate-capture/slices/<slice>
   ```

   Exit code `1` means that the binding or saved reference changed. Tell the
   user and stop. For discover or capture of an older slice, tell the user
   that the C# sources at the branch head can include later annotations.
   The requested PR head may differ from the recorded odata-rs commit. Report
   both commits and compare the results with the published expectations.
5. Check that `odata-rs\Cargo.toml` has a `specgate` dependency with a `git`
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
pwsh -File $skill\scripts\Invoke-SliceReplay.ps1 -PullRequest <rust-pr-url>
```

The script selects the published reference for the PR URL. It validates the
saved bundle, replays it against odata-rs,
validates the candidate trace against the registry, and compares the traces.
For the replay only, it adds a temporary `<parent>\.cargo\config.toml` patch,
because the pinned CLI rejects the Git-sourced `specgate` dependency. It stops
if `<parent>\.cargo\config.toml` or `<parent>\.cargo\config` exists. It removes
only its own unchanged patch and restores `odata-rs\Cargo.lock`, even if replay
fails. If the lockfile changed during replay, its post-replay contents are
saved as `Cargo.lock.after-replay` in the output folder. Report any cleanup
error and the paths it gives; do not delete or overwrite those files.
Do not edit the lockfile or run other Cargo commands in that checkout during
replay. Inspect the saved post-replay file for any concurrent changes.

If the output folder is not empty, choose another `-OutDir`. Alternatively,
use `-Force` to create a new run folder inside it. The script prints the actual
output path. This rule also applies to discovery and capture.

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
pwsh -File $skill\scripts\Invoke-SliceDiscover.ps1 -PullRequest <rust-pr-url>
```

The script runs `specgate discover` on the annotated C# sources and validates
the registry. It then checks that the registry has the same content as the
saved reference registry. A difference means that the annotations changed
after the reference was recorded. Report it.

## Step 6: Record a fresh C# reference (optional)

```powershell
pwsh -File $skill\scripts\Invoke-SliceCapture.ps1 -PullRequest <rust-pr-url>
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
