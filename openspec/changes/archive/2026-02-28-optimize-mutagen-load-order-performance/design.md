## Context

Current startup flow performs expensive Mutagen environment construction in places that only require load-order metadata. `PluginListManager` builds an environment to list plugin names, and `PluginProcessingService` builds another environment to create a membership dictionary before record extraction begins. In large load orders this duplicates overlay discovery/import work and increases startup latency.

At the same time, separated-master games (notably Starfield) require reliable master-style lookup during overlay reads for correct FormID translation. The current direct overlay open path does not consistently provide an explicit `MasterFlagsLookup`, which creates correctness risk for light/medium master interpretation.

Constraints:
- Preserve existing supported game release behavior and plugin filtering UX.
- Keep implementation inside existing service boundaries where possible (no broad UI rewrite).
- Maintain current cancellation and error-reporting behavior.
- Avoid introducing expensive preloading that shifts cost from startup to memory pressure.

## Goals / Non-Goals

**Goals:**
- Reduce startup overhead by replacing redundant `GameEnvironment` materialization with lightweight load-order metadata snapshots.
- Ensure overlay reads for separated-master releases receive `BinaryReadParameters` with correct `MasterFlagsLookup`.
- Keep plugin validation and warning semantics deterministic for a single processing run.
- Add tests that lock in the new behavior and guard against performance regressions.

**Non-Goals:**
- Rewriting record extraction logic or database schema.
- Changing user-facing workflow for selecting plugins.
- Introducing a global cache shared across application restarts.
- Optimizing every Mutagen call site beyond load-order discovery and overlay read-parameter correctness.

## Decisions

1. **Introduce a lightweight load-order snapshot model used by service layer flows.**
   - Snapshot includes ordered listed plugin names, fast membership lookup, and (when needed) master-style entries.
   - Rationale: separates "metadata discovery" from "full mod import", avoiding heavyweight environment build for name/membership checks.
   - Alternatives considered:
     - Keep `GameEnvironment` and dispose quickly: still pays repeated import cost.
     - Cache full environment object: high memory/file-handle complexity and invalidation burden.

2. **Use Mutagen listing APIs for load-order discovery instead of constructing `GameEnvironment`.**
   - Build snapshot from Mutagen listing providers that read plugin/load-order files and implicit entries.
   - Rationale: preserves Mutagen ordering semantics while removing unnecessary mod materialization.
   - Alternative considered: parse `plugins.txt` manually in app code. Rejected due to game-specific semantics and duplicated parser logic.

3. **Compute `MasterFlagsLookup` once per processing run for separated-master releases only.**
   - For releases with separated master load orders, derive `KeyedMasterStyle` entries from listed existing plugin headers and build a lookup cache.
   - Rationale: correctness for FormID translation with bounded startup cost.
   - Alternative considered: open overlays without lookup. Rejected due to known correctness risk in separated-master scenarios.

4. **Pass `BinaryReadParameters` into overlay creation in `ModProcessor`.**
   - Overlay open methods receive read parameters carrying the snapshot-derived `MasterFlagsLookup` when applicable.
   - Rationale: keeps record extraction path unchanged while fixing separated-master context propagation.
   - Alternative considered: move to full `LoadOrder`/`LinkCache` per plugin. Rejected as unnecessary for current extraction needs.

5. **Adopt snapshot-at-processing-start semantics.**
   - Processing uses one immutable snapshot captured at run start; UI refresh may construct its own snapshot independently.
   - Rationale: deterministic behavior and simpler cancellation/error handling.
   - Alternative considered: live snapshot updates during processing. Rejected due to race conditions and harder reasoning.

## Risks / Trade-offs

- **[Risk] Snapshot can become stale if load-order files change mid-run** -> **Mitigation:** document run-start snapshot semantics and refresh on the next run.
- **[Risk] Header reads for master-style lookup add overhead on separated-master games** -> **Mitigation:** perform once per run and only for releases that require separated master lookup.
- **[Risk] Behavior drift from current warning paths** -> **Mitigation:** preserve warning conditions and add unit tests for missing-from-load-order and missing-file scenarios.
- **[Risk] Mutagen listing behavior differs by game/version** -> **Mitigation:** route through Mutagen listing APIs rather than custom parsing, and add release-coverage tests for representative games.

## Migration Plan

1. Add snapshot-building service/abstraction and tests for listing/membership/master-style output.
2. Update `GameLoadOrderProvider` and `PluginListManager` to consume snapshot listing names without `GameEnvironment` construction.
3. Update `PluginProcessingService` to build one processing snapshot and pass it into plugin processing flow.
4. Update `ModProcessor` overlay open path to accept and apply snapshot-derived `BinaryReadParameters`.
5. Run unit/integration/performance tests focused on plugin startup latency and separated-master correctness.

Rollback strategy:
- Revert to previous provider/processing path that used `GameEnvironment` construction and direct overlay opens without snapshot-derived read parameters.

## Open Questions

- Should processing always build a fresh snapshot at run start, or optionally reuse the most recent UI refresh snapshot when directories match?
- For separated-master lookup generation, should missing listed files fail-fast or degrade gracefully with warning + partial lookup?
- Do we want an explicit diagnostic metric (e.g., startup snapshot build ms) exposed in logs for future tuning?
