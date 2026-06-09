## Why

Phase 3 left the WinUI shell rendering the main workflow but intentionally kept picker, dispatcher, and workflow-service wiring as placeholders. Phase 4 is needed now so the WinUI app can use native Windows platform services while preserving the staged migration boundary and the existing Avalonia app.

## What Changes

- Add a WinUI implementation of `IThreadDispatcher` backed by `DispatcherQueue`.
- Add a WinUI implementation of `IFileDialogService` backed by Windows App SDK picker APIs for game directories, database save paths, and optional FormID text files.
- Wire the WinUI main window to construct and own the same core services used by the Avalonia workflow, including game detection, plugin loading, and processing-service cancellation.
- Replace Phase 3 picker placeholder messages with real picker calls and ViewModel updates.
- Preserve picker exception reporting through the ViewModel and keep window-close cancellation/disposal behavior.
- Keep full processing parity, Avalonia removal, deployment identity, and final UX polish scoped to later migration phases unless they are required to support platform-service wiring.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `winui-migration-foundation`: extend the staged migration contract with Phase 4 WinUI platform-service requirements for dispatcher marshalling, Windows App SDK pickers, picker error reporting, service lifecycle ownership, and pre-parity verification.

## Impact

- Affected projects: `FormID Database Manager.WinUI`, `FormID Database Manager.Core`, and existing test projects where platform-service construction or ViewModel dispatch behavior needs coverage.
- Affected code: WinUI `MainWindow`, WinUI startup/lifecycle code, new WinUI service classes, and possibly small core abstractions only if required by WinUI picker constructor or lifecycle constraints.
- Dependencies/APIs: Windows App SDK `Microsoft.Windows.Storage.Pickers` and WinUI `DispatcherQueue`; fallback to `Windows.Storage.Pickers` plus `InitializeWithWindow` remains allowed only if the selected Windows App SDK package/runtime forces it.
- Verification: WinUI project build, full solution build, existing tests, and a packaged launch check that the main window still renders after service wiring.
