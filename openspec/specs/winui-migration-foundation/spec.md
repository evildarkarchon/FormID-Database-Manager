## Purpose

Define the Phase 0 and Phase 1 foundation required for the staged WinUI migration while preserving current Avalonia behavior.
## Requirements
### Requirement: Phase 0 baseline is recorded
The system SHALL record the current migration branch, build baseline, test baseline, WinUI template availability, and selected deployment model before Phase 1 code movement is treated as complete.

#### Scenario: Baseline record is available to future migration phases
- **WHEN** a developer reviews the migration documentation after Phase 0
- **THEN** the documentation identifies the current branch/checkpoint, baseline build result, baseline test result, WinUI template status, and packaged MSIX as the selected target deployment model for later WinUI phases

---

### Requirement: UI-neutral core owns reusable application behavior
The system SHALL provide a `.NET 10` core class library containing UI-neutral models, ViewModels, services, and abstractions needed by both the existing Avalonia app and the future WinUI app.

#### Scenario: Core project builds without Avalonia references
- **WHEN** the solution is built after Phase 1 extraction
- **THEN** the core project compiles without Avalonia package references or Avalonia-specific source files

#### Scenario: Current application consumes core behavior
- **WHEN** the existing Avalonia app builds after Phase 1 extraction
- **THEN** it references the core project for reusable processing, game detection, database, load-order, model, and ViewModel code while keeping Avalonia startup and platform services in the app project

---

### Requirement: Platform file dialogs are abstracted for future WinUI implementation
The system SHALL define a core file-dialog abstraction for selecting the game directory, database output path, and optional FormID text file without depending on a specific UI framework.

#### Scenario: File dialog contract is UI-neutral
- **WHEN** a future WinUI picker service is added
- **THEN** it can implement the same core abstraction used by the current UI-facing code for game directory, database save path, and FormID list file selection

---

### Requirement: Existing processing behavior remains unchanged
The system SHALL preserve existing Mutagen parsing, SQLite writing, plugin loading, game detection, filtering, progress, and cancellation behavior during Phase 1 extraction.

#### Scenario: Existing tests remain the behavior guardrail
- **WHEN** the full test suite is run after Phase 1 extraction
- **THEN** UI-independent service and ViewModel tests continue to validate the same behavior through the core project reference

### Requirement: Packaged WinUI shell project is scaffolded
The system SHALL include a new template-first C# WinUI shell project for Phase 2 that uses the packaged deployment model selected in Phase 0.

#### Scenario: WinUI shell project is present
- **WHEN** a developer inspects the solution after Phase 2
- **THEN** `FormID Database Manager.WinUI` exists as a WinUI project with generated startup files, package manifest assets, and Windows App SDK project configuration

#### Scenario: Packaged model is explicit
- **WHEN** a developer inspects the WinUI shell project after scaffolding
- **THEN** the project is configured for packaged MSIX behavior rather than inheriting an unpackaged template default

### Requirement: WinUI shell consumes the UI-neutral core
The system SHALL reference `FormID Database Manager.Core` from the WinUI shell and use the extracted core ViewModel/model types as the state source for the Phase 3 main-window UI.

#### Scenario: Core project reference is available
- **WHEN** the WinUI shell project is built
- **THEN** it restores and compiles with a project reference to `FormID Database Manager.Core`

#### Scenario: Main-window UI consumes core state
- **WHEN** the Phase 3 WinUI shell is reviewed
- **THEN** the main-window UI is bound to `MainWindowViewModel` and core model types rather than duplicating application state in WinUI-only classes

#### Scenario: Platform-service porting remains deferred
- **WHEN** the Phase 3 WinUI shell is reviewed
- **THEN** it has not yet claimed final WinUI picker, dispatcher, processing workflow, or Avalonia removal parity that belongs to later migration phases

### Requirement: Current Avalonia application remains buildable
The system SHALL keep the existing Avalonia application and its project references intact while the WinUI shell is introduced.

#### Scenario: Existing application still participates in the solution
- **WHEN** the full solution is built after Phase 2
- **THEN** both the existing Avalonia application project and the new WinUI shell project are included without removing existing Avalonia startup, view, converter, dispatcher, or picker files

