# Slice 5: completed polygon factory observations

This reference covers the separately approved completed-polygon boundary.
It does not establish parity for the fluent factory API.
Direct generic discovery failed because `T` has no registered component owner.
The boundary calls the real C# factory and reads its completed polygon.
The Rust test adapter calls the native polygon builder.

Both operations accept the same five inputs: nullable `srid`, ordered
`firstCoordinates`, `secondCoordinates`, `z`, and `m` columns.
Each outer array represents rings. Each inner array represents positions.
Optional ordinates preserve absence and present zero separately.
Empty polygons have no rings. Empty individual rings are outside this boundary.
Neither adapter closes a ring or changes invalid coordinates.

Both native implementations close the recorded open rings by appending the
complete first position, including Z/M. They retain different endpoint Z/M
when primary coordinates already match. Geography endpoints at longitude
0 and 360 cause both implementations to append the first position.
These results do not establish equal validation timing.

The null-SRID path selects DefaultGeography/DefaultGeometry in C# and WGS84/UNIT
in the Rust adapter. Observed SRIDs are 4326 and 0.
This is adapter default selection, not a native Rust default factory API.
Rust `Id=""` and `Name=""` are missing-field placeholders.
They must not be reconstructed from the SRID or counted as matches.

## Evidence

The unique .NET 8 project links the existing 19 factory Facts and adds
20 focused test cases. The published bootstrap and clean recapture scripts
each preserved 39 results, all Passed exactly once.
Only 14 cases call the annotated boundary and produce recorded scenarios.
The other 25 cases are passing native coverage, not replay comparisons.

Normal discovery matches the saved registry. Clean recapture matches the saved
reference. Rust replay produces 14 scenarios and 14 operations.
Linked validation passes. All 70 named input values match exactly.

Strict comparison is not equivalent. It reports 25 mismatch lines:
22 `CoordinateSystem.Id`/`Name` placeholders and three native error messages.
Two errors reject one-position rings; one rejects latitude 91.
The native messages are not normalized. Error type, structured metadata,
fluent state, implicit conversions, and validation timing remain unpaired.
The recorded error name is the generic `error`.
The Rust adapter retains native Display text through `String`.

The product project sets `CopyLocalLockFileAssemblies=true` because discovery
otherwise fails to resolve `SpecGate.Annotations`.
This is a capture-setup requirement, not a factory behavior change.
Capture builds emit CS3016 warnings for array arguments to `SpecException`.
The exact-once gate still passes.

## Reproduction

Use the scripts in `.github\skills\specgate-spatial-repro\scripts`.
Select `-Slice 05-factories` and component `spatial.factories.polygons`.
Pass the source build at SpecGate commit
`4a904c276e24177c84cf4fb7182c93e811137f9d` through `-SpecGateRoot`.
Fresh capture also needs the matching annotations feed through `-FeedPath`.
Do not accept a fresh capture without the published exact-once test gate.
Replay uses the published temporary Cargo patch wrapper.
See `specgate-capture\REPRODUCE.md` for tool setup and wrapper details.
