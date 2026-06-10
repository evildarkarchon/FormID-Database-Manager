## Why

Phase 2 proved the packaged WinUI shell can build and launch, but it still shows the template counter page. Phase 3 should replace that blank shell with the application's main workflow surface so later phases can wire platform services and behavior parity against a real WinUI UI.

## What Changes

- Port the existing `MainWindow.axaml` layout into WinUI XAML in the staged `FormID Database Manager.WinUI` project.
- Replace the template `MainPage` counter UI with the FormID Database Manager controls for game selection, directory/database/list-file paths, plugin filtering, plugin selection, mode toggles, messages, progress, and processing actions.
- Bind the WinUI UI to `MainWindowViewModel` and `PluginListItem` from `FormID Database Manager.Core`.
- Add theme-aware WinUI resources for the initial UI port, using standard WinUI controls and a standard title bar first.
- Use a virtualization-friendly WinUI plugin list with checkbox item templates bound to `PluginListItem.IsSelected`.
- Replace Avalonia-only layout, visibility, converter, acrylic, and control APIs with WinUI equivalents.
- Keep the port behavior-first; defer file picker implementations, WinUI dispatcher implementation, custom title bar/backdrop polish, Avalonia removal, and full workflow parity to later migration phases.

## Capabilities

### New Capabilities

### Modified Capabilities
- `winui-migration-foundation`: Adds Phase 3 requirements for a real WinUI main-window UI surface that replaces the template page while preserving the staged migration boundary.

## Impact

- Affected projects: `FormID Database Manager.WinUI`, `FormID Database Manager.Core`, and potentially small ViewModel binding-support changes required by WinUI.
- Affected UI files: WinUI `App.xaml`, `App.xaml.cs`, `Views/MainPage.xaml`, `Views/MainPage.xaml.cs`, and any new WinUI resource/style files created for the port.
- Affected workflows: WinUI shell build, packaged local launch, and manual inspection of the main-window UI.
- Existing Avalonia application files, Avalonia tests, Mutagen processing, SQLite writing, file picker behavior, and deployment settings should remain intact in this phase.
