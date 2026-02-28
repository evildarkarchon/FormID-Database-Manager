## Why

Plugin processing currently pays high setup cost by constructing full Mutagen `GameEnvironment` instances to retrieve load-order names and membership data, even though the app only needs lightweight listing metadata for those steps. We also need a safer path for separated-master games (notably Starfield) by ensuring overlay reads consistently receive the master-style lookup needed for correct FormID interpretation.

## What Changes

- Replace heavy load-order discovery paths that materialize full `GameEnvironment` objects with lightweight load-order listing snapshots derived from Mutagen listing APIs.
- Reuse one processing-time load-order snapshot instead of rebuilding equivalent load-order context multiple times in a single run.
- Build and supply `BinaryReadParameters` with `MasterFlagsLookup` for separated-master releases when opening plugin overlays.
- Align plugin validation and warning behavior with snapshot-based metadata so missing files and stale listings are handled consistently.
- Add focused tests for snapshot generation, separated-master read parameter behavior, and processing correctness/performance regression guards.

## Capabilities

### New Capabilities
- `mutagen-load-order-snapshot`: Provide a lightweight, reusable load-order metadata snapshot for plugin list refresh and processing startup without full environment materialization.
- `mutagen-separated-master-overlay-read`: Ensure separated-master game overlays are opened with the required master-style lookup so FormID translation remains correct.

### Modified Capabilities
- *(none)*

## Impact

- Affected code: `GameLoadOrderProvider`, `PluginListManager`, `PluginProcessingService`, `ModProcessor`, and related tests in `FormID Database Manager.Tests/Unit/Services`.
- Runtime behavior: reduced startup overhead before record extraction; fewer redundant load-order construction passes.
- Correctness: explicit separated-master read parameter handling for Starfield-class releases.
- Dependencies/APIs: continues using Mutagen APIs, but shifts from `GameEnvironment`-centric discovery toward listing and read-parameter-centric paths.
