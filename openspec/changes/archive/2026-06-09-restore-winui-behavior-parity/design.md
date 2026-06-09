## Context

The staged WinUI migration has completed core extraction, shell scaffolding, main-window UI porting, and Phase 4 platform-service wiring. The WinUI window now owns `PluginProcessingService`, `PluginListManager`, WinUI pickers, and a WinUI dispatcher, but `ProcessFormIds_Click` still shows a deferred-work information message instead of invoking the processing workflow. The Avalonia `MainWindow.axaml.cs` remains the behavior reference for process/start/cancel state, parameter validation, default database-path generation, and progress reporting.

Phase 5 should make the WinUI shell behaviorally useful without crossing into later migration phases. Avalonia must stay buildable, the current core processing services should remain the source of Mutagen/SQLite behavior, and UI adjustments should be limited to what parity requires.

## Goals / Non-Goals

**Goals:**

- Port the primary processing workflow into `FormID Database Manager.WinUI/MainWindow.xaml.cs`.
- Preserve the Avalonia validation contract: a selected game is required, plugin processing requires a game directory and at least one selected plugin, and FormID-list processing can run without selected plugins.
- Preserve default database-path generation with `Path.Combine(Directory.GetCurrentDirectory(), $"{GameReleaseHelper.GetSafeTableName(gameRelease)}.db")`.
- Route progress through `MainWindowViewModel.UpdateProgress` so dispatcher-backed UI updates remain thread-safe regardless of where `Progress<T>` callbacks execute.
- Verify the Phase 5 workflow checklist from `docs/WinUI-Migration-Plan.md` and record the checkpoint there.

**Non-Goals:**

- Do not remove Avalonia packages, AXAML files, Avalonia tests, or the existing Avalonia app project.
- Do not redesign the WinUI layout beyond small control/content state updates required by process/cancel parity.
- Do not take on Phase 6 binding polish, Phase 7 test-suite conversion, Phase 8 Avalonia removal, or Phase 9 deployment identity work.
- Do not introduce a dependency-injection container solely for this phase.

## Decisions

### Port the Avalonia processing handler into the WinUI window

The WinUI window already mirrors the Avalonia manual service wiring and owns `PluginProcessingService`, so Phase 5 should replace the placeholder `ProcessFormIds_Click` with an async handler based on the Avalonia implementation. The handler will keep the existing start/cancel toggle behavior, clear error messages at run start, build `ProcessingParameters` from the ViewModel and WinUI checkboxes, call `ProcessPlugins`, and reset button/state in `finally`.

Alternative considered: move the whole workflow into a new shared coordinator before porting. That could reduce duplication later, but it increases the migration blast radius while Avalonia still needs to remain intact. A coordinator can be revisited after WinUI parity is stable.

### Keep validation in the UI workflow for now

The validation rules are currently part of the window workflow rather than `PluginProcessingService`. Phase 5 will keep those rules at the WinUI window boundary so text-file mode, plugin mode, selected-game validation, default database path mutation, and user-facing error messages match the existing application.

Alternative considered: push validation into core. That would be a useful cleanup if multiple UI shells stay active long term, but this phase is about WinUI parity rather than changing service contracts.

### Use the ViewModel dispatcher as the progress safety net

The progress reporter should call `ViewModel.UpdateProgress(update.Message, update.Value)`. `UpdateProgress` already posts through `IThreadDispatcher` when called off the UI thread, which makes the WinUI path robust even if `Progress<T>` does not marshal through the same synchronization context as Avalonia.

Alternative considered: require every progress callback to run on the WinUI UI thread before touching the ViewModel. That couples progress behavior to WinUI synchronization details and duplicates protection already present in the shared ViewModel.

### Treat automated tests as focused guardrails plus manual WinUI verification

Existing core service and ViewModel tests already cover most processing and message-limit behavior. Phase 5 should add targeted tests where practical, including source/architecture checks for the WinUI processing handler and any newly factored validation/default-path helpers. End-to-end picker and packaged launch behavior should remain a manual or smoke verification checkpoint until Phase 7 decides the long-term WinUI UI automation strategy.

Alternative considered: replace Avalonia headless UI tests with WinUI automation now. That belongs to Phase 7 and would distract from restoring workflow parity.

## Risks / Trade-offs

- WinUI process-button content may drift from processing state if an exception exits early -> Keep the `finally` reset path and include verification for both successful and failed starts.
- Cancellation during Mutagen overlay creation can remain delayed because overlay creation is synchronous -> Preserve the current service behavior and ensure cancellation requests still call `PluginProcessingService.CancelProcessing`.
- Validation duplicated between Avalonia and WinUI can diverge -> Port directly from the Avalonia reference and add focused checks for the WinUI source/helper behavior.
- Manual WinUI verification depends on local packaged launch state -> Record exact launch commands, objective evidence, or concrete blockers in the migration plan.

## Migration Plan

1. Remove the WinUI deferred-processing message constant and one-off message helper if no longer needed.
2. Add an async WinUI `ProcessFormIds_Click` path with `RequiresUnreferencedCode` annotations matching the Avalonia processing path.
3. Build and validate `ProcessingParameters` from WinUI state, including text-file mode, update mode, selected plugins, selected game, and default database path.
4. Wire progress reporting and cancellation through the existing `PluginProcessingService` and `MainWindowViewModel`.
5. Add focused automated coverage for parity-sensitive WinUI source/helper behavior and any ViewModel/default-path behavior that lacks coverage.
6. Build the WinUI project, build the solution, run the test suite, search the WinUI project for accidental Avalonia references, and perform packaged launch/manual workflow verification.
7. Record the Phase 5 verification checkpoint in `docs/WinUI-Migration-Plan.md`.
