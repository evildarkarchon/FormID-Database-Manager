# ADR-0012: Processing Run Control owns the active run

- Status: Accepted
- Date: 2026-10-18

## Context

Once ADR-0011 moved Game Context out, about half of `UserWorkflow`'s 712 lines were the Processing Run session:

- the run gate, `_runActivity` and `_runActivityLock`;
- input capture and the call to Processing Run Admission;
- writing a defaulted database path back;
- the hand-off to the executor;
- routing a failure to the right message;
- applying outcomes and progress.

ADR-0007 kept the gate in `UserWorkflow`, because a change about the locality of wording had no business moving
concurrency invariants into a module whose whole job is building strings. That argued against putting them in
Processing Run Presentation. It did not argue against giving them a module of their own. Leaving them where they were
had three costs.

**Two owners of one rule, and a defect between them.** "At most one active run" was enforced twice. `UserWorkflow`'s gate
was one owner. The executor's cancellation slot was the other, and its supersession logic cancelled an older run when a
newer one started. Neither could see the other's state.

An admitted press publishes activity, clears the message lists, and writes the defaulted path back before it calls
`ExecuteAsync`. Each of those notifications can re-enter the workflow. A second press re-entering through one of them
found the gate active and called `Cancel()`. The executor had no source yet, so `Cancel()` did nothing: the user read
"Cancelling...", and the run then completed without being cancelled.

The test for this case only counted `Cancel()` calls on a fake, so it passed. Because the gate stopped a second
`ExecuteAsync` from ever arriving, the executor's supersession logic could not be reached in production, yet it was
still tested.

**No interface of its own.** 34 of the 47 tests in `UserWorkflowTests` went through `ProcessFormIdsAsync`. Each one built
Game Context, a real Plugin List, and the presentation adapter just to put a Confirmed Plugin List in front of
admission. Several also restated Presentation's exact wording, so one wording change edited two test files.

**Failure routing split three ways.**

- The executor reported `ProcessingRunFailure` on the transient channel and then rethrew. `UserWorkflow`'s `finally`
  cleared that channel at once, so the status existed for a single notification.
- Which failure is shown unwrapped (ADR-0006) was decided by a catch clause in `UserWorkflow`.
- `"Initializing..."` and `"Cancelling..."` were literals in `UserWorkflow`, although ADR-0010 made Presentation the home
  of every sentence about a run.

## Decision

**A `ProcessingRunControl` module is the single owner of whether a Processing Run is active.**

- **What it owns.**
  - the run gate, its re-entrant lock, and the current run activity, including the report ordering behind issue #60;
  - calling Processing Run Admission with the captured inputs and the default database root;
  - creating the active run's cancellation source;
  - handing the request and its token to the executor;
  - turning progress, the outcome, a refusal, or a failure into a rendered report through
    `ProcessingRunPresentation`.
- **Inputs arrive captured.** `UserWorkflow` captures `ProcessingRunInputs` and passes them in. Processing Run Control
  reads no Game Context, Plugin List, or ViewModel. The existing rule only requires that capture happen before anything
  is published. It never needed the lock: every press arrives on the UI thread, and a re-entrant press only needs to
  find the gate active.
- **It reports to a sink and writes nothing.**
  - The sink receives run activity, messages, the clearing of the message lists, and a defaulted database path.
  - `UserWorkflow` implements the sink over the ViewModel as a private nested adapter. Tests implement it by recording,
    so the seam has two adapters.
  - The sink is called while the lock is held, which keeps the #60 ordering.
  - A press returns `Task`. The sink's ordered record is what a press is observed through.
- **Cancellation exists from admission.**
  - The cancellation source is created inside the lock on the admitted branch, before the first report reaches the
    sink. A press re-entering through that report finds both an active gate and a token it can cancel.
  - A press made before the run reaches the executor is a real cancel. The executor is handed an already-cancelled
    token, returns `CancelledRunOutcome`, and opens no Store. The defaulted path is still written back, because
    admission chose it.
  - A further press after cancellation was requested reports that cancellation was requested and does nothing else.
- **The executor becomes stateless.**
  - `ExecuteAsync` takes the request, the progress reporter, and a cancellation token.
  - It loses its cancellation slot, `Cancel()`, supersession, and `IDisposable`.
  - It keeps the offload to a worker thread, opening and disposing the Store (ADR-0001), and dispatch on the request.
  - It still returns `CancelledRunOutcome` only when the token it was given is cancelled.
  - ADR-0007's attribution stays precise: Processing Run Control creates every token, and it is the executor's only
    production caller.
