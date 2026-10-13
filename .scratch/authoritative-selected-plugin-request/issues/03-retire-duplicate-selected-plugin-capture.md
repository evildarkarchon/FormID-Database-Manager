# 03: Retire duplicate selected-Plugin capture

**What to build:** Complete the migration to one selected-Plugin authority by removing the duplicate internal request, the field-for-field Processing Run translation, and the standalone selection-capture helper. Keep immutable ordered selection capture and all existing validation behavior local to the public selected-Plugin Processing Run request, and pin that architecture against regression.

**Blocked by:** 01: Plan from the authoritative selected-Plugin request; 02: Ingest from the authoritative selected-Plugin request.

**Status:** resolved

- [x] The duplicate internal selected-Plugin request and Processing Run translation are removed without a compatibility shim or replacement adapter.
- [x] The standalone selection-capture helper is removed, and its eager immutable snapshot behavior is private implementation of the public selected-Plugin Processing Run request.
- [x] The existing public constructor and properties remain source-compatible, and callers can still construct a request from explicit game directory, GameRelease, Plugin names, Update Mode, Store path, and Dry Run facts.
- [x] Request construction preserves original Plugin casing and order while rejecting an empty selection, blank names, and case-insensitive duplicates.
- [x] A blank game directory remains invalid; a Store path remains required for a real run and optional for a Dry Run.
- [x] Existing exception types, exact user-facing validation wording, and validation precedence remain unchanged, including when several inputs are invalid.
- [x] Reflection and architecture contracts require both Plugin Ingestion operations to accept the public selected-Plugin Processing Run request and fail if the retired request, mapper, or standalone capture helper returns.
- [x] Tests no longer construct or directly test the retired internal request; request invariants are covered through the same public contract production uses.
- [x] Plans and reports continue to use the request only for construction-time correlation and expose only their own relevant facts.
- [x] Existing comments remain intact unless their code is deleted or they become misleading; rewritten interfaces retain concise documentation of their contracts.
- [x] The supported build sequence and non-performance test suite pass.

## Completion

Implemented 2026-09-16. Removed the duplicate internal request and standalone capture helper; immutable ordered capture now lives privately in `PluginProcessingRunRequest`. The mapper was already removed by ticket 02. Public construction, validation wording, exception translation, and validation precedence are preserved. Removed duplicate internal-request tests and added public request characterization and architecture retirement guards.

Validation: both supported builds passed; all 716 non-performance tests passed. Standards review found one inaccurate new test comment, corrected before completion; no outstanding Standards or Spec findings. Existing NU1903 SQLite and xUnit2017 warnings remain.

Documentation attached to the deleted request and helper types was removed with those types. The capture method documentation and case-insensitive identity comment were retained; the constructor's exception-translation comment was updated to describe its now-private capture implementation.
