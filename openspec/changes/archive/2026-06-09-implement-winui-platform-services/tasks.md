## 1. WinUI Platform Services

- [x] 1.1 Add `WinUiThreadDispatcher` under `FormID Database Manager.WinUI/Services` using `DispatcherQueue.HasThreadAccess` and `DispatcherQueue.TryEnqueue`.
- [x] 1.2 Implement awaited dispatcher behavior so `InvokeAsync` completes on direct UI-thread calls, propagates queued callback exceptions, and fails deterministically if the dispatcher rejects queued work.
- [x] 1.3 Add `WinUiFileDialogService` under `FormID Database Manager.WinUI/Services` implementing `IFileDialogService` with `Microsoft.Windows.Storage.Pickers`.
- [x] 1.4 Configure the folder, save-file, and open-file pickers with `AppWindow.Id`, path-returning results, database `.db` choices, and FormID list `.txt` filtering.
- [x] 1.5 Preserve picker cancellation and exception semantics by returning `null` for cancellation and reporting picker exceptions through `MainWindowViewModel`.
- [x] 1.6 Add XML documentation to new service types and non-trivial methods, including comments for shutdown or dispatcher-rejection handling.

## 2. WinUI Main Window Wiring

- [x] 2.1 Update WinUI `MainWindow` construction so production startup creates the ViewModel with `WinUiThreadDispatcher` instead of the immediate dispatcher.
- [x] 2.2 Add WinUI main-window fields for the picker service, game detection/location services, plugin-list manager, and processing service.
- [x] 2.3 Update `Dispose` and close handling to cancel the owned processing service, dispose owned disposable services, unsubscribe close handlers, and dispose the ViewModel once.
- [x] 2.4 Wire the detected-directory selector in `MainWindow.xaml` to a WinUI selection handler without changing the Phase 3 layout beyond required event hooks.
- [x] 2.5 Port the Avalonia game-selection flow into WinUI: version stale-result guard, background installed-location lookup, directory state reset, detected-directory population, and plugin loading.
- [x] 2.6 Replace the WinUI browse-directory placeholder with a real folder picker call, game auto-detection when needed, ViewModel directory updates, and plugin loading.
- [x] 2.7 Replace the WinUI database and FormID list picker placeholders with real picker calls that update `DatabasePath` and `FormIdListPath` only when a path is returned.
- [x] 2.8 Use `PluginListManager` for Select All, Select None, and advanced-mode plugin reload behavior.
- [x] 2.9 Keep `ProcessFormIds_Click` scoped to the Phase 5 pending message while ensuring the owned processing service is available for close-time cancellation.

## 3. Verification And Documentation

- [x] 3.1 Build the WinUI project with `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64`.
- [x] 3.2 Build the full solution with `dotnet build "FormID Database Manager.slnx"`.
- [x] 3.3 Run the current automated test suite with `dotnet test "FormID Database Manager.Tests"` and report skipped/manual tests using the existing conventions.
- [x] 3.4 Search the WinUI project for accidental Avalonia or AXAML references introduced by Phase 4 service wiring.
- [x] 3.5 Perform packaged WinUI launch verification and record objective launch evidence or the exact environment blocker.
- [x] 3.6 Update `docs/WinUI-Migration-Plan.md` with a Phase 4 verification checkpoint and any deferred Phase 5 follow-up notes discovered during implementation.
