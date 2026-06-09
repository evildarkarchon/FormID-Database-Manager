# WinUI Migration Plan

Last updated: 2026-06-09

## Goal

Migrate FormID Database Manager from Avalonia 12 to WinUI, using the Windows App SDK, while preserving the current Mutagen and SQLite processing behavior. The app is intended to be Windows-only, so the migration should optimize for native Windows UX, Windows deployment, and maintainable C#/.NET code rather than cross-platform compatibility.

Microsoft documentation now recommends the term "WinUI app"; the same docs still use "WinUI 3" in some URLs and historical pages. In this plan, "WinUI" means the Windows App SDK desktop UI framework previously referred to as WinUI 3.

## Official References

- [WinUI overview](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [Windows App Development FAQ](https://learn.microsoft.com/windows/apps/get-started/windows-developer-faq#native-windows-only-app-development)
- [Quick start: set up your environment and create a WinUI project](https://learn.microsoft.com/windows/apps/get-started/start-here)
- [Publish your first Windows app](https://learn.microsoft.com/windows/apps/package-and-deploy/publish-first-app)
- [Open files and folders with pickers in WinUI](https://learn.microsoft.com/windows/apps/develop/files/using-file-folder-pickers)
- [Save a file with Windows App SDK pickers in WinUI](https://learn.microsoft.com/windows/apps/develop/files/pickers-save-file)

Key points from the current docs:

- WinUI ships as part of the Windows App SDK and targets Windows 10 version 1809 or later, including Windows 11.
- Visual Studio with the WinUI application development workload and Developer Mode enabled is the supported primary development path.
- Packaged MSIX is the default WinUI template path. Unpackaged WinUI apps are supported, but they need an explicit runtime/deployment strategy.
- Windows App SDK 1.8 adds `Microsoft.Windows.Storage.Pickers` APIs for WinUI file and folder picking. These return path-based results and avoid the older HWND initialization ceremony used by `Windows.Storage.Pickers`.

## Current Codebase Inventory

Avalonia-specific surface area is relatively concentrated:

- `FormID Database Manager/FormID Database Manager.csproj` references Avalonia packages and has `AvaloniaUseCompiledBindingsByDefault`.
- `FormID Database Manager/App.axaml` and `App.axaml.cs` define app resources, startup, and developer tools.
- `FormID Database Manager/Program.cs` starts Avalonia with a classic desktop lifetime.
- `FormID Database Manager/MainWindow.axaml` and `MainWindow.axaml.cs` define the full UI and most UI event orchestration.
- `FormID Database Manager/Boolean_Converter.cs` implements Avalonia `IValueConverter`.
- `FormID Database Manager/Services/AvaloniaThreadDispatcher.cs` contains the reusable `IThreadDispatcher` interface and an Avalonia-specific implementation.
- `FormID Database Manager/Services/WindowManager.cs` wraps Avalonia `IStorageProvider` file and folder pickers.
- `FormID Database Manager.Tests/UI/`, `TestInitialization.cs`, and several ViewModel tests depend on Avalonia headless test infrastructure.

Reusable application logic:

- `Services/DatabaseService.cs`, `ModProcessor.cs`, `FormIdTextProcessor.cs`, `PluginProcessingService.cs`, `PluginListManager.cs`, `GameDetectionService.cs`, `GameLocationService.cs`, and load-order services should largely survive the migration.
- `Models/PluginListItem.cs`, `Models/ProcessingParameters.cs`, and `ViewModels/MainWindowViewModel.cs` are already CommunityToolkit.Mvvm-based and can remain the main state model after removing Avalonia defaults.
- The test utilities already include `SynchronousThreadDispatcher`, which is useful for UI-independent ViewModel and service tests.

## Recommended Target Shape

Use a template-first WinUI migration rather than manually mutating the Avalonia project file into a Windows App SDK project.

Recommended solution structure:

```text
FormID Database Manager.Core/
  Models/
  ViewModels/
  Services/
  Platform abstractions such as IThreadDispatcher and IFileDialogService

FormID Database Manager.WinUI/ or the final FormID Database Manager project
  App.xaml / App.xaml.cs
  MainWindow.xaml / MainWindow.xaml.cs
  Services/WinUiThreadDispatcher.cs
  Services/WinUiFileDialogService.cs
  Styles/
  Assets/

FormID Database Manager.Tests/
  Unit and integration tests for Core
  Thin WinUI smoke/integration tests where practical
```

The safest implementation sequence is to create a new staging WinUI project alongside the current app, migrate behavior until parity is reached, then swap or rename the project once Avalonia dependencies are gone.

## Open Decisions Before Implementation

1. Packaging model
   - Recommended default: packaged MSIX, because it is the standard WinUI path and supports Store/AppInstaller-style deployment.
   - Alternative: unpackaged self-contained app if preserving the current direct `.exe` or single-file publish workflow is a hard requirement. This requires explicit Windows App SDK runtime handling and verification.

2. Project naming and cutover
   - Staging option: create `FormID Database Manager.WinUI`, keep the Avalonia app compiling during migration, then replace the old app project at cutover.
   - Direct option: convert the existing app project after a branch/checkpoint. This is faster but harder to bisect.

3. Test automation depth
   - Keep most behavior covered by unit/integration tests against the UI-neutral core.
   - Add WinUI launch smoke tests and manual verification checklists first.
   - Consider desktop UI automation only if the workflow needs long-term automated UI coverage.

## Migration Phases

### Phase 0: Baseline and Environment

- Create a clean migration branch/checkpoint.
- Run and record current baseline:
  - `dotnet build "FormID Database Manager.slnx"`
  - `dotnet test "FormID Database Manager.Tests"`
- Verify WinUI toolchain:
  - Visual Studio with the WinUI application development workload.
  - Developer Mode enabled.
  - `dotnet new list winui` shows the WinUI template.
- Decide packaged vs unpackaged before writing startup, storage, or publish code.
- Document the selected deployment model in `README.md` once implementation starts.

### Phase 1: Extract UI-Neutral Core

- Add a core class library targeting `.NET 10` for non-UI code.
- Move models, ViewModels, and UI-neutral services into the core project.
- Keep `CommunityToolkit.Mvvm`; it is compatible with the target architecture and avoids a ViewModel rewrite.
- Split `IThreadDispatcher` from the Avalonia implementation:
  - Keep `IThreadDispatcher` in core.
  - Remove Avalonia defaults from core constructors.
  - Inject the dispatcher from the UI project or tests.
- Add an `IFileDialogService` abstraction for:
  - Select game directory.
  - Select database save path.
  - Select FormID list file.
- Keep Mutagen and SQLite processing behavior unchanged.
- Move or preserve comments/docstrings when moving code. Rewrite comments only when framework-specific wording becomes wrong.

### Phase 2: Scaffold the WinUI Shell

- Scaffold a new C# WinUI project from the official template.
- Prefer the template's default packaged model unless Phase 0 chooses unpackaged.
- Target the Windows-specific TFM selected by the template, such as `net10.0-windows10.0.19041.0`.
- Reference the core project.
- Add required packages only where needed:
  - `CommunityToolkit.Mvvm` in core or UI as required by source generators.
  - `Microsoft.WindowsAppSDK` through the WinUI template.
  - Mutagen and `Microsoft.Data.Sqlite` in core if processing code moves there.
- Build and launch the blank WinUI app before porting UI. This gives a known-good template baseline for later troubleshooting.

Phase 2 verification checkpoint, 2026-06-09:

- `dotnet new list winui` showed the C# `WinUI 3 App` template, and `dotnet new winui --help` confirmed `--framework net10.0`, `--unpackaged false`, and `--no-solution-file`.
- `FormID Database Manager.WinUI` was scaffolded with `dotnet new winui -o "FormID Database Manager.WinUI" --framework net10.0 --unpackaged false --no-solution-file`, which emitted `net10.0-windows10.0.19041.0`, `UseWinUI`, `EnableMsixTooling`, `Package.appxmanifest`, package assets, and Windows App SDK package references.
- `FormID Database Manager.WinUI` references `FormID Database Manager.Core`, and `FormID Database Manager.slnx` includes both the existing Avalonia project and the new WinUI shell.
- `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64` succeeded with 0 warnings and 0 errors.
- `dotnet build "FormID Database Manager.slnx"` succeeded with 0 warnings and 0 errors, including the Avalonia app and the WinUI shell.
- `dotnet build "FormID Database Manager\FormID Database Manager.csproj"` succeeded with 0 warnings and 0 errors, confirming the Avalonia app remains buildable during the staged migration.
- The generated packaged launch profile is `commandName: MsixPackage`; `dotnet run --launch-profile "FormID Database Manager.WinUI (Package)"` cannot apply that profile from the CLI. Local packaged verification used `Add-AppxPackage -Register` against the generated x64 `AppxManifest.xml`, then launched `shell:AppsFolder\f403736a-da6f-4a60-b086-e0a232acbcaa_9zz4h110yvjzm!App`.
- Objective launch evidence: process `FormID Database Manager.WinUI` opened from the x64 WinUI output, returned main window handle `7211482`, title `WinUI Desktop`, and `Responding: True`.
- Phase 9 deployment follow-up: replace the template identity/signing defaults in `Package.appxmanifest` (`Name="f403736a-da6f-4a60-b086-e0a232acbcaa"`, `Publisher="CN=User Name"`, display name `FormID Database Manager.WinUI`) with release-ready identity, publisher, signing, display name, and distribution-channel decisions.

### Phase 3: Port the Main Window UI

- Create `App.xaml` and central resources for theme-aware styles.
- Port `MainWindow.axaml` to `MainWindow.xaml` using `Microsoft.UI.Xaml` controls.
- Start with a standard WinUI window and title bar. Reintroduce custom title bar behavior only after core parity is working.
- Replace Avalonia-only controls and properties:
  - `ExperimentalAcrylicBorder` -> `MicaBackdrop` or `DesktopAcrylicBackdrop`, with normal theme fallback.
  - `DockPanel` layouts -> WinUI `Grid` or `StackPanel`.
  - `IsVisible` -> WinUI `Visibility` or computed ViewModel properties.
  - Avalonia `PlaceholderText`, `ComboBox`, `TextBox`, `CheckBox`, and `ProgressBar` mappings should be checked property-by-property.
- Use a virtualization-friendly WinUI `ListView` for plugins, with a `CheckBox` item template bound to `PluginListItem.IsSelected`.
- Replace the red/green message borders with WinUI `InfoBar` surfaces or a compact message panel with severity styling.
- Keep the initial port behavior-first. A later polish pass can improve layout density, icons, and responsive behavior.

Phase 3 verification checkpoint, 2026-06-09:

- `FormID Database Manager.WinUI/MainWindow.xaml` and `MainWindow.xaml.cs` now provide the single-window WinUI workflow surface, and `App.OnLaunched` instantiates `MainWindow` directly instead of navigating to the generated counter `MainPage`.
- The generated `Views/MainPage.xaml` and `Views/MainPage.xaml.cs` files were removed because they are no longer referenced by the launch path.
- The WinUI UI binds to the core `MainWindowViewModel` and `PluginListItem` types for game selection, paths, plugin filtering, plugin checkbox selection, messages, and progress state.
- `MainWindowViewModel` exposes `HasErrorMessages` and `HasInformationMessages` so WinUI `InfoBar` message surfaces can bind to stable boolean state instead of collection-count converter semantics.
- Picker-dependent buttons report a temporary information message until the Phase 4 picker services exist, and the process action reports that WinUI workflow parity remains disabled until Phase 5.
- `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64` succeeded with 0 warnings and 0 errors.
- `dotnet build "FormID Database Manager.slnx"` succeeded with 0 warnings and 0 errors, including the existing Avalonia project and the staged WinUI project.
- `dotnet test "FormID Database Manager.Tests"` passed with 278 tests passed, 11 skipped, and 0 failed.
- `rg "Avalonia|axaml|ExperimentalAcrylicBorder|DockPanel|IsVisible|Boolean_Converter|BooleanConverter" "FormID Database Manager.WinUI" -g "!bin/**" -g "!obj/**"` found no WinUI project matches.
- `rg "MainPage|Hello, World|Welcome to WinUI 3|Current count|OnCountClicked|Click Me" "FormID Database Manager.WinUI" -g "!bin/**" -g "!obj/**"` found no WinUI project matches.
- Packaged local verification refreshed the x64 registration with `Add-AppxPackage -Register` against `FormID Database Manager.WinUI\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\AppxManifest.xml`, then launched `shell:AppsFolder\f403736a-da6f-4a60-b086-e0a232acbcaa_9zz4h110yvjzm!App` through Explorer.
- Objective launch evidence: process `FormID Database Manager.WinUI` opened from the x64 WinUI output, returned main window handle `16911194`, title `FormID Database Manager`, and `Responding: True`.

### Phase 4: Port Platform Services

- Implement `WinUiThreadDispatcher` using the WinUI `DispatcherQueue`.
- Implement `WinUiFileDialogService` using Windows App SDK pickers:
  - `Microsoft.Windows.Storage.Pickers.FolderPicker` for game directory.
  - `FileSavePicker` for database path.
  - `FileOpenPicker` for the optional FormID text file.
- Pass `AppWindow.Id` into the picker constructors.
- Use older `Windows.Storage.Pickers` plus `InitializeWithWindow` only if the selected Windows App SDK version or app elevation requirements force that fallback.
- Preserve the existing error-reporting behavior by routing picker exceptions to the ViewModel.
- Keep cancellation on window close: the WinUI window should still cancel processing and dispose the ViewModel/service lifecycle.

Phase 4 verification checkpoint, 2026-06-09:

- `FormID Database Manager.WinUI/Services/WinUiThreadDispatcher.cs` now adapts the window `DispatcherQueue` to the core `IThreadDispatcher`, including awaited callback completion, queued exception propagation, and deterministic failure if `TryEnqueue` rejects work during shutdown.
- `FormID Database Manager.WinUI/Services/WinUiFileDialogService.cs` now uses Windows App SDK `Microsoft.Windows.Storage.Pickers` with `AppWindow.Id`, path-returning folder/save/open picker results, `.db` database save choices, `.txt` FormID list filtering, cancellation-as-`null`, and ViewModel error reporting.
- `FormID Database Manager.WinUI/MainWindow.xaml.cs` now owns the picker service, game detection/location services, plugin-list manager, and processing service; close/dispose cancels processing, disposes the processing service, unsubscribes the close handler, and disposes the ViewModel once.
- WinUI game selection now performs background installed-location lookup, ignores stale lookup results, resets directory/plugin state, applies detected directories, and loads plugins through `PluginListManager`.
- Browse, database, and FormID list buttons now call real WinUI picker services and only update ViewModel paths when a path is returned.
- Select All, Select None, and advanced-mode reload now use `PluginListManager`; `ProcessFormIds_Click` intentionally remains scoped to the Phase 5 pending message while the owned processing service is available for close-time cancellation.
- Added focused coverage for dispatcher queue behavior and WinUI platform-service source wiring. `dotnet test "FormID Database Manager.Tests" --filter "FullyQualifiedName~QueuedThreadDispatcherTests|FullyQualifiedName~WinUiPlatformServiceSourceTests"` passed with 7 passed, 0 skipped, and 0 failed.
- `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64` succeeded with 0 warnings and 0 errors.
- `dotnet build "FormID Database Manager.slnx"` succeeded with 0 warnings and 0 errors, including the existing Avalonia app and staged WinUI project.
- `dotnet test "FormID Database Manager.Tests"` passed with 285 tests passed, 11 skipped, and 0 failed. The skipped tests are the existing symbolic-link/game-environment and manual load/stress tests.
- `rg "Avalonia|axaml|AXAML" "FormID Database Manager.WinUI" -g "!bin/**" -g "!obj/**"` found no WinUI project matches.
- Packaged local verification refreshed the x64 registration with `Add-AppxPackage -Register` against `FormID Database Manager.WinUI\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\AppxManifest.xml`, then launched `shell:AppsFolder\f403736a-da6f-4a60-b086-e0a232acbcaa_9zz4h110yvjzm!App` through Explorer.
- Objective launch evidence: process `FormID Database Manager.WinUI` opened from the x64 WinUI output, returned main window handle `30084502`, title `FormID Database Manager`, and `Responding: True`.
- Phase 5 follow-up: port full processing parity, including start/cancel process button behavior, parameter validation, default database path generation, progress updates, and manual verification against at least one real or representative game-directory flow.

### Phase 5: Restore Behavior Parity

Verify each existing workflow against the WinUI shell:

- Game dropdown populates all supported `GameRelease` values.
- Selecting a game detects installed directories on a background thread.
- Stale game-directory lookup results are ignored.
- Browse can select a directory and auto-detect the game when possible.
- Multiple detected directories show a selectable directory control.
- Plugin list loads, filters live, and preserves checkbox selection.
- Select All and Select None update all plugins.
- Advanced mode reloads plugins with base game/DLC visibility.
- Database picker sets `DatabasePath`.
- FormID list picker sets `FormIdListPath`.
- Process starts, reports progress, and can be canceled.
- Default database path generation still uses `GameReleaseHelper.GetSafeTableName`.
- Error and information messages cap at the existing limits.

Phase 5 verification checkpoint, 2026-06-09:

- `FormID Database Manager.WinUI/MainWindow.xaml.cs` now replaces the deferred processing placeholder with the Avalonia-parity processing workflow: selected-game validation, plugin-mode validation, FormID-list mode, selected-plugin snapshots, update mode, default database path generation through `GameReleaseHelper.GetSafeTableName`, progress updates through `MainWindowViewModel.UpdateProgress`, cancellation through `PluginProcessingService.CancelProcessing`, and start/finally process-button state resets.
- WinUI workflow parity checks remain wired through the Phase 4 code paths: `GameComboBox` binds to `MainWindowViewModel.AvailableGames`; game-selection installed-location lookup still runs through `Task.Run` and `_gameSelectionVersion` stale-result checks; Browse suppresses duplicate installed-location lookup while applying auto-detected games; detected directory changes reload plugins; plugin filtering, individual checkbox selection, Select All, Select None, and advanced-mode reloads continue through the shared ViewModel and `PluginListManager`; database and FormID list pickers update paths only when a selection is returned; workflow messages still route through the capped ViewModel collections.
- Added Phase 5 guardrails in `WinUiPlatformServiceSourceTests` for the restored WinUI processing workflow and in `MainWindowViewModelTests` for selected-plugin snapshots plus the default 10-message caps. `dotnet test "FormID Database Manager.Tests" --filter "FullyQualifiedName~WinUiPlatformServiceSourceTests|FullyQualifiedName~MainWindowViewModelTests"` passed with 63 passed, 0 skipped, and 0 failed.
- `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64` succeeded with 0 warnings and 0 errors.
- `dotnet build "FormID Database Manager.slnx"` succeeded with 0 warnings and 0 errors, including the Avalonia app and staged WinUI project.
- `dotnet test "FormID Database Manager.Tests"` passed with 289 tests passed, 11 skipped, and 0 failed. The skipped tests are the existing symbolic-link/game-environment and manual load/stress tests.
- `rg "Avalonia|axaml|AXAML" "FormID Database Manager.WinUI"` found no WinUI project matches.
- Packaged local verification refreshed the x64 registration with `Add-AppxPackage -Register` against `FormID Database Manager.WinUI\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\AppxManifest.xml`, then launched `shell:AppsFolder\f403736a-da6f-4a60-b086-e0a232acbcaa_9zz4h110yvjzm!App`.
- Objective launch evidence: process `FormID Database Manager.WinUI` opened from the x64 WinUI output, returned process ID `58640`, main window handle `6560662`, title `FormID Database Manager`, and `Responding: True`.
- Default database path generation was observed with `DatabasePath` empty: selecting Fallout 4 and starting processing set the WinUI database path to `C:\WINDOWS\system32\Fallout4.db` through `GameReleaseHelper.GetSafeTableName`. The packaged shell could not create that file from its default working directory, producing the expected SQLite permission error for that path.
- Representative game-directory workflow verification used the Fallout 4 install discovered at `HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4`, `Installed Path = E:\SteamLibrary\steamapps\common\Fallout 4\`. The WinUI game selector loaded that directory, displayed 116 visible plugin checkboxes and reported `Loaded 184 non-base game plugins`. The database picker set `C:\Users\evild\AppData\Local\Temp\FIDM-WinUI-Phase5\Fallout4-ui-verification.db`; Select All plus Process changed the button to `Cancel Processing`; a second Process click showed `Cancelling...`; the button returned to `Process FormIDs`; the app remained responsive; and the temp database file was created at 4096 bytes. The cancellation surfaced an expected per-plugin cancellation error for `ccRZRFO4001-TunnelSnakes.esm`, confirming the active service cancellation path was exercised.

### Phase 6: Update Bindings and ViewModel Details

- Prefer `Binding` for template data and dynamic ViewModel properties; use `x:Bind` where strongly typed bindings are straightforward.
- Add ViewModel boolean properties for message visibility if WinUI binding to `Collection.Count` is brittle:
  - `HasErrorMessages`
  - `HasInformationMessages`
- Raise those properties when message collections change.
- Audit `IDataErrorInfo` on `PluginListItem`; WinUI validation behavior may not match Avalonia's.
- Make process button text bind to processing state or update it in a small WinUI event handler.
- Keep the existing debounce/filtering logic, but verify it runs on the WinUI dispatcher.

Phase 6 verification checkpoint, 2026-06-09:

- `FormID Database Manager.WinUI/MainWindow.xaml` was audited for binding semantics. Template data, observable collection sources, and dynamic ViewModel properties remain on runtime `Binding`; no `x:Bind` bindings are used in the main window. Binding-critical source tests now lock down game, directory, path, plugin-filter, plugin-list, message, progress, and process-button wiring against the shared core ViewModel.
- Process button content remains a narrow WinUI code-behind responsibility for Phase 6. Source tests verify the initial `Process FormIDs` content, the `Cancel Processing` start state assignment, and the `Process FormIDs` reset in the `finally` path without adding duplicate WinUI-only ViewModel state.
- `PluginListItem` validation is documented by tests as a model-level contract in the current WinUI shell. Whitespace names return `Name cannot be empty`; valid plugin names remain selectable through the existing checkbox template.
- ViewModel tests now cover `HasErrorMessages` and `HasInformationMessages` notifications for add, clear, and replacement collection changes. Debounced filtering tests verify dispatcher posting when dispatcher access is unavailable and verify that hidden/shown filtered plugins preserve the same `PluginListItem` instance and `IsSelected` state.
- Focused Phase 6 tests passed: `dotnet test "FormID Database Manager.Tests" --filter "FullyQualifiedName~WinUiPlatformServiceSourceTests|FullyQualifiedName~MainWindowViewModelTests"` completed with 74 passed, 0 skipped, and 0 failed.
- `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64` succeeded with 0 warnings and 0 errors.
- `dotnet build "FormID Database Manager.slnx"` succeeded with 0 warnings and 0 errors.
- `dotnet test "FormID Database Manager.Tests"` passed with 300 tests passed, 11 skipped, and 0 failed. The skipped tests remain the existing symbolic-link and manual performance/load/stress tests.
- Packaged local verification registered the x64 WinUI manifest and launched `shell:AppsFolder\f403736a-da6f-4a60-b086-e0a232acbcaa_9zz4h110yvjzm!App`. UI Automation observed process `108244`, main window handle `14095188`, title `FormID Database Manager`, `Responding: True`, and the expected binding-critical controls: game selector, directory selector, path fields, plugin filter, plugin list, message area, progress bar, and process button.
- Message-bar and process reset verification clicked `Process FormIDs` with no game selected. The Errors `InfoBar` opened with `Please select a game from the dropdown first.`, and the process button reset to `Process FormIDs`.
- Representative Fallout 4 workflow verification selected Fallout 4 in the packaged shell and loaded the installed game directory. The shell reported `Loaded 184 non-base game plugins` and exposed 86 visible plugin checkboxes through UI Automation.
- Plugin filtering and selection preservation were verified live with `ccRZRFO4001-TunnelSnakes.esm`: filtering to `Tunnel` showed the plugin, toggling it selected changed the checkbox to `On`, filtering to `NoSuchPluginPhase6` hid it, and filtering back to `Tunnel` returned the same visible plugin with selection still `On`.
- A selected-plugin processing attempt in the packaged shell reset the process button to `Process FormIDs` and surfaced the expected default-path SQLite failure, `SQLite Error 14: 'unable to open database file'`, because the packaged launch defaulted the database path to `C:\WINDOWS\system32\Fallout4.db`. UI Automation could not set `DatabasePathTextBox` because it is read-only, and a direct executable launch with a writable working directory did not expose a UI window for automation. The cancel transition remains covered by source-level guardrails in this checkpoint; a fully writable packaged processing run should be rechecked when picker automation or a manual desktop pass is available (Human edit: A picker dialog did pop up, but was either not recognized by UI automation or was the wrong type).

### Phase 7: Rework Tests

- Move service and ViewModel tests to the core project dependency path.
- Convert ViewModel tests that only need state changes from `[AvaloniaFact]` to regular xUnit facts using `SynchronousThreadDispatcher`.
- Replace `AvaloniaThreadDispatcherTests` with `WinUiThreadDispatcher` tests or a launch-time smoke test.
- Replace `WindowManagerTests` with tests around `IFileDialogService` where logic can be mocked; keep actual picker behavior as manual or smoke verification.
- Retire `Avalonia.Headless.XUnit`, `TestInitialization.cs`, and `UiTestHost` once WinUI UI coverage is in place.
- Keep integration and performance tests focused on Mutagen/SQLite behavior.
- Create a CI workflow to run tests on a Windows runner.

Phase 7 verification checkpoint, 2026-06-09:

- ViewModel tests now run as standard xUnit facts through `FormID Database Manager.Core` and `FormID Database Manager.TestUtilities` with `SynchronousThreadDispatcher` or focused fake dispatchers. The test project no longer references `Avalonia.Headless.XUnit`, `Avalonia`, `Avalonia.Themes.Fluent`, or the legacy Avalonia app project.
- Avalonia-only test host files and UI tests were retired after replacement coverage was added or preserved. WinUI source tests now lock down `WinUiThreadDispatcher` delegation, `IFileDialogService` picker workflow consumption, migration-critical controls and handlers, binding-critical XAML, and platform-service wiring. Real WinUI picker behavior remains manual/smoke coverage because the automated test project does not host a live WinUI window.
- Windows CI was added at `.github/workflows/windows-ci.yml`. It restores the solution, builds the WinUI project for x64, builds the solution, and runs `dotnet test "FormID Database Manager.Tests"` on `windows-latest` using existing skip attributes for manual performance, load, stress, and environment-specific tests.
- Final automated-headless search: `rg "AvaloniaFact|Avalonia.Headless|UiTestHost|TestInitialization|AvaloniaThreadDispatcher|using Avalonia|Avalonia.Themes|WindowManager" "FormID Database Manager.Tests"` found no automated headless dependencies. The only remaining Avalonia text in tests is the intentional `CoreProjectBoundaryTests` guard that asserts Core source files do not contain `using Avalonia`.
- Focused Phase 7 tests passed: `dotnet test "FormID Database Manager.Tests" --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~QueuedThreadDispatcherTests|FullyQualifiedName~ImmediateThreadDispatcherTests|FullyQualifiedName~WinUiPlatformServiceSourceTests|FullyQualifiedName~CoreProjectBoundaryTests"` completed with 85 passed, 0 skipped, and 0 failed.
- `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64` succeeded with 0 warnings and 0 errors.
- `dotnet build "FormID Database Manager.slnx"` succeeded with 0 warnings and 0 errors.
- `dotnet test "FormID Database Manager.Tests"` passed with 274 tests passed, 11 skipped, and 0 failed. The skipped tests remain the existing symbolic-link and manual performance/load/stress tests.

### Phase 8: Remove Avalonia

After WinUI behavior parity and tests are stable:

- Remove Avalonia package references from app and test projects.
- Delete or archive:
  - `App.axaml`
  - `MainWindow.axaml`
  - `Boolean_Converter.cs`, unless replaced by a WinUI converter
  - `AvaloniaThreadDispatcher`
  - Avalonia headless test files
- Update `Program.cs` and startup to the WinUI template shape.
- Search for remaining Avalonia references with `rg "Avalonia|axaml|AvaloniaFact|Headless"`.
- Build and test after removal.

### Phase 9: Deployment and Publish

- If packaged:
  - Produce MSIX/MSIX bundle output.
  - Decide Store, AppInstaller, or direct MSIX distribution.
  - Handle signing and update flow for the selected channel.
- If unpackaged:
  - Set the appropriate project properties for unpackaged Windows App SDK startup.
  - Decide framework-dependent vs self-contained.
  - Verify whether `PublishSingleFile` remains supported for the selected Windows App SDK version and runtime model.
  - Include or install the Windows App SDK runtime as required.
- Update README build/run/publish commands.
- Verify release output on a clean Windows machine or VM.

### Phase 10: UX, Accessibility, and Final Polish

- Check layout at wide, medium, and narrow window widths.
- Support light, dark, and high contrast themes through WinUI resources.
- Ensure keyboard-only operation for all primary flows.
- Add useful automation names for controls that are not self-explanatory.
- Verify text scaling does not clip labels, picker paths, progress text, or button content.
- Confirm long plugin lists scroll smoothly and remain responsive during filtering.
- Keep Windows-native controls and Fluent styling as the default; avoid custom chrome or custom controls unless a verified gap requires them.

## Risk Register

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Packaging choice conflicts with current single-file publish workflow | Release flow may change late | Decide packaged vs unpackaged in Phase 0 and keep startup/publish code aligned |
| WinUI UI tests are not a drop-in replacement for Avalonia headless tests | Coverage may appear to regress | Move behavior to core tests, keep UI smoke/manual checks, add desktop automation only where valuable |
| File picker API version mismatch | Picker implementation may need rework | Prefer Windows App SDK 1.8 pickers; document fallback to HWND-initialized WinRT pickers |
| Dispatcher defaults still reference Avalonia | Core project cannot stay UI-neutral | Require dispatcher injection or provide a UI-neutral test/default dispatcher |
| Binding semantics differ from Avalonia | Message visibility, validation, or templates may fail silently | Add explicit ViewModel properties and smoke tests for binding-critical UI |
| Mutagen reflection and trimming interact with new publish model | Release build could miss record types | Preserve current trimming stance and root assemblies; test release publish separately |
| Custom title bar/backdrop causes startup or drag-region issues | App feels broken despite correct logic | Start with standard title bar, add backdrop/custom title behavior after parity |

## Completion Criteria

The migration is complete when:

- `rg "Avalonia|axaml|AvaloniaFact|Headless"` finds no remaining required app/test dependencies.
- `dotnet build "FormID Database Manager.slnx"` succeeds on Windows.
- `dotnet test "FormID Database Manager.Tests"` succeeds, excluding any explicitly manual game-installation tests.
- The WinUI app launches from the chosen local workflow and shows the main window.
- The app can process plugin files and optional FormID text files with output matching the Avalonia baseline.
- Cancel, progress, errors, and information messages behave correctly.
- The release package or executable is produced through the documented publish workflow.
- Manual verification covers light/dark theme, keyboard navigation, narrow resize, and at least one real game-directory flow.
