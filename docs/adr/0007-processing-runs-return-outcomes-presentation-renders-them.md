# ADR-0007: A Processing Run returns outcomes; presentation renders them

- Status: Accepted
- Date: 2026-08-03

## Context

`ProcessingRunExecutor` used to both execute a Processing Run and write the prose describing it, and returned `Task`.
It carried five formatters and two progress adapters that rendered English, and its terminal facts were recoverable by
a caller only as status strings and exception types.

That cost more than tidiness. `PluginIngestionReport` — the authoritative ordered outcome of Plugin Ingestion — was
consumed inside the executor and then discarded, so no caller could ask how many Plugins were ingested, skipped, or
failed. `FormIdTextFileImportResult` was discarded outright, and a text run ended with a flat "Processing completed
successfully!" while a selected-Plugin run had always reported its counts. `UserWorkflow` reconstructed what a run had
done from three catch blocks and a three-way switch over a `ProcessingRunEvent` enum. `FormIdRecordStore` emitted
pre-rendered English into the same channel and branched on `UpdateMode` to decide whether to name a Plugin — a
presentation decision made inside the Store.

The Plugin List already had the intended shape to point at: a domain module publishing typed `PluginListActivity`, and
one adapter rendering it.

## Decision

**A Processing Run reports and returns values, and one module renders them into words.**

`ExecuteAsync` returns `Task<ProcessingRunOutcome>`, a closed union mirroring the request union and carrying the native
reports the run already produced: `PluginRunOutcome` holds the `PluginIngestionReport`, `FormIdTextRunOutcome` holds the
`FormIdTextFileImportResult`, `PlannedRunOutcome` holds a Processing Run Plan, and `CancelledRunOutcome` is the fourth
state. Between them these cover every terminal state — completed, completed with warnings, completed with failures, and
cancelled — because the report a Plugin run carries is what distinguishes the first three.

Transient progress is `ProcessingRunProgress`, a vocabulary the run defines for itself rather than one borrowed from a
collaborator: Plugin Ingestion's stage enum and the FormID Record Store's counters are both translated into it, so the
run says what it is doing in terms of its own work rather than in terms of whichever collaborator happens to be doing
it. `FormIdStoreProgress` reports Store facts without making presentation decisions.

`ProcessingRunPresentation` is the one module that turns either of those into wording. It is `internal static`, is
called rather than subscribed to, and holds no state. `ProcessingRun.cs` now contains no user-facing sentence at all,
which an architecture test enforces by pinning the retired ones by name.

### FormID text file progress representation (2026-09-16)

The original counters-and-most-recently-seen-Plugin representation required the Processing Run to remember and compare
Plugin names to reconstruct what the Store meant. Replace it directly with a public abstract record whose derivation
is closed to the assembly and whose two cases are internal: `FormIdStoreCounterUpdate` and
`FormIdStorePluginFirstEncountered`. Both carry `RecordCount`, `BytesRead`, and `TotalBytes`; the first-encounter case
also carries `PluginName`. No compatibility adapter retains the ambiguous representation.

The Store owns case-insensitive Plugin identity and reports the first encounter before staging that Plugin's first
record. It emits a counter update initially with zero records and at the existing periodic intervals. If a periodic
counter update and a first encounter coincide, the counter update comes first. These facts preserve the existing
report timing and ordering; a first encounter does not claim that a Plugin's records have been committed.

The Processing Run translates each report independently, with no remembered Plugin name. It retains the Update Mode
policy: first-encounter reports name Plugins in Update Mode and are suppressed in append mode. Counter updates are
translated in both modes. Processing Run Presentation remains unchanged, preserving the existing wording.

## Cancellation is a value; failure is not

The asymmetry is the part of this rule a future reader would otherwise have to infer, so it is recorded here.

