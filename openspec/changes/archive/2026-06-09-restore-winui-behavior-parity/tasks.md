## 1. WinUI Processing Workflow

- [x] 1.1 Remove the WinUI processing-deferred constant and one-off information-message helper if they are no longer used after parity is restored.
- [x] 1.2 Add the WinUI async `ProcessFormIds_Click` event path with XML documentation and `RequiresUnreferencedCode` annotations matching the Mutagen reflection/trimming contract.
- [x] 1.3 Implement the active-run cancel branch so a second process click sets the progress status to cancelling and calls `PluginProcessingService.CancelProcessing`.
- [x] 1.4 Implement start-state updates so a valid run clears existing errors, sets `IsProcessing`, resets progress, and changes the process button content to the cancel action.
- [x] 1.5 Ensure the `finally` path always clears active processing state and restores the process button content after success, failure, or cancellation.

## 2. Processing Parameters And Validation

- [x] 2.1 Build `ProcessingParameters` from WinUI ViewModel state, selected plugins, update-mode checkbox state, selected game, game directory, database path, and optional FormID list path.
- [x] 2.2 Preserve selected-game validation and show the existing user-facing error when processing starts without a selected game.
- [x] 2.3 Preserve plugin-mode validation so a missing game directory or no selected plugins prevents processing when no FormID list file is selected.
- [x] 2.4 Preserve FormID-list mode so a selected game plus `FormIdListPath` can start processing without requiring a game directory or selected plugins.
- [x] 2.5 Generate a default database path with `Directory.GetCurrentDirectory()` and `GameReleaseHelper.GetSafeTableName(selectedGame)` when `DatabasePath` is empty, and write it back to the ViewModel before processing starts.
- [x] 2.6 Wire progress reporting so `PluginProcessingService` updates call `MainWindowViewModel.UpdateProgress`.
- [x] 2.7 Preserve processing error and cancellation handling, including the existing FormID processing error-message prefix.

## 3. Workflow Parity Checks

- [x] 3.1 Verify the WinUI game selector exposes all supported `MainWindowViewModel.AvailableGames` values.
- [x] 3.2 Verify game-selection lookup still runs off the UI thread and stale lookup results cannot update the current WinUI state.
- [x] 3.3 Verify Browse can set `GameDirectory`, auto-detect the game when no game is selected, and load plugins without duplicate installed-location lookup.
- [x] 3.4 Verify multiple detected directories remain selectable and changing the selected directory reloads plugins for the current game.
- [x] 3.5 Verify plugin live filtering, individual checkbox selection, Select All, and Select None preserve the selection state consumed by processing.
- [x] 3.6 Verify advanced mode reloads plugins with base game and DLC visibility matching the checkbox state.
- [x] 3.7 Verify database and FormID list pickers update ViewModel paths on selection and leave existing state unchanged on cancellation.
- [x] 3.8 Verify error and information messages remain capped at the existing maximum through the shared ViewModel.

## 4. Automated Guardrails

- [x] 4.1 Extend WinUI source or architecture tests to assert the deferred processing placeholder is gone and the WinUI handler calls `PluginProcessingService.ProcessPlugins`.
- [x] 4.2 Add guardrail coverage for WinUI cancellation wiring, default database path generation, and safe table-name usage.
- [x] 4.3 Add or confirm ViewModel coverage for supported game values, selected-plugin snapshots, progress visibility, and error/information message caps.
- [x] 4.4 Add focused tests for any extracted validation or parameter-building helper introduced during implementation.
- [x] 4.5 Run targeted tests for the new or changed guardrails before the full verification pass.

## 5. Verification And Documentation

- [x] 5.1 Build the WinUI project with `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64`.
- [x] 5.2 Build the full solution with `dotnet build "FormID Database Manager.slnx"`.
- [x] 5.3 Run the current automated test suite with `dotnet test "FormID Database Manager.Tests"` and report skipped/manual tests using the existing conventions.
- [x] 5.4 Search the WinUI project for accidental Avalonia or AXAML references introduced during Phase 5.
- [x] 5.5 Perform packaged WinUI launch verification and record objective launch evidence or the exact environment blocker.
- [x] 5.6 Manually verify at least one real or representative game-directory workflow covering processing start, progress, cancellation, and default database path generation.
- [x] 5.7 Update `docs/WinUI-Migration-Plan.md` with a Phase 5 verification checkpoint and any follow-up notes for later phases.
