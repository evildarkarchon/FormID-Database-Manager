## 1. WinUI Shell Ownership

- [x] 1.1 Review the Avalonia `MainWindow.axaml` and `MainWindow.axaml.cs` controls/events against the Phase 3 spec so every primary workflow surface has a WinUI destination.
- [x] 1.2 Add `FormID Database Manager.WinUI/MainWindow.xaml` and `MainWindow.xaml.cs` as the single-window WinUI shell for the ported UI.
- [x] 1.3 Update `App.OnLaunched` to instantiate and activate `MainWindow` directly with the standard WinUI title bar.
- [x] 1.4 Remove the generated counter `MainPage` from the launch path, deleting the template page files if they are no longer referenced.

## 2. Resources and Binding Support

- [x] 2.1 Replace template sample resources in `App.xaml` with neutral, theme-aware resources used by the ported main-window UI.
- [x] 2.2 Add minimal WinUI binding helpers for visibility and message display, preferring core ViewModel properties such as `HasErrorMessages` and `HasInformationMessages` when collection-count binding would be brittle.
- [x] 2.3 Add or update focused ViewModel tests for any new core binding-support properties.
- [x] 2.4 Keep all new comments and XML documentation aligned with the repository comment policy when adding or substantially rewriting methods.

## 3. Main Window XAML Port

- [x] 3.1 Port game selection, game directory, database path, optional FormID list file, and plugin-filter controls to WinUI XAML.
- [x] 3.2 Port the plugin selection area to a virtualization-friendly WinUI `ListView` with checkbox item templates bound to `PluginListItem.IsSelected`.
- [x] 3.3 Port Select All, Select None, update mode, advanced mode, progress, and process-action controls.
- [x] 3.4 Replace red/green Avalonia message borders with WinUI-native message surfaces or compact styled message panels.
- [x] 3.5 Replace Avalonia-only layout and visibility semantics, including `DockPanel`, `IsVisible`, `ExperimentalAcrylicBorder`, and `Boolean_Converter`.

## 4. Code-Behind and Safe Interactions

- [x] 4.1 Construct and assign a `MainWindowViewModel` from the WinUI shell without duplicating core application state.
- [x] 4.2 Wire non-picker UI interactions that can already use core behavior, including Select All, Select None, plugin filtering through binding, and safe close-time disposal.
- [x] 4.3 Ensure picker-dependent buttons do not throw before Phase 4 services exist; either leave them disabled with a clear state or route clicks to a temporary information message.
- [x] 4.4 Ensure the process-action control cannot start an incomplete processing workflow accidentally before Phase 5 parity work is complete.
- [x] 4.5 Keep existing Avalonia application files and behavior untouched during the WinUI UI port.

## 5. Verification and Documentation

- [x] 5.1 Build `FormID Database Manager.WinUI/FormID Database Manager.WinUI.csproj` for x64.
- [x] 5.2 Build `FormID Database Manager.slnx` with both Avalonia and WinUI projects included.
- [x] 5.3 Run relevant ViewModel tests, or the full `FormID Database Manager.Tests` suite if core binding-support changes are made.
- [x] 5.4 Search the WinUI project for unintended Avalonia UI APIs and verify the generated counter UI is not on the launch path.
- [x] 5.5 Launch the packaged WinUI app through the local workflow and record objective evidence that the ported main-window UI renders, or document the blocking environment/deployment issue.
- [x] 5.6 Add Phase 3 verification notes to `docs/WinUI-Migration-Plan.md` and update this task list with completed items.
