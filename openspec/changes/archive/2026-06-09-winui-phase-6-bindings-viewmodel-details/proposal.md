## Why

Phase 5 restored WinUI behavior parity, so the next risk is quiet UI drift from binding differences between Avalonia and WinUI. Phase 6 tightens the WinUI binding and ViewModel details that determine whether message visibility, validation feedback, processing state, and debounced filtering remain reliable as the migration moves toward test rework and Avalonia removal.

## What Changes

- Audit the WinUI main-window bindings and prefer standard `Binding` for template or dynamic ViewModel state while using `x:Bind` only where strongly typed bindings are straightforward and stable.
- Ensure error and information message visibility is driven by explicit ViewModel boolean properties that raise change notifications when message collections change.
- Audit `PluginListItem` validation behavior under WinUI and add explicit UI or ViewModel support if `IDataErrorInfo` does not surface the expected validation state.
- Make the process button state reliable by binding or narrowly handling its text/content from processing state.
- Verify the existing plugin filtering debounce continues to run through the WinUI dispatcher and does not mutate UI-bound collections from background threads.
- Record automated and manual verification for the Phase 6 binding-critical workflows before Phase 7 test rework begins.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `winui-migration-foundation`: extend the staged WinUI migration contract with Phase 6 requirements for binding semantics, message visibility notifications, validation behavior, process-button state, dispatcher-backed filtering, and verification.

## Impact

- Affected code: `FormID Database Manager.WinUI/MainWindow.xaml`, `FormID Database Manager.WinUI/MainWindow.xaml.cs`, `FormID Database Manager.Core/ViewModels/MainWindowViewModel.cs`, `FormID Database Manager.Core/Models/PluginListItem.cs`, and focused tests under `FormID Database Manager.Tests`.
- Affected behavior: WinUI message visibility, validation feedback, process button content, plugin filtering responsiveness, and UI-thread safety for binding-critical updates.
- Dependencies: no new runtime dependencies are expected; this change should continue using WinUI, CommunityToolkit.Mvvm, and the existing core abstractions.
