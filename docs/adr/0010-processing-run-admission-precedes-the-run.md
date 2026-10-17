# ADR-0010: Processing Run Admission happens before a run, and the request carries its Plugin List Source

- Status: Accepted
- Date: 2026-10-17

## Context

Deciding whether a button press may start a Processing Run, and which request it starts, was spread across four
places:

- `UserWorkflow.CreateProcessingRunRequest` checked that a GameRelease was selected, matched the captured Confirmed
  Plugin List against the captured Game Context's Plugin List Source, defaulted the database path, and chose between a
  FormID text run and a selected-Plugin run.
- The request constructors in `ProcessingRunContracts.cs` held the remaining user-facing checks: database path, game
  directory, and Plugin selection, thrown as `ProcessingRunValidationException`.
- `DefaultDatabasePathProvider` computed the default path and created its directory as a side effect.
- `PluginIngestion` canonicalized the request's directory again, after `PluginListSource` had already canonicalized it
  twice on the way there.

That split caused two problems a user or developer could see, and left a question open.

**A wrong message.** When the captured confirmation did not match the captured source, for example while a refresh for
a newly selected directory was in flight, the workflow deliberately built a request with an empty selection so that
its constructor would throw. The user read "No plugins selected" after selecting one. The workflow only kept the
confirmation, so it had no way to tell "still loading", "failed to load", and "selected nothing" apart.

**A side effect before validation.** Every real run without a chosen path created
`%LOCALAPPDATA%\FormID Database Manager\Databases` before any validation ran, including runs that then failed
validation. `UserWorkflow` had no seam for it, so unit tests created that directory on the developer's machine.

**An open question.** ADR-0002 deliberately left "carrying canonical paths in the selected-Plugin request" for a
separate decision. The request carried a raw directory string, and nothing in its type said whether it was canonical.

## Decision

**Processing Run Admission is one pure module that runs before a Processing Run exists.**

`ProcessingRunAdmission.Admit` takes the inputs captured when the user pressed the button and the default database
root. The inputs are the GameRelease, the game directory, the whole `PluginListState` snapshot, the FormID text path,
the database path, Update Mode, and Dry Run. Admit returns either an admitted run carrying the request, or a refusal
carrying a closed `RunRejectionReason`. It reads nothing from disk, creates nothing, and writes no ViewModel state.

- **Refusals are values.** They are rendered into words by `ProcessingRunPresentation`, which is now the only home of
  run wording, including the sentence that used to live in `UserWorkflow`. A refused run never started, so a refusal
  is neither a Processing Run Outcome nor a failure, and ADR-0007's rule that failures stay exceptions is unaffected.
- **The reasons and their order.** A FormID text path chooses a text run before any Plugin check applies.

  1. No GameRelease is selected.
  2. A Plugin run has no game directory.
  3. The Plugin List is still loading: it is refreshing, or its source differs from the captured Game Context.
  4. The Plugin List is unavailable: no source, or a failed, cancelled, or faulted refresh with no matching
     confirmation.
  5. A matching confirmation selects no Plugins.
  6. A real run has no database path and no default root.

  A matching Confirmed Plugin List admits the run whatever the Plugin List's state, including during a same-source
  refresh, as the glossary requires.
- **Admission runs inside the User Workflow's run gate,** before any run activity is published. A refused press
  therefore never becomes an active run, and never flashes "Initializing...". The refusal message, and writing a
  defaulted database path back to the ViewModel, both happen after the gate in `UserWorkflow`, which keeps every
  ViewModel write (ADR-0007).
- **The default root is configuration.** `UserWorkflow.Create` resolves the local application data directory once and
  passes it in. Admission only computes the path. `DefaultDatabasePathProvider` is folded into admission, because
  without its directory side effect it is one path computation with one caller.
- **The Store creates the database's parent directory** as part of ready-on-return opening (ADR-0001, amended). A Dry
  Run and a refused run therefore create nothing.
- **The selected-Plugin request carries the matched `PluginListSource`,** which settles the question ADR-0002 left
  open. The source is the type-level proof that its Data directory is canonical, and the request's GameRelease comes
  from it. Plugin Ingestion reads `Source.DataDirectory` and no longer canonicalizes. The request constructor becomes
  `internal`; no project outside Core constructs requests.
- **`ProcessingRunValidationException` is deleted.** The request constructors keep their invariants, but as
  `ArgumentException`: once admission guarantees a valid request, a violation is a programming error rather than
  something to show the user.

## Considered options

- **Keep throwing `ProcessingRunValidationException`, with admission as the place the throws live.** Rejected: the
  wording would stay outside `ProcessingRunPresentation`, and catching an exception to learn that a run was refused is
  the kind of reconstruction ADR-0007 removed for outcomes.
- **A static factory on `ProcessingRunRequest` instead of a separate module.** Rejected: it would grow an already large
  contracts file, and the many tests that construct requests directly from plain values would have to go through
  admission.
- **Capture only the Confirmed Plugin List and fix just the mismatched wording.** Rejected: without the state, admission
  cannot tell loading from failed, and the snapshot is already immutable and coherent under ADR-0009.
- **Admission creates the default directory under an injected root, or the executor creates it before opening the
  Store.** Rejected: the first keeps admission impure; the second splits Store setup across two modules. The Store
  already owns setup, and creating the directory there also covers a path the user picked whose folder was later
  deleted.
- **Keep a raw directory string in the request, renamed or documented as canonical.** Rejected: nothing would enforce
  it, and canonicalizing a second time in Plugin Ingestion would keep the duplication this decision removes.
- **Give admission a smaller Plugin List projection computed by `UserWorkflow`,** so its tests could use plain values.
  Rejected: the mapping from Plugin List state to refusal reason would move back into the workflow. ADR-0009 is kept:
  admission tests for the Plugin List reasons drive a real Plugin List into each state through a shared test helper.

## Consequences

- A refused run tells the user why, including when the Plugin List is still loading or failed to load.
  `ProcessFormIdsAsync_DuringDirectoryProjection_RejectsMismatchedConfirmation` changes from "No plugins selected" to
  the loading message.
- The workflow tests for each reason, the check order, and default-path computation move to admission tests.
  `DefaultDatabasePathProviderTests` moves with them. `UserWorkflowTests` keep only the wiring: where a refusal is
  shown, the guarded write-back, and that `Create` passes the root through. No unit test creates a directory under the
  developer's profile.
- Plugin Ingestion tests that pass a game root and assert canonicalization move to `PluginListSource` and admission
  tests. `CoreProjectBoundaryTests`, which pins `GameInstallations.CanonicalizeDataDirectory` in `PluginIngestion.cs`,
  is rewritten so that canonicalization reaches a run only through `PluginListSource`.
- `WinUiPlatformServiceSourceTests`' ban on the shell calling `DefaultDatabasePathProvider` is retargeted or removed
  along with the provider.
- This ADR records the decision ahead of the change. The planned order is: the Store creates the parent directory;
  then admission and the `UserWorkflow` cutover; then the request carrying `PluginListSource`.

> Note (2026-10-17): The change has landed in that order, so the bullet above is historical. Two follow-ups went with
> it: the generic "Error processing FormIDs" sentence for an unforeseen failure moved into `ProcessingRunPresentation`
> as well, and `PluginProcessingRunRequest` became an internal type rather than a public type with an internal
> constructor.
