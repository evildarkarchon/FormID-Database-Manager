## Why

Phase 4 wired the WinUI shell to real platform services, but the primary processing action still reports that behavior parity is deferred. Phase 5 is needed now so the staged WinUI app can execute the same end-to-end workflows as the Avalonia app before later binding polish, test rework, Avalonia removal, and deployment work begin.

## What Changes

- Restore the WinUI processing workflow so the process button validates inputs, starts processing, reports progress, supports cancellation, and resets UI state consistently with the existing application.
- Preserve default database path generation through `GameReleaseHelper.GetSafeTableName`.
- Verify all Phase 5 workflow parity items from `docs/WinUI-Migration-Plan.md`, including game selection, stale lookup handling, directory browsing, detected-directory selection, plugin filtering/selection, advanced-mode reloads, file pickers, progress, cancellation, and message caps.
- Keep the staged migration boundary: do not remove Avalonia, do not redesign the WinUI UI beyond parity-required adjustments, and do not take on Phase 6 binding polish or Phase 7 test conversion except where needed to verify behavior.
- Record Phase 5 verification evidence in the migration plan.

## Capabilities

### New Capabilities

### Modified Capabilities
- `winui-migration-foundation`: Adds Phase 5 requirements for end-to-end WinUI workflow parity, processing start/cancel behavior, default database path generation, progress reporting, message limits, and migration-plan verification.

## Impact

- Affected projects: `FormID Database Manager.WinUI`, `FormID Database Manager.Core`, and existing test projects where behavior-parity coverage is practical without full WinUI UI automation.
- Affected code: WinUI `MainWindow` process/start/cancel event handling, ViewModel state transitions, progress callbacks, validation paths, and any small binding or helper adjustments required for parity.
- Affected workflows: packaged WinUI launch/manual workflow verification, WinUI project build, full solution build, existing automated tests, and targeted tests for parity-sensitive logic.
- Out of scope: Avalonia dependency removal, broad UI polish, deployment identity/signing, release publishing, and wholesale test-suite migration.
