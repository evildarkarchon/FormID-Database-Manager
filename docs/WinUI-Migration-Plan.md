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
- Windows CI was added at `.github/workflows/windows-ci.yml`. It restores the solution, builds the WinUI project for x64, builds the solution, and runs the CI-safe test suite on `windows-latest` with manual performance, performance regression, load, and stress categories excluded. These performance suites remain opt-in through `RUN_MANUAL_PERFORMANCE_TESTS=1` or explicit local filters.
- Final automated-headless search: `rg "AvaloniaFact|Avalonia.Headless|UiTestHost|TestInitialization|AvaloniaThreadDispatcher|using Avalonia|Avalonia.Themes|WindowManager" "FormID Database Manager.Tests"` found no automated headless dependencies. The only remaining Avalonia text in tests is the intentional `CoreProjectBoundaryTests` guard that asserts Core source files do not contain `using Avalonia`.
- Focused Phase 7 tests passed: `dotnet test "FormID Database Manager.Tests" --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~QueuedThreadDispatcherTests|FullyQualifiedName~ImmediateThreadDispatcherTests|FullyQualifiedName~WinUiPlatformServiceSourceTests|FullyQualifiedName~CoreProjectBoundaryTests"` completed with 85 passed, 0 skipped, and 0 failed.
- `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64` succeeded with 0 warnings and 0 errors.
- `dotnet build "FormID Database Manager.slnx"` succeeded with 0 warnings and 0 errors.
- `dotnet test "FormID Database Manager.Tests"` passed with 274 tests passed, 11 skipped, and 0 failed. The skipped tests remain the existing symbolic-link and manual performance/load/stress tests; hardware-sensitive performance regression tests are treated as manual performance coverage for CI.

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

Phase 8 verification checkpoint, 2026-06-09:

- The legacy `FormID Database Manager` desktop project was removed from `FormID Database Manager.slnx`, and its startup files, AXAML views, converter, dispatcher, picker service, manifest, project file, and publish profiles were deleted from the active source tree. The remaining ignored `bin`/`obj` outputs under the old directory are not part of the buildable solution.
- Behavior formerly hosted by the removed shell is represented by `FormID Database Manager.Core` and `FormID Database Manager.WinUI`: Core owns models, ViewModels, processing, database, game detection, load-order, dispatcher abstractions, and file-dialog abstractions; WinUI owns startup, XAML, window workflow, file pickers, and dispatcher implementation.
- Focused reference searches for `Avalonia|axaml|AvaloniaFact|Headless` found no matches in `FormID Database Manager.Core`, `FormID Database Manager.WinUI`, `FormID Database Manager.Tests`, `FormID Database Manager.TestUtilities`, active `*.csproj` files, `FormID Database Manager.slnx`, `README.md`, `AGENTS.md`, `CLAUDE.md`, or `GEMINI.md`.
- Remaining broad-search matches are intentionally historical or workflow-local: this migration plan, archived OpenSpec changes, the completed Phase 7 OpenSpec change, and the Phase 8 OpenSpec artifacts themselves.
- `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64` succeeded with 0 warnings and 0 errors.
- `dotnet build "FormID Database Manager.slnx"` succeeded with 0 warnings and 0 errors across Core, TestUtilities, Tests, and WinUI.
- `dotnet test "FormID Database Manager.Tests"` passed with 282 tests passed, 11 skipped, and 0 failed. The skipped tests remain the existing symbolic-link and manual performance/load/stress tests.

### Phase 9: Deployment and Publish

- Support both packaged and unpackaged release lanes from the WinUI project.
- Keep the base project MSIX-capable; do not set `WindowsPackageType=None` globally.
- Add explicit publish profiles or documented commands for:
  - Packaged MSIX/MSIX bundle output.
  - Unpackaged framework-dependent output, if a small download with a separately installed Windows App SDK runtime is desired.
  - Unpackaged self-contained output, if the release should carry the Windows App SDK runtime with the app.
  - Unpackaged single-file output only when using an eligible Windows App SDK version and the required self-contained properties.
- For the packaged lane:
  - Decide Store, AppInstaller, or direct MSIX distribution.
  - Handle signing and update flow for the selected channel.
- For the unpackaged lane:
  - Set `WindowsPackageType=None` only in the unpackaged profile or command.
  - Decide framework-dependent vs self-contained.
  - Include or install the Windows App SDK runtime as required.
  - If using `PublishSingleFile`, set the required self-contained and self-extract properties and verify first-launch extraction behavior.
