## 1. Build load-order snapshot foundation

- [x] 1.1 Define a run-scoped load-order snapshot model (ordered names, membership lookup, optional master-style lookup data).
- [x] 1.2 Implement snapshot construction from Mutagen load-order listing APIs without `GameEnvironment` materialization for listing-only use cases.
- [x] 1.3 Implement separated-master lookup population during snapshot creation for releases that require `MasterFlagsLookup`.
- [x] 1.4 Add unit tests for snapshot construction across representative releases, including missing-file and ordering behavior.

## 2. Integrate snapshot into refresh and processing startup

- [x] 2.1 Update `GameLoadOrderProvider` to return names from snapshot/listing metadata instead of building `GameEnvironment`.
- [x] 2.2 Update `PluginListManager.RefreshPluginList` to consume snapshot output while preserving current filtering and messaging behavior.
- [x] 2.3 Update `PluginProcessingService` to build one snapshot at run start and pass it through plugin-processing flow.
- [x] 2.4 Remove redundant startup load-order reconstruction paths that only existed for membership checks.

## 3. Apply separated-master read parameters in ModProcessor

- [x] 3.1 Extend `ModProcessor` inputs to accept processing snapshot context needed for membership and read parameters.
- [x] 3.2 Construct `BinaryReadParameters` for overlay opens using snapshot-derived `MasterFlagsLookup` for separated-master releases.
- [x] 3.3 Pass read parameters through all `CreateFromBinaryOverlay` branches while keeping non-separated releases compatible.
- [x] 3.4 Preserve existing warning-and-skip behavior when a selected plugin is absent from run snapshot membership.

## 4. Verify behavior and performance

- [x] 4.1 Update/add unit tests for `PluginListManager`, `PluginProcessingService`, and `ModProcessor` to cover snapshot semantics and warning paths.
- [x] 4.2 Add/adjust tests that validate separated-master overlay reads receive non-null `MasterFlagsLookup` for Starfield-class releases.
- [x] 4.3 Run targeted performance tests to confirm reduced startup overhead and no regression in record throughput.
- [x] 4.4 Run full impacted test suites and resolve failures before implementation is considered complete.
