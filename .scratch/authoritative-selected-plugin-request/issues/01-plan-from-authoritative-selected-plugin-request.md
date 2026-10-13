# 01: Plan from the authoritative selected-Plugin request

**What to build:** Make a selected-Plugin Dry Run carry the exact accepted Processing Run request through Plugin Ingestion and into a plan whose entries demonstrably correspond one-for-one to that authoritative selection. Planning must preserve the selected Plugin names, casing, and order without retaining unrelated Processing Run facts after the plan has been constructed.

**Blocked by:** None (can start immediately).

**Status:** resolved

- [x] Plugin Ingestion planning accepts the public selected-Plugin Processing Run request used by callers, rather than reconstructing or accepting a second selected-Plugin request.
- [x] Processing Run passes the same request instance it accepted to the planning operation, and an executor-level contract test fails if a mapper or copy is reintroduced.
- [x] A plan requires a non-null authoritative request and exactly one non-null planned entry for every selected Plugin.
- [x] Plan construction rejects missing, extra, renamed, recased, or reordered entries using ordinal comparison against the authoritative selection.
- [x] A valid plan snapshots and exposes only its ordered planned entries; it does not retain or expose the complete Processing Run request.
- [x] Dry Run continues to allow a blank FormID Record Store path and opens no Store.
- [x] Existing Data-directory canonicalization, Game Load Order preparation, overlay lifetime, cancellation, Plugin-specific failure classification, and Unresolvable Master behavior remain unchanged.
- [x] Contract tests require the planning operation to accept the public selected-Plugin Processing Run request, while the still-unmigrated ingestion operation remains intact for the next ticket.
- [x] The supported build sequence and non-performance test suite pass.

## Completion

Implemented planning with the exact accepted `PluginProcessingRunRequest`. Plans validate entries against the authoritative selection with ordinal comparison and retain only an immutable entry snapshot. Real ingestion remains on its existing request contract for ticket 02.

Validation: the supported WinUI x64 build and solution build passed; all 704 non-performance tests passed. Independent Standards and Spec reviews found no issues. Existing SQLite NuGet advisory warnings remain.

The suite exposed two pre-existing blockers, also corrected: `SupportedGameReleases` built its lookup before its table initialized, and the projected-property architecture test incorrectly rejected private setters. Obsolete empty-plan test fixtures were replaced; the empty-plan rendering test and its documentation were removed. Plan constructor documentation and the workflow default-outcome comment were updated for the new contract.
