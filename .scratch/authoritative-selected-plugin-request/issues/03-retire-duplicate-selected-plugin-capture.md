# 03: Retire duplicate selected-Plugin capture

**What to build:** Complete the migration to one selected-Plugin authority by removing the duplicate internal request, the field-for-field Processing Run translation, and the standalone selection-capture helper. Keep immutable ordered selection capture and all existing validation behavior local to the public selected-Plugin Processing Run request, and pin that architecture against regression.

**Blocked by:** 01: Plan from the authoritative selected-Plugin request; 02: Ingest from the authoritative selected-Plugin request.

**Status:** ready-for-agent

- [ ] The duplicate internal selected-Plugin request and Processing Run translation are removed without a compatibility shim or replacement adapter.
- [ ] The standalone selection-capture helper is removed, and its eager immutable snapshot behavior is private implementation of the public selected-Plugin Processing Run request.
- [ ] The existing public constructor and properties remain source-compatible, and callers can still construct a request from explicit game directory, GameRelease, Plugin names, Update Mode, Store path, and Dry Run facts.
- [ ] Request construction preserves original Plugin casing and order while rejecting an empty selection, blank names, and case-insensitive duplicates.
- [ ] A blank game directory remains invalid; a Store path remains required for a real run and optional for a Dry Run.
- [ ] Existing exception types, exact user-facing validation wording, and validation precedence remain unchanged, including when several inputs are invalid.
- [ ] Reflection and architecture contracts require both Plugin Ingestion operations to accept the public selected-Plugin Processing Run request and fail if the retired request, mapper, or standalone capture helper returns.
- [ ] Tests no longer construct or directly test the retired internal request; request invariants are covered through the same public contract production uses.
- [ ] Plans and reports continue to use the request only for construction-time correlation and expose only their own relevant facts.
- [ ] Existing comments remain intact unless their code is deleted or they become misleading; rewritten interfaces retain concise documentation of their contracts.
- [ ] The supported build sequence and non-performance test suite pass.