- **Failure has one route.**
  - The `ProcessingRunFailure` progress case is deleted. A failure reaches the error messages only.
  - `ProcessingRunPresentation` gains three inputs: a typed "run starting" fact, a typed "cancellation requested" fact,
    and `RenderFailure(Exception)`. `RenderFailure` shows an `UnresolvableMasterException` unwrapped and puts the
    "Error processing FormIDs" prefix on anything else.
  - Processing Run Control contains no user-facing string literal. An architecture test closes the set of literals it
    may contain, as one already does for admission.
- **Lifetime.**
  - `UserWorkflow` builds Processing Run Control in its constructor from `IProcessingRunExecutor` and the default
    database root, the same way it builds Game Context. `UserWorkflow.Create` remains the only production composition.
  - Processing Run Control owns the executor. Its `Dispose` cancels the active run without waiting for it.
  - A press after disposal throws `ObjectDisposedException`. It used to be shown as "Error processing FormIDs: Cannot
    access a disposed object", which disguised a programming error as a run failure.
  - `UserWorkflow.Dispose` retires, in order: the Plugin List presentation adapter, Game Context, Processing Run Control,
    and the Plugin List.

This supersedes three parts of ADR-0007:

- that `UserWorkflow` keeps `_runActivity`, `_runActivityLock`, and the run gate;
- that the cancellation token is executor-owned;
- that `ProcessingRunFailure` is a progress case.

It also supersedes ADR-0010's placement of admission inside the User Workflow's run gate, and point 3 of ADR-0006,
which put the unwrapped rendering of an Unresolvable Master in `UserWorkflow`.

The rest of each stands:

- outcomes are values;
- failures are exceptions;
- Presentation is a pure renderer;
- admission is pure and runs before any run exists;
- every ViewModel write is performed by `UserWorkflow`, now through its sink adapter.

## Considered options

- **Merge the gate into the executor.** Rejected. The executor would take captured inputs and admit them itself, which
  mixes "may this press start a run" with "run this request". Its request-level tests would also end up below its
  interface.
- **Keep the gate in `UserWorkflow` and make the executor remember a cancel requested before `ExecuteAsync`.** Rejected.
  It fixes the defect by adding a third piece of state shared across the seam: a pending cancel whose lifetime belongs to
  someone else's gate. Run tests would still sit behind Game Context and the Plugin List.
- **Have Processing Run Control write the ViewModel directly, as `PluginListPresentationAdapter` does.** Rejected: that
  brings the ViewModel back into every test of the module.
- **Send typed facts to the sink and let `UserWorkflow` render them.** Rejected. A progress report whose rendering
  implies no new percentage carries the shown one forward, so the activity Processing Run Control holds has to be the
  rendered one. Rendering therefore happens where the activity is held.
- **Name it "Processing Run Session".** Rejected: a run already opens a FormID Record Store session, and one run should
  not involve two different kinds of session.

## Consequences

- A cancel pressed during startup cancels the run.
- `ProcessingRunControlTests` drive the module through `ProcessingRunInputs` records, with a fake executor that records
  calls and can be held at a gate, and a recording sink.
  - They take over the run-press tests from `UserWorkflowTests`.
  - The startup-window test asserts that the token handed to `ExecuteAsync` is already cancelled.
  - One test uses the real executor over fake Plugin Ingestion and a fake Store opener, and shows a press during the
    window returns `CancelledRunOutcome` and opens no Store.
  - They compare against `ProcessingRunPresentation` output rather than literal sentences. Wording is asserted only in
    `ProcessingRunPresentationTests`.
- `UserWorkflowTests` keep what remains `UserWorkflow`'s:
  - capturing inputs from Game Context, the Plugin List, and the ViewModel;
  - the sink adapter's ViewModel writes, including the defaulted-path guard and the write order;
  - a Plugin List refresh during a run;
  - disposal.
- `ProcessingRunExecutorTests` lose their supersession, `Cancel()`, and `Dispose` tests, and gain tests for a cancelled
  token supplied by the caller.
- Architecture tests that pin `UserWorkflow.cs` text are moved to `ProcessingRunControl.cs`, including the refusal
  sentences and the closed set of literals. The teardown-order test now lists four steps.
- `UserWorkflow` keeps composition, Game Context forwarding, the database and FormID-list pickers, Plugin selection,
  input capture, and the sink adapter.
- This ADR records the decision ahead of the change.
