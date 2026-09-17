# 02: Ingest from the authoritative selected-Plugin request

**What to build:** Make a real selected-Plugin Processing Run carry the exact accepted Processing Run request through Plugin Ingestion and into a report whose outcomes demonstrably correspond one-for-one to that authoritative selection. Ingestion must preserve selected Plugin names, casing, and order without retaining unrelated Processing Run facts after the report has been constructed.

**Blocked by:** None (can start immediately).

**Status:** ready-for-agent

- [ ] Plugin Ingestion accepts the public selected-Plugin Processing Run request used by callers, rather than reconstructing or accepting a second selected-Plugin request.
- [ ] Processing Run passes the same request instance it accepted to the ingestion operation, and an executor-level contract test fails if a mapper or copy is reintroduced.
- [ ] A report requires a non-null authoritative request and exactly one non-null outcome for every selected Plugin.
- [ ] Report construction rejects missing, extra, renamed, recased, or reordered outcomes using ordinal comparison against the authoritative selection.
- [ ] A valid report snapshots and exposes only its ordered outcomes; it does not retain or expose the complete Processing Run request.
- [ ] Non-Dry Runs continue to require a FormID Record Store path, and Processing Run retains Store opening, optimization, cancellation, and disposal ownership.
- [ ] Existing progress, Store writes, Update Mode behavior, overlay lifetime, cancellation, Skipped Plugin and Failed Plugin classification, and Unresolvable Master propagation remain unchanged.
- [ ] Contract tests require the ingestion operation to accept the public selected-Plugin Processing Run request, while the independently migrated planning operation can land before or after this ticket.
- [ ] The supported build sequence and non-performance test suite pass.
