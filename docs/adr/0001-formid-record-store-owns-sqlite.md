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

### Parent directory creation (2026-10-17, ADR-0010)

Ready-on-return opening also creates the database file's parent directory when it is missing. Previously the default
database path's directory was created by the module that computed the path, before the request was validated. The
Store is now the only production code that creates it, so a Dry Run or a run that Processing Run Admission refuses
creates nothing, and a path the user picked whose folder was later deleted opens rather than failing.

### FormID text file reading (2026-10-18)

The Store persists FormID text rows. It does not read FormID text files. Previously `ImportFormIdTextFileAsync` did
four jobs inside the Store:

- opened the file;
- parsed its pipe-delimited rows;
- counted records and Plugins;
- reported import progress.

None of that work involves SQLite, and it left the Store as the only home of a domain concept, the FormID text file. The
counter encoding it reported also had to be decoded the same way by the Processing Run and Processing Run Presentation
(ADR-0007).

That work moves to FormID Text Ingestion, the sibling of Plugin Ingestion.

- **The seam.** FormID Text Ingestion hands the Store a lazy stream of parsed rows through a text-row writer role that
  FormID Text Ingestion owns. This is the same shape as the Plugin write role.
- **What stays in the Store.** The Store consumes the stream once, stages it in its temporary table, and commits one
  transaction per Plugin. It clears staging when the stream or a commit fails. Every SQLite concern this ADR assigns to
  the Store stays here.
- **What is deleted.** `ImportFormIdTextFileAsync` and the public `FormIdStoreProgress` hierarchy are removed, with no
  forwarding members kept.

## Consequences

- SQLite lifecycle and schema changes are localized to `FormIdRecordStore`.
- Store tests cover ready-on-return opening and behavior details through the public Store seam.
- Architecture verification pins production `Microsoft.Data.Sqlite` use to the Store and rejects the retired setup types.
- Processing Runs continue to observe explicit optimization success, failure, and cancellation semantics.
- Tests that need SQLite implementation access must remain inspection or workload clients rather than alternate setup owners.