- Fix default writable output locations before release verification so packaged launches do not default the database path to `C:\WINDOWS\system32` (or any other directory that requires escalated permissions).
- Update README build/run/publish commands.
- Verify both release outputs on a clean Windows machine or VM.

Phase 9 implementation checkpoint, 2026-06-10:

- Tooling inspection resolved .NET SDK `10.0.300`, MSBuild `18.6.3`, and Windows App SDK package `1.8.260529003`. The base WinUI project evaluates `EnableMsixTooling=true` and `WindowsPackageType=MSIX`; it does not set `WindowsPackageType=None` globally.
- Debug CLI commands pass `WindowsPackageType=None` and `WindowsAppSDKSelfContained=true` explicitly so `dotnet run` uses the advertised unpackaged self-contained path without changing the base project default.
- Added explicit x64 publish profiles:
  - `win-x64-msix.pubxml` uses single-project MSIX packaging through `dotnet build` with `GenerateAppxPackageOnBuild=true`, `UapAppxPackageBuildMode=SideloadOnly`, and `AppxBundle=Never`. Single-project MSIX does not produce MSIX bundles directly, so any future bundle would need a separate bundling step.
  - `win-x64-unpackaged-framework-dependent.pubxml` scopes `WindowsPackageType=None`, `SelfContained=false`, and `WindowsAppSDKSelfContained=false` to the framework-dependent unpackaged lane. Target machines must install the matching .NET desktop runtime and Windows App SDK runtime.
  - `win-x64-unpackaged-self-contained.pubxml` scopes `WindowsPackageType=None`, `SelfContained=true`, and `WindowsAppSDKSelfContained=true` to the self-contained unpackaged lane. The output carries the Windows App SDK runtime with the app.
- The packaged lane targets direct MSIX verification for this phase. Microsoft Store submission, a production signing certificate, AppInstaller feed generation, and automatic update flow are deferred; local verification uses the current development manifest identity and records any signing or clean-machine installation blocker.
- Single-file unpackaged output is not a Phase 9 release lane. Windows App SDK 1.8 supports it only for unpackaged self-contained apps with self-extraction, and this phase does not enable it because first-launch extraction behavior still needs clean-machine verification.
- Empty WinUI database paths now resolve to `%LOCALAPPDATA%\FormID Database Manager\Databases\<SafeGameName>.db`; the directory is created before processing starts and the resolved path is assigned back to the ViewModel.

Phase 9 verification checkpoint, 2026-06-10:

- `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64` succeeded with 0 warnings and 0 errors.
- `dotnet build "FormID Database Manager.slnx"` succeeded with 0 warnings and 0 errors.
- `dotnet test "FormID Database Manager.Tests"` passed with 285 tests passed, 11 skipped, and 0 failed. The skipped tests remain the existing symbolic-link and manual performance/load/stress tests.
- Packaged MSIX lane: `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -c Release -p:Platform=x64 -p:PublishProfile=win-x64-msix.pubxml` produced `FormID Database Manager.WinUI\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\AppPackages\FormID Database Manager.WinUI_1.0.0.0_x64_Test\FormID Database Manager.WinUI_1.0.0.0_x64.msix` (35,270,551 bytes). The build warned that `mspdbcmf.exe` was missing, so no symbols package was generated.
- Packaged install/launch verification is blocked for this phase because the generated MSIX is unsigned (`Get-AuthenticodeSignature` returned `NotSigned`) and no production or trusted development signing certificate is configured. The package manifest depends on `Microsoft.WindowsAppRuntime.1.8` `MinVersion="8000.879.2017.0"`, and the generated package folder includes the matching x64 dependency MSIX.
- Unpackaged framework-dependent lane: `dotnet publish "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -c Release -p:Platform=x64 -p:PublishProfile=win-x64-unpackaged-framework-dependent.pubxml` produced `FormID Database Manager.WinUI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\unpackaged-framework-dependent\`. Computer Use launch verification reached the Windows App Runtime prerequisite dialog; the machine had Windows App Runtime 1.8 installed only below the required package version, and the app requested `>= 8000.879.2017.0`.
- Unpackaged self-contained lane: `dotnet publish "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -c Release -p:Platform=x64 -p:PublishProfile=win-x64-unpackaged-self-contained.pubxml` produced `FormID Database Manager.WinUI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\unpackaged-self-contained\`. Computer Use launch verification did not reach a targetable window; Windows logged an app crash in `Microsoft.UI.Xaml.dll` with exception code `0xc000027b` and WER fault bucket `2038938963162178728`.
- Clean Windows machine or VM verification is blocked because this workspace does not expose a clean Windows VM. The exact local blockers are unsigned MSIX for packaged install, insufficient shared Windows App Runtime package version for framework-dependent launch, and the self-contained WinUI/XAML startup crash above.