**Cancellation is an outcome** because the token is executor-owned. `ProcessingRunExecutor` creates the
`CancellationTokenSource` for each execution and hands out no token that anything else can trip, so the only
`OperationCanceledException` that can surface from a run is the one this executor requested. That makes it precisely
attributable: catching it cannot swallow an unrelated cancellation coming from somewhere else, because there is nowhere
else for one to come from. A run that stops this way is a normal terminal state, and modelling it as a value removes the
`catch (OperationCanceledException)` block `UserWorkflow` needed in order to know a run had been cancelled at all.

**Failure stays an exception** because there is no equivalent way to be precise about it. Building a `Failed` outcome
would require catching `Exception` broadly, and ADR-0006 and `MutagenPluginOverlayReader` deliberately avoid exactly
that: the overlay reader's expected-failure list is kept narrow (issue #49) so an unexpected internal failure aborts
loudly instead of being disguised as a bad Plugin, and an Unresolvable Master fails the whole run rather than becoming
one Failed Plugin. A broad catch in the executor would undo both, converting every unanticipated bug into a tidy result
value that the caller would report as a normal ending. So `ProcessingRunValidationException`,
`UnresolvableMasterException`, and anything unforeseen propagate unchanged.

This does leave a failing run with something to say and no outcome to say it with, which is why
`ProcessingRunFailure` is a *progress* case: the last thing a failing run reports goes out on the transient channel,
like everything else it says while running, before the exception leaves.

## Why presentation renders but does not write

`ProcessingRunPresentation` returns a `RenderedRunReport` — an activity projection plus warning, error, and information
messages — and `UserWorkflow.ApplyProcessingRunOutcome` performs the ViewModel writes. It is therefore a role-only
mirror of `PluginListPresentationAdapter`, which writes the ViewModel itself. That difference is deliberate, and it is
why this module is not named `...Adapter`.

`UserWorkflow` keeps `_runActivity`, `_runActivityLock`, and the run-active gate. Both of those carry comments
explaining non-obvious races — the ordering fix behind issue #60, and the window between the workflow committing to a
run and the executor's cancellation source existing. A change about the *locality of wording* has no business also
relocating concurrency invariants into a module whose entire job is string construction; a stateless renderer that can
be tested by calling it with a value is worth more here than symmetry with the Plugin List adapter.

The same split decides ordering. Because the renderer only returns, `UserWorkflow` chooses that the message lists are
written before the transient status, matching the order the executor used to report them in. An inactive projection
means the outcome has nothing to say on the transient channel: a cancelled run's acknowledgement is a terminal fact and
belongs in the message lists, since the channel is handed back the moment a run ends and would erase it (#60). A Dry
Run's plan is terminal for the same reason and goes to the information messages.

## Consequences

- A caller can ask what a run did without parsing prose. `UserWorkflow` lost `ApplyProcessingRunEvent`, its
  three-way switch, and its cancellation catch block; `ProcessingRunEvent` and `ProcessingRunEventKind` were deleted
  outright rather than reshaped, because every producer of either kind fired terminally from `report.Outcomes` after
  ingestion returned.
- Tests split along the same seam: `ProcessingRunExecutorTests` assert outcomes, `ProcessingRunPresentationTests`
  assert wording. Wording that already existed was pinned by characterization tests written before any code moved.
- Two user-visible sentences changed, and only two. A text run's completion now reports its Plugin and record counts,
  matching what a selected-Plugin run always reported. A Dry Run reports would-ingest and would-skip per Plugin — or a
  text file's presence and size — instead of echoing back the selection the user had just made.
- A Dry Run now resolves the Data directory, prepares the load order, and opens overlays, so it can surface an
  Unresolvable Master (ADR-0006) that the old dry run could not have noticed. It still opens no FormID Record Store.
- The outcome and progress base types are public because the executor that returns and reports them is; every case is
  internal, because the reports they carry are, and derivation is closed to the assembly exactly as it is for the
  request union. A caller outside Core sees that a run ended, not how.
- An architecture test now fails the build if a user-facing sentence reappears in `ProcessingRun.cs`, or if
  "Would process" reappears anywhere in Core.
