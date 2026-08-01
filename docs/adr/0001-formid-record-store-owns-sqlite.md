# ADR-0001: FormID Record Store owns production SQLite behavior

- Status: Accepted
- Date: 2026-07-21

## Context

SQLite connection setup, GameRelease schema preparation, FormID persistence, and database maintenance previously spanned
the FormID Record Store and a shallow database service. That split let production and tests prepare databases through a
different seam from the one used for ingestion, distributed lifecycle rules across modules, and required duplicate
connections during Store opening.

The Processing Run boundary already depends on a small run-scoped Store interface and opener. Its lifecycle also needs
optimization failures to remain visible while failed or cancelled ingestion must not trigger maintenance during cleanup.

## Decision

`FormIdRecordStore.OpenAsync` is the sole production entry point for SQLite-backed FormID Record Store setup. It owns
connection-string construction, configures its single owned connection, prepares the selected GameRelease schema as one
transaction, creates temporary staging resources, and returns only when the Store is ready for immediate use.

The Store also owns persisted FormID operations and `OptimizeAsync`. Optimization remains an explicit successful-run
step that checkpoints the write-ahead log and asks SQLite to optimize. `DisposeAsync` performs cleanup only; it never
optimizes and must not replace the primary Processing Run exception.

The existing run-scoped Store interface and opener remain the Processing Run substitution seam. Production code must not
introduce another schema helper, database service, repository-wide SQLite abstraction, or test-only setup interface.
Tests open Stores through the production seam; separate raw SQLite connections are permitted for workload generation,
failure injection, and persisted-state inspection.

## Consequences

- SQLite lifecycle and schema changes are localized to `FormIdRecordStore`.
- Store tests cover ready-on-return opening and behavior details through the public Store seam.
- Architecture verification pins production `Microsoft.Data.Sqlite` use to the Store and rejects the retired setup types.
- Processing Runs continue to observe explicit optimization success, failure, and cancellation semantics.
- Tests that need SQLite implementation access must remain inspection or workload clients rather than alternate setup owners.
