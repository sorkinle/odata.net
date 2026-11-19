# SpecGate capture: CoordinateSystem

This folder records one SpecGate reference bundle for `Microsoft.Spatial.CoordinateSystem`.
The bundle is the C# reference for the odata-rs spatial parity review.

## What the capture records

| Operation | Source test | Recorded result |
| --- | --- | --- |
| `geography`, `geometry` | `CoordinateSystemTests` and the two null tests in this folder | `EpsgId`, `Id`, and `Name` |
| `to_string` | `ToStringImplementation`, `InstanceFormattingUsesTopologyAndDefaults` | Returned string |
| `to_wkt_id` | `ToWktImplementation`, `InstanceFormattingUsesTopologyAndDefaults` | Returned string |

The workspace report records the scenario and operation counts for each capture.

`GetHashCode` is not a SpecGate operation. Rust has no numeric hash-code API
to replay it against, and SpecGate replay stops at planning when a recorded
operation is missing from the candidate. The existing `GetHashCodeImplementation`
test is the only evidence for the C# hash value. In the capture, that test
records only its `geography` call.

## Capture setup

- `CoordinateSystem.Geography` and `CoordinateSystem.Geometry` are SpecGate operations.
- The instance operations need a SpecGate setup that produces the receiver.
  SpecGate rejects a method that is both an operation and a setup.
- `CoordinateSystemCaptureSetup.Create` is the setup. It records the topology
  and delegates to the corresponding factory. It is `internal` and does not
  change public API or behavior.
- The setup is in `src/Microsoft.Spatial` because SpecGate discovers and weaves
  only the project named by `sources` in `specgate.yaml`.
- `ToStringImplementation` and `ToWktImplementation` create their receiver
  through this setup. Their assertions are unchanged.

## Run the capture

Prerequisites: the .NET 8 runtime, an SDK that builds `net8.0`, and the
SpecGate CLI built from commit `4a904c27`, and the matching
`SpecGate.Annotations` `0.6.0-g4a904c27` package. See
[REPRODUCE.md](REPRODUCE.md) for setup. The workspace pilot uses SDK 10.

1. Go to the repository root.
2. Run the tests:

   ```powershell
   dotnet test specgate-capture\Microsoft.Spatial.CoordinateSystem.Capture.csproj --framework net8.0
   ```

3. Capture the bundle:

   ```powershell
   specgate capture --binding specgate.yaml --component spatial.coordinate_system --impl csharp --out <bundle-dir> --force
   ```

   Check the reported scenario and operation counts against the capture.
   All configured tests must pass before and during capture. A valid bundle
   does not prove that every test passed. SpecGate removes its temporary
   per-test `.trx` files after capture. Copy them while capture runs and check
   every configured test result. A failed, skipped, or missing result blocks
   acceptance.

4. Validate the bundle:

   ```powershell
   specgate validate bundle <bundle-dir>
   ```

5. To check that a new capture matches an earlier one, compare the traces:

   ```powershell
   specgate compare <old-dir>\reference.otlp.json <new-dir>\reference.otlp.json --registry <old-dir>\registry.ctsc.json
   ```

   Expected output: `equivalent: true`.

A valid bundle is a C# reference only. It does not show Rust parity.
