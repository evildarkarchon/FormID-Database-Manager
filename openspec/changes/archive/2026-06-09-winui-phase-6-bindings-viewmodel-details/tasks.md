## 1. Binding and State Audit

- [x] 1.1 Audit `FormID Database Manager.WinUI/MainWindow.xaml` for `Binding` versus `x:Bind` usage across template data, observable collections, and dynamic ViewModel properties.
- [x] 1.2 Audit `FormID Database Manager.WinUI/MainWindow.xaml.cs` process button content handling and decide whether to keep the narrow event-handler update or move button text to ViewModel state.
- [x] 1.3 Audit `PluginListItem` validation usage in the WinUI plugin list and decide whether validation is user-visible or model-level only for Phase 6.

## 2. Focused Test Coverage

- [x] 2.1 Add or update source-level WinUI tests that lock down binding-critical main-window XAML wiring for workflow state, message `InfoBar` visibility, plugin list item bindings, and process button content.
- [x] 2.2 Add or update ViewModel tests proving `HasErrorMessages` and `HasInformationMessages` change notifications fire when message collections are added to, cleared, or replaced.
- [x] 2.3 Add tests for the chosen `PluginListItem` validation contract, including whitespace names and valid selectable plugin items.
- [x] 2.4 Add tests proving debounced plugin filtering posts UI-bound collection updates through `IThreadDispatcher` when dispatcher access is unavailable.
- [x] 2.5 Add tests proving filtered plugin items preserve underlying `PluginListItem` instances and `IsSelected` state across hide/show filter changes.

## 3. Implementation

- [x] 3.1 Update ViewModel message visibility notification code if needed so collection add, remove, clear, and replacement all keep WinUI `InfoBar.IsOpen` bindings current.
- [x] 3.2 Update process button state handling if needed so start, cancellation, completion, failure, and cancellation reset states consistently present the correct action.
- [x] 3.3 Update WinUI plugin item validation handling or documentation according to the Phase 6 validation decision.
- [x] 3.4 Update debounced filtering dispatcher handling if tests reveal any path can mutate `FilteredPlugins` without dispatcher access.
- [x] 3.5 Keep workflow state bound to the shared core ViewModel and avoid adding WinUI-only duplicate state unless it is strictly local to the window.

## 4. Verification and Documentation

- [x] 4.1 Run focused Phase 6 tests for WinUI source wiring, ViewModel message visibility, plugin item validation, process button state, and debounced filtering.
- [x] 4.2 Run `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64`.
- [x] 4.3 Run `dotnet build "FormID Database Manager.slnx"`.
- [x] 4.4 Run `dotnet test "FormID Database Manager.Tests"` or record any environment-specific blocker.
- [x] 4.5 Manually verify the packaged WinUI shell for message bars, plugin filtering, plugin selection preservation, and process button start/cancel/reset transitions.
- [x] 4.6 Add a Phase 6 verification checkpoint to `docs/WinUI-Migration-Plan.md` with build, test, launch, and workflow evidence.
