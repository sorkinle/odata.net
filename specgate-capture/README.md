# SpecGate capture: CoordinateSystem

This folder records one SpecGate reference bundle for `Microsoft.Spatial.CoordinateSystem`.
The bundle is the C# reference for the odata-rs spatial parity review.

## What the capture records

| Operation | Source test | Recorded result |
| --- | --- | --- |
| `geography`, `geometry` | Existing `CoordinateSystemTests` and the two null tests in this folder | `EpsgId`, `Id`, and `Name` |
| `to_string` | `ToStringImplementation` | Returned string |
| `to_wkt_id` | `ToWktImplementation` | Returned string |

The expected result is 8 scenarios and 13 operations.

`GetHashCode` is not a SpecGate operation. Rust has no numeric hash-code API
to replay it against, and SpecGate replay stops at planning when a recorded
operation is missing from the candidate. The existing `GetHashCodeImplementation`
test is the only evidence for the C# hash value. In the capture, that test
records only its `geography` call.

## Capture setup

- `CoordinateSystem.Geography` and `CoordinateSystem.Geometry` are SpecGate operations.
- The instance operations need a SpecGate setup that produces the receiver.
  SpecGate rejects a method that is both an operation and a setup.
- `CoordinateSystemCaptureSetup.Geography` is the setup. It is `internal` and
  only calls `CoordinateSystem.Geography`. It does not change public API or behavior.
- The setup is in `src/Microsoft.Spatial` because SpecGate discovers and weaves
  only the project named by `sources` in `specgate.yaml`.
- `ToStringImplementation` and `ToWktImplementation` create their receiver
  through this setup. Their assertions are unchanged.

## Run the capture

Prerequisites: the .NET 8 SDK and the SpecGate 0.6.0 CLI.

1. Go to the repository root.
2. Run the tests:

   ```powershell
   dotnet test specgate-capture\Microsoft.Spatial.CoordinateSystem.Capture.csproj --framework net8.0
   ```

3. Capture the bundle:

   ```powershell
   specgate capture --binding specgate.yaml --component spatial.coordinate_system --impl csharp --out <bundle-dir> --force
   ```

   Expected output: `Complete(component=spatial.coordinate_system, scenarios=8, operations=13, ...)`.

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