### Requirement: Blank WinUI shell is verified before UI porting
The system SHALL verify the generated WinUI shell before Phase 3 begins by building the shell and recording launch verification for the blank app.

#### Scenario: Blank shell build succeeds
- **WHEN** Phase 2 verification is run
- **THEN** the WinUI shell project builds successfully with its core project reference

#### Scenario: Blank shell launch is recorded
- **WHEN** Phase 2 verification is complete
- **THEN** the verification notes identify the local launch workflow and either objective evidence of a responsive blank WinUI window or a blocking environment/deployment issue that must be resolved before UI porting

### Requirement: WinUI main-window workflow surface is ported
The system SHALL replace the template WinUI counter page with a FormID Database Manager main-window surface covering the primary workflow controls from the Avalonia window.

#### Scenario: Primary workflow controls are present
- **WHEN** the WinUI app launches after Phase 3
- **THEN** the visible UI includes controls for selecting a game, displaying or choosing a game directory, selecting a database path, selecting an optional FormID list file, filtering plugins, selecting plugins, toggling update and advanced modes, viewing messages, viewing progress, and starting processing

#### Scenario: Template counter UI is removed from the launch path
- **WHEN** the WinUI app launches after Phase 3
- **THEN** it does not show the generated counter sample as the primary application UI

### Requirement: WinUI main-window surface uses WinUI-native controls
The system SHALL express the Phase 3 UI with WinUI-native XAML controls, resources, and properties rather than Avalonia-only controls or AXAML semantics.

#### Scenario: Avalonia-only UI APIs are not used by the WinUI surface
- **WHEN** the WinUI project is inspected after Phase 3
- **THEN** the ported main-window surface does not use Avalonia AXAML files, `ExperimentalAcrylicBorder`, `DockPanel`, Avalonia `IsVisible`, or the Avalonia `Boolean_Converter`

#### Scenario: Plugin list remains virtualization-friendly
- **WHEN** the WinUI plugin selection surface is inspected after Phase 3
- **THEN** it uses a virtualization-friendly WinUI list control with checkbox items bound to `PluginListItem.IsSelected`

### Requirement: Phase 3 preserves the staged migration boundary
The system SHALL keep the existing Avalonia application buildable while the WinUI main-window surface is introduced.

#### Scenario: Existing Avalonia app remains in the solution
- **WHEN** the full solution is built after Phase 3
- **THEN** the existing Avalonia application project and the WinUI shell project both remain included and buildable

#### Scenario: Later migration phases remain explicit
- **WHEN** Phase 3 is complete
- **THEN** WinUI file picker implementation, WinUI dispatcher implementation, end-to-end workflow parity, Avalonia dependency removal, deployment identity, and final UX polish remain documented as later-phase work unless separately implemented by another accepted change

### Requirement: Ported WinUI UI is verified before service parity work
The system SHALL verify that the ported WinUI main-window UI builds and launches before Phase 4 platform-service work begins.

#### Scenario: WinUI main-window build succeeds
- **WHEN** Phase 3 verification is run
- **THEN** the WinUI shell project builds successfully with the ported main-window UI

#### Scenario: Ported UI launch is recorded
- **WHEN** Phase 3 verification is complete
- **THEN** the verification notes identify the local launch workflow and either objective evidence that the ported WinUI main-window UI renders or a blocking environment/deployment issue that must be resolved before Phase 4

### Requirement: WinUI dispatcher marshals UI-bound core updates
The system SHALL provide a WinUI implementation of `IThreadDispatcher` that marshals ViewModel and collection updates through the WinUI window's `DispatcherQueue`.

#### Scenario: UI-thread caller runs dispatcher work directly
- **WHEN** WinUI code invokes dispatcher work from the owning UI thread
- **THEN** the dispatcher reports access through `CheckAccess` and executes `InvokeAsync` work without posting it to a background thread

#### Scenario: Background caller queues dispatcher work
- **WHEN** a background operation such as plugin-list refresh or debounced filtering needs to update UI-bound state
- **THEN** the dispatcher queues the update on the WinUI `DispatcherQueue` before mutating the ViewModel or observable collections

#### Scenario: Awaited dispatcher work propagates failures
- **WHEN** work passed to `InvokeAsync` throws after being queued to the WinUI dispatcher
- **THEN** the returned task completes with that exception instead of silently swallowing it or hanging