### Phase 10: UX, Accessibility, and Final Polish

- Check layout at wide, medium, and narrow window widths.
- Support light, dark, and high contrast themes through WinUI resources.
- Ensure keyboard-only operation for all primary flows.
- Add useful automation names for controls that are not self-explanatory.
- Verify text scaling does not clip labels, picker paths, progress text, or button content.
- Confirm long plugin lists scroll smoothly and remain responsive during filtering.
- Keep Windows-native controls and Fluent styling as the default; avoid custom chrome or custom controls unless a verified gap requires them.

Phase 10 implementation checkpoint, 2026-06-10:

- Main-window layout audit found the expected polish issues: fixed label and path widths, a fixed detected-directory column, right-side action columns, horizontal mode toggles, and missing explicit automation metadata for path fields, plugin filtering, progress, and message surfaces.
- Representative Phase 10 verification widths are recorded in `App.xaml` resources as wide `1100`, medium `800`, and narrow `640` logical pixels. The plugin list sits in the root grid's star-sized row with a `120` logical-pixel minimum so ordinary non-maximized windows condense the list region instead of scrolling the whole workflow.
- `MainWindow.xaml` now uses a compact root-grid workflow with flexible path/action grids, named visible labels, a constrained plugin-list border, horizontal mode toggles, side-by-side progress/action controls, and bottom `InfoBar` message surfaces. Path text boxes now have `MinWidth=0` and horizontal scrolling, long plugin names trim with ellipsis, and labels/messages/progress text wrap with text scaling enabled.
- Accessibility metadata was added through visible-label relationships, explicit names/help text where native content is insufficient, plugin checkbox automation names, progress/message names, and access keys for the primary commands: Browse (`B`), Select Database (`D`), Select List File (`L`), Select All (`A`), Select None (`N`), and Process FormIDs (`P`). No custom controls, custom chrome, or custom automation peers were introduced.
- Theme and high-contrast risk is kept low by continuing to use WinUI theme resources and native Fluent control states for app background, secondary text, plugin-list surface, borders, focus, buttons, progress, and `InfoBar` severity rendering.
- Added WinUI source guardrails in `WinUiPlatformServiceSourceTests` for Phase 10 responsive resources, named controls, bindings, automation metadata, access keys/tab order, text scaling, plugin-list virtualization constraints, and theme-resource usage.
- Focused Phase 10 source tests passed: `dotnet test "FormID Database Manager.Tests" --filter "FullyQualifiedName~WinUiPlatformServiceSourceTests"` completed with 16 passed, 0 skipped, and 0 failed after first confirming the new tests failed against the pre-Phase-10 shell.
- `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64` succeeded with 0 warnings and 0 errors.
- `dotnet build "FormID Database Manager.slnx"` succeeded with 0 warnings and 0 errors.
- `dotnet test "FormID Database Manager.Tests"` passed with 288 tests passed, 20 skipped, and 0 failed. The skipped tests remain existing symbolic-link and manual performance/load/stress/performance-regression coverage.
- Local live desktop verification for wide/medium/narrow resize, light/dark/high contrast, keyboard-only activation, UI Automation metadata inspection, enlarged text scaling, and representative long-list scrolling/filtering is blocked in this environment because the Windows automation helper fails during setup before it can list apps: `Package subpath './dist/project/cua/sky_js/src/targets/windows/internal/computer_use_client_base.js' is not defined by "exports" in C:\Users\evild\AppData\Local\OpenAI\Codex\runtimes\cua_node\2f053e67fec2d258\bin\node_modules\@oai\sky\package.json`. Re-run these live checks with a working desktop automation layer or a manual Windows pass before treating Phase 10 as fully verified.
- Manual visual verification after the layout condensation fix confirmed the selected width/layout, theme/readability, and enlarged-text/long-text behavior are satisfactory. Keyboard-only activation remains tracked separately from the visual pass.

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
- Linux/macOS solution builds are intentionally unsupported because the default solution includes the WinUI desktop shell.
- `dotnet test "FormID Database Manager.Tests"` succeeds, excluding any explicitly manual game-installation tests.
- The WinUI app launches from the chosen local workflow and shows the main window.
- The app can process plugin files and optional FormID text files with output matching the Avalonia baseline.
- Cancel, progress, errors, and information messages behave correctly.
- The release package or executable is produced through the documented publish workflow.
- Manual verification covers light/dark theme, keyboard navigation, narrow resize, and at least one real game-directory flow.
