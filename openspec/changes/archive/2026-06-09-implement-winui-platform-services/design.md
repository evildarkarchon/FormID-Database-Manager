## Context

The staged WinUI migration has completed the UI-neutral core extraction, WinUI shell scaffold, and Phase 3 main-window surface. `FormID Database Manager.WinUI` now binds to the core `MainWindowViewModel`, but picker-dependent buttons and service-dependent workflows still show placeholder messages. The core already exposes `IThreadDispatcher` and `IFileDialogService`, and the existing Avalonia app remains the working reference for dispatcher, picker, game-location, plugin-list, and processing-service lifecycle behavior.

Microsoft Learn documents Windows App SDK pickers in `Microsoft.Windows.Storage.Pickers` as available in Windows App SDK 1.8 and later, path-returning, and constructed with the parent `AppWindow.Id`. It also documents WinUI dispatcher marshalling through `Microsoft.UI.Dispatching.DispatcherQueue`, `HasThreadAccess`, and `TryEnqueue`.

## Goals / Non-Goals

**Goals:**

- Provide WinUI-native implementations for the existing core dispatcher and file-dialog abstractions.
- Wire the WinUI main window to use real platform services for game directory detection, plugin list loading, file picker results, and service lifecycle ownership.
- Preserve the current staged migration model by keeping the existing Avalonia app buildable and leaving unsupported WinUI workflow portions clearly deferred.
- Preserve ViewModel-based error and information reporting for picker failures, unexpected event-handler exceptions, scanning progress, and plugin-load messages.
- Verify that the WinUI project still builds and launches after platform-service wiring.

**Non-Goals:**

- Do not remove Avalonia dependencies or files; that remains Phase 8.
- Do not finalize full processing parity or release publishing behavior; those remain Phases 5 and 9.
- Do not introduce a dependency-injection container solely for Phase 4 service construction.
- Do not redesign the Phase 3 WinUI layout beyond small event-hook additions needed for service wiring.

## Decisions

### Use a `DispatcherQueue` adapter for `IThreadDispatcher`

`WinUiThreadDispatcher` will capture a `DispatcherQueue` from the WinUI window and implement `CheckAccess` with `HasThreadAccess`. `Post` will enqueue work with `TryEnqueue`, while `InvokeAsync` will complete synchronously on the UI thread and otherwise bridge `TryEnqueue` to a `TaskCompletionSource` so callers can await completion and receive exceptions.

Alternative considered: use `DispatcherQueue.GetForCurrentThread()` inside the dispatcher. Capturing the window's queue is safer because the service graph is built by the window that owns the UI-bound ViewModel state.

Alternative considered: keep `ImmediateThreadDispatcher` for WinUI. That would compile, but background plugin-list refresh and debounce callbacks could mutate UI-bound collections off-thread.

### Use Windows App SDK picker APIs first

`WinUiFileDialogService` will use `Microsoft.Windows.Storage.Pickers.FolderPicker`, `FileSavePicker`, and `FileOpenPicker`, each constructed with the WinUI `AppWindow.Id`. The service will return `result.Path` for successful picks and `null` for cancellation. Picker exceptions will be caught and reported through the existing ViewModel messages, matching the Avalonia `WindowManager` behavior.

Alternative considered: use `Windows.Storage.Pickers` plus `InitializeWithWindow`. That path should remain only as a fallback if the selected Windows App SDK package/runtime or future elevation requirements make the Windows App SDK pickers unavailable.

### Keep service ownership explicit in `MainWindow`

The WinUI `MainWindow` will construct the platform dispatcher, ViewModel, picker service, game detection/location services, plugin-list manager, and processing service directly, mirroring the current Avalonia app's manual wiring. The window will retain the existing internal constructor pattern for tests where practical, but production construction should use the WinUI dispatcher and `AppWindow.Id`.

Alternative considered: introduce a shared composition root or dependency-injection container. That would be useful later if service construction grows, but Phase 4 needs a small and observable migration step.

### Restore platform-dependent event handlers without claiming full parity

Phase 4 will replace picker placeholders and wire game-selection/plugin-list workflows that require dispatcher and platform-service behavior. `ProcessFormIds_Click` may continue to report that processing parity is deferred until Phase 5, but the WinUI window will still own `PluginProcessingService` so window-close cancellation/disposal behavior is in place.

Alternative considered: port the full Avalonia processing event handler in Phase 4. That risks merging Phase 4 and Phase 5, making verification harder and weakening the migration plan's checkpoints.

## Risks / Trade-offs

- Windows App SDK picker API surface differs from Avalonia picker options -> Verify exact C# property names during implementation and keep the fallback path documented if package restore exposes an older API.
- `DispatcherQueue.TryEnqueue` can return false during shutdown -> Treat shutdown races as lifecycle events, and make `InvokeAsync` fail deterministically rather than silently hanging.
- Manual service wiring duplicates the Avalonia constructor shape -> Accept the duplication during migration; remove or consolidate after WinUI parity is stable.
- Game-directory lookup and plugin loading can finish after a newer selection -> Preserve version-token checks from the Avalonia path so stale results are ignored.
- UI smoke verification for packaged WinUI can be environment-sensitive -> Record objective launch evidence when possible and document any environment blocker with exact command output.

## Migration Plan

1. Add WinUI service classes under `FormID Database Manager.WinUI/Services`.
2. Update `MainWindow` construction to use `WinUiThreadDispatcher`, `WinUiFileDialogService`, and the core service graph.
3. Replace picker and game/plugin placeholder handlers with async service-backed handlers based on the Avalonia reference flow.
4. Keep processing start deferred to Phase 5 while ensuring close-time cancellation/disposal calls into the owned processing service.
5. Build the WinUI project, build the solution, run the current test suite, and perform packaged launch verification.
6. Record Phase 4 verification evidence in `docs/WinUI-Migration-Plan.md`.