### Requirement: WinUI file dialogs use Windows App SDK pickers
The system SHALL implement `IFileDialogService` in the WinUI project with Windows App SDK picker APIs constructed from the parent window's `AppWindow.Id`.

#### Scenario: Game directory picker returns a path
- **WHEN** the user chooses a folder through the WinUI game-directory picker
- **THEN** the service returns the selected folder path to the caller

#### Scenario: Database save picker returns a path
- **WHEN** the user chooses a SQLite database output path through the WinUI save picker
- **THEN** the service returns the selected database path to the caller

#### Scenario: FormID list picker returns a path
- **WHEN** the user chooses a text file through the WinUI FormID list picker
- **THEN** the service returns the selected text file path to the caller

#### Scenario: Picker cancellation leaves state unchanged
- **WHEN** the user cancels a WinUI picker
- **THEN** the service returns `null` and the caller does not overwrite the existing ViewModel path

#### Scenario: Picker exceptions are reported through the ViewModel
- **WHEN** a WinUI picker throws during folder, save-file, or open-file selection
- **THEN** the service adds an error message to the ViewModel using the same user-facing error categories as the Avalonia picker service

### Requirement: WinUI main window owns platform service lifecycle
The system SHALL construct and dispose the WinUI platform services and core workflow services needed for Phase 4 from the WinUI main window.

#### Scenario: Production WinUI window uses WinUI dispatcher
- **WHEN** the WinUI main window is created by application startup
- **THEN** its ViewModel, plugin-list manager, and processing service use a `WinUiThreadDispatcher` instead of the core immediate dispatcher

#### Scenario: Window close cancels processing lifecycle
- **WHEN** the WinUI main window closes
- **THEN** it requests cancellation from the owned `PluginProcessingService`, disposes owned disposable services, and disposes the ViewModel

#### Scenario: Existing Avalonia app remains buildable
- **WHEN** Phase 4 platform services are added to the WinUI project
- **THEN** the existing Avalonia application project still builds with its current Avalonia dispatcher and picker service

### Requirement: WinUI service-backed picker and plugin workflows replace placeholders
The system SHALL replace Phase 3 WinUI placeholder messages for platform-service-backed workflows with real service calls where Phase 4 services are sufficient.

#### Scenario: Selecting a game uses installed-location lookup
- **WHEN** the user selects a supported game in the WinUI game dropdown
- **THEN** the WinUI window looks up installed game folders on a background thread, ignores stale lookup results, updates detected directories or information messages, and loads plugins when a directory is available

#### Scenario: Browsing for a game directory updates the workflow
- **WHEN** the user picks a game directory through the WinUI folder picker
- **THEN** the WinUI window updates `GameDirectory`, auto-detects the game when no game is selected, and loads plugins for the current selection when possible

#### Scenario: Selecting a detected directory reloads plugins
- **WHEN** the user chooses a different detected directory in the WinUI directory selector
- **THEN** the WinUI window reloads the plugin list for the selected game and directory

#### Scenario: Selecting database and FormID files updates paths
- **WHEN** the user chooses database or optional FormID list paths through WinUI pickers
- **THEN** the WinUI window updates `DatabasePath` or `FormIdListPath` with the selected path

#### Scenario: Advanced mode reloads plugins
- **WHEN** the user toggles the WinUI advanced-mode checkbox after a game directory is selected
- **THEN** the WinUI window reloads plugins with base game and DLC visibility matching the checkbox state

### Requirement: Phase 4 platform-service wiring is verified
The system SHALL verify and document the WinUI platform-service port before Phase 5 behavior-parity work begins.

#### Scenario: WinUI platform services build
- **WHEN** Phase 4 verification is run
- **THEN** the WinUI project and full solution build successfully with the new platform services

#### Scenario: Existing tests remain green
- **WHEN** the current automated test suite is run after Phase 4
- **THEN** the tests pass or any skipped/manual tests are reported with the same conventions used by prior migration checkpoints

#### Scenario: WinUI launch evidence is recorded
- **WHEN** Phase 4 verification is complete
- **THEN** `docs/WinUI-Migration-Plan.md` records the local launch workflow and either objective evidence that the WinUI main window renders after service wiring or a blocking environment issue that must be resolved before Phase 5

