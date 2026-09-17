# 02: Ingest from the authoritative selected-Plugin request

**What to build:** Make a real selected-Plugin Processing Run carry the exact accepted Processing Run request through Plugin Ingestion and into a report whose outcomes demonstrably correspond one-for-one to that authoritative selection. Ingestion must preserve selected Plugin names, casing, and order without retaining unrelated Processing Run facts after the report has been constructed.

**Blocked by:** None (can start immediately).

**Status:** resolved

- [x] Plugin Ingestion accepts the public selected-Plugin Processing Run request used by callers, rather than reconstructing or accepting a second selected-Plugin request.
- [x] Processing Run passes the same request instance it accepted to the ingestion operation, and an executor-level contract test fails if a mapper or copy is reintroduced.
- [x] A report requires a non-null authoritative request and exactly one non-null outcome for every selected Plugin.
- [x] Report construction rejects missing, extra, renamed, recased, or reordered outcomes using ordinal comparison against the authoritative selection.
- [x] A valid report snapshots and exposes only its ordered outcomes; it does not retain or expose the complete Processing Run request.
- [x] Non-Dry Runs continue to require a FormID Record Store path, and Processing Run retains Store opening, optimization, cancellation, and disposal ownership.
- [x] Existing progress, Store writes, Update Mode behavior, overlay lifetime, cancellation, Skipped Plugin and Failed Plugin classification, and Unresolvable Master propagation remain unchanged.
- [x] Contract tests require the ingestion operation to accept the public selected-Plugin Processing Run request, while the independently migrated planning operation can land before or after this ticket.
- [x] The supported build sequence and non-performance test suite pass.


## Completion

Implemented 2026-09-16. Ingestion and report construction now accept the authoritative public Processing Run request; the executor passes the original instance, protected by an identity contract test. Reports retain only the immutable ordered outcomes and reject invalid correlation. Migrated ingestion callers while preserving their behavior scenarios. Removed the unused mapper and its associated documentation; duplicate request and selection-capture retirement remain in ticket 03.

Validation: supported WinUI x64 build and solution build passed; all 710 non-performance tests passed. Standards review: 0 findings. Spec review: 0 findings. Existing NU1903 SQLite package warning remains.
