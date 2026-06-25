## Purpose

Define the Phase 0 and Phase 1 foundation required for the staged WinUI migration while preserving current Avalonia behavior.
## Requirements
### Requirement: Phase 0 baseline is recorded
The system SHALL record the current migration branch, build baseline, test baseline, WinUI template availability, and selected deployment model before Phase 1 code movement is treated as complete.

#### Scenario: Baseline record is available to future migration phases
- **WHEN** a developer reviews the migration documentation after Phase 0
- **THEN** the documentation identifies the current branch/checkpoint, baseline build result, baseline test result, WinUI template status, and the selected target deployment model for later WinUI phases

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

### Requirement: WinUI shell project is scaffolded
The system SHALL include a new template-first C# WinUI shell project for Phase 2.

#### Scenario: WinUI shell project is present
- **WHEN** a developer inspects the solution after Phase 2
- **THEN** `FormID Database Manager.WinUI` exists as a WinUI project with generated startup files and Windows App SDK project configuration

#### Scenario: Portable model is explicit
- **WHEN** a developer inspects the WinUI shell project after deployment cleanup
- **THEN** the project is configured for unpackaged self-contained portable output without MSIX tooling

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

### Requirement: WinUI game and plugin selection workflows retain parity
The system SHALL preserve the existing game selection, directory selection, plugin loading, plugin filtering, plugin selection, and advanced-mode workflows in the WinUI shell while restoring Phase 5 processing behavior.

#### Scenario: Supported games populate the WinUI game dropdown
- **WHEN** the WinUI main window displays the game selector
- **THEN** the selector exposes every supported `GameRelease` value provided by `MainWindowViewModel.AvailableGames`

#### Scenario: Latest game selection owns installed-directory results
- **WHEN** a user selects another game before an earlier installed-directory lookup completes
- **THEN** only the latest game selection may update `GameDirectory`, `DetectedDirectories`, messages, or the plugin list

#### Scenario: Browsed directory can establish the current game
- **WHEN** the user browses to a game directory while no game is selected and game detection succeeds
- **THEN** the WinUI workflow sets `SelectedGame`, sets `GameDirectory`, and loads plugins for that detected game without triggering a duplicate installed-location lookup

#### Scenario: Multiple detected directories remain selectable
- **WHEN** installed-location lookup finds more than one directory for the selected game
- **THEN** the WinUI workflow exposes the detected directories and reloads plugins when the user selects a different directory

#### Scenario: Plugin list selection controls remain live
- **WHEN** plugins are loaded in the WinUI shell
- **THEN** live filtering preserves matching plugin items and Select All, Select None, and individual checkbox changes update the same underlying plugin selection state used for processing

#### Scenario: Advanced mode reloads plugin visibility
- **WHEN** the user toggles advanced mode after a game directory is selected
- **THEN** the WinUI workflow reloads plugins with base game and DLC visibility matching the advanced-mode setting

### Requirement: WinUI processing action starts with existing validation semantics
The system SHALL replace the deferred Phase 5 processing placeholder with a WinUI processing action that validates inputs and builds processing parameters using the same user-facing rules as the existing application.

#### Scenario: Missing game selection prevents processing
- **WHEN** the user starts processing without selecting a game
- **THEN** the WinUI workflow adds an error message telling the user to select a game and does not start `PluginProcessingService`

#### Scenario: Plugin processing requires a game directory
- **WHEN** the user starts plugin processing without an optional FormID list file and without a game directory
- **THEN** the WinUI workflow adds an error message that the game directory is required and does not start `PluginProcessingService`

#### Scenario: Plugin processing requires selected plugins
- **WHEN** the user starts plugin processing without an optional FormID list file and with no selected plugins
- **THEN** the WinUI workflow adds an error message that no plugins are selected and does not start `PluginProcessingService`

#### Scenario: FormID list processing can run without selected plugins
- **WHEN** the user starts processing with a selected game and an optional FormID list file path
- **THEN** the WinUI workflow builds `ProcessingParameters` for text-file processing without requiring a game directory or selected plugins

#### Scenario: Plugin processing passes selected workflow state
- **WHEN** the user starts processing with a selected game, game directory, selected plugins, update-mode state, optional FormID list path, and database path
- **THEN** the WinUI workflow passes matching `ProcessingParameters` to `PluginProcessingService.ProcessPlugins`

### Requirement: WinUI default database path generation remains safe
The system SHALL generate a default database path for WinUI processing through `GameReleaseHelper.GetSafeTableName` when the user has not chosen a database path.

#### Scenario: Missing database path uses safe table name
- **WHEN** the user starts processing with a selected game and an empty `DatabasePath`
- **THEN** the WinUI workflow sets `DatabasePath` to a file in the current directory named with `GameReleaseHelper.GetSafeTableName(selectedGame)` and a `.db` extension before processing starts

### Requirement: WinUI processing progress and cancellation retain parity
The system SHALL expose processing start, progress, cancellation, completion, and failure state through the WinUI ViewModel and process button consistently with the existing application.

#### Scenario: Starting processing updates UI state
- **WHEN** a valid WinUI processing run starts
- **THEN** the workflow clears existing error messages, sets `IsProcessing` to `true`, sets progress to the initial state, and changes the process button content to the cancel action

#### Scenario: Progress reports update ViewModel progress
- **WHEN** `PluginProcessingService` reports progress during a WinUI processing run
- **THEN** `ProgressStatus` and `ProgressValue` update through `MainWindowViewModel.UpdateProgress`

#### Scenario: Processing click cancels active run
- **WHEN** the user clicks the process button while `IsProcessing` is `true`
- **THEN** the WinUI workflow sets the progress status to cancelling and calls `PluginProcessingService.CancelProcessing`

#### Scenario: Processing completion resets active state
- **WHEN** a WinUI processing run finishes, fails, or observes cancellation
- **THEN** the workflow sets `IsProcessing` to `false` and restores the process button content to the start action

#### Scenario: Processing errors are user-visible
- **WHEN** a WinUI processing run throws an unexpected exception
- **THEN** the workflow adds an error message prefixed with the existing FormID processing error wording

### Requirement: WinUI message limits remain enforced
The system SHALL route Phase 5 WinUI workflow error and information messages through `MainWindowViewModel` so the existing message caps remain enforced.

#### Scenario: Error messages cap at existing limit
- **WHEN** WinUI workflow operations add more error messages than the existing maximum
- **THEN** the oldest error messages are removed and only the capped number of newest error messages remains

#### Scenario: Information messages cap at existing limit
- **WHEN** WinUI workflow operations add more information messages than the existing maximum
- **THEN** the oldest information messages are removed and only the capped number of newest information messages remains

### Requirement: Phase 5 behavior parity is verified
The system SHALL verify and document Phase 5 WinUI behavior parity before later migration phases begin.

#### Scenario: Automated verification remains green
- **WHEN** Phase 5 verification is run
- **THEN** the WinUI project build, full solution build, and current automated test suite pass or report skipped/manual tests using the existing project conventions

#### Scenario: WinUI workflow checklist is recorded
- **WHEN** Phase 5 verification is complete
- **THEN** `docs/WinUI-Migration-Plan.md` records a Phase 5 checkpoint covering game selection, stale lookup handling, directory browsing, detected-directory selection, plugin filtering and selection, advanced-mode reloads, database and FormID list pickers, processing start, progress, cancellation, default database path generation, and message caps

#### Scenario: WinUI launch and representative workflow evidence is recorded
- **WHEN** Phase 5 verification is complete
- **THEN** `docs/WinUI-Migration-Plan.md` records packaged WinUI launch evidence and either at least one real or representative game-directory workflow verification or a concrete environment blocker

### Requirement: WinUI bindings use stable binding semantics
The system SHALL use WinUI binding forms that match the bound data source and avoid brittle template or dynamic ViewModel bindings during the Phase 6 migration hardening pass.

#### Scenario: Template and dynamic ViewModel data use runtime bindings
- **WHEN** the Phase 6 WinUI main-window XAML is inspected
- **THEN** item-template data, observable collection sources, and dynamic ViewModel properties use runtime `Binding` unless a binding has a clearly typed `x:Bind` source

#### Scenario: Binding audit preserves primary workflow state
- **WHEN** the Phase 6 WinUI shell is built and reviewed
- **THEN** game selection, directory selection, path display, plugin filtering, plugin checkbox selection, messages, progress, and processing action bindings still target the shared core ViewModel and model state

### Requirement: WinUI message visibility is notified from ViewModel state
The system SHALL drive WinUI error and information message visibility from explicit ViewModel boolean properties that raise change notifications when message collection contents change.

#### Scenario: Error message visibility changes when errors are added or cleared
- **WHEN** the ViewModel error message collection changes from empty to non-empty or non-empty to empty during WinUI workflow operations
- **THEN** `HasErrorMessages` reflects the current collection state and raises property change notification for WinUI `InfoBar.IsOpen` bindings

#### Scenario: Information message visibility changes when information messages are added or cleared
- **WHEN** the ViewModel information message collection changes from empty to non-empty or non-empty to empty during WinUI workflow operations
- **THEN** `HasInformationMessages` reflects the current collection state and raises property change notification for WinUI `InfoBar.IsOpen` bindings

#### Scenario: Message collection replacement preserves visibility notifications
- **WHEN** either message collection property is replaced with another observable collection
- **THEN** subsequent changes to the replacement collection continue to update the matching message visibility property

### Requirement: WinUI plugin item validation behavior is explicit
The system SHALL define and verify how `PluginListItem` validation behaves in the WinUI shell instead of assuming Avalonia validation behavior carries over.

#### Scenario: Plugin item validation contract is covered
- **WHEN** a `PluginListItem` has an empty or whitespace-only name
- **THEN** the Phase 6 implementation either surfaces the existing validation error through the WinUI workflow or records through tests and documentation that plugin item validation remains a model-level guard rather than a visible WinUI validation surface

#### Scenario: Valid plugin items remain selectable
- **WHEN** a plugin item has a non-empty name and is displayed in the WinUI plugin list
- **THEN** the checkbox binding continues to display the plugin name and update `PluginListItem.IsSelected`

### Requirement: WinUI process button state follows processing state
The system SHALL keep the WinUI process button content consistent with processing start, cancellation, and completion state.

#### Scenario: Processing start exposes cancel action
- **WHEN** a valid WinUI processing run starts
- **THEN** the process button presents the cancel action while `MainWindowViewModel.IsProcessing` is `true`

#### Scenario: Processing cancellation keeps cancel feedback visible
- **WHEN** the process button is clicked during an active processing run
- **THEN** the workflow requests cancellation and reports cancelling progress without leaving the button in the start-action state before processing finishes

#### Scenario: Processing reset restores start action
- **WHEN** a WinUI processing run completes, fails, or observes cancellation
- **THEN** the process button presents the start action after `MainWindowViewModel.IsProcessing` returns to `false`

### Requirement: Debounced plugin filtering marshals UI-bound updates
The system SHALL preserve the existing plugin filtering behavior while ensuring debounced filter application mutates WinUI-bound collections through the configured `IThreadDispatcher`.

#### Scenario: Debounced filtering posts through dispatcher
- **WHEN** `PluginFilter` changes on a ViewModel configured with a non-zero debounce interval and dispatcher access is not currently available
- **THEN** the delayed filter application posts the UI-bound collection update through `IThreadDispatcher` before mutating `FilteredPlugins`

#### Scenario: Debounced filtering preserves selection state
- **WHEN** a selected plugin is hidden and later shown by debounced filter changes
- **THEN** the plugin item remains the same underlying `PluginListItem` instance and preserves its `IsSelected` value

### Requirement: Phase 6 binding and ViewModel details are verified
The system SHALL verify and document Phase 6 binding and ViewModel hardening before Phase 7 test rework begins.

#### Scenario: Automated verification covers Phase 6 guardrails
- **WHEN** Phase 6 verification is run
- **THEN** focused tests cover binding-critical source wiring, message visibility notifications, plugin item validation expectations, process button state, and dispatcher-backed debounced filtering

#### Scenario: Migration plan records Phase 6 checkpoint
- **WHEN** Phase 6 verification is complete
- **THEN** `docs/WinUI-Migration-Plan.md` records the build, test, and WinUI workflow verification results for the binding and ViewModel hardening pass

### Requirement: Phase 7 tests validate UI-neutral behavior through the core path
The system SHALL run service, model, and ViewModel tests for UI-neutral behavior through the `FormID Database Manager.Core` project and standard xUnit execution unless a test explicitly requires a platform UI runtime.

#### Scenario: ViewModel state tests run without Avalonia
- **WHEN** Phase 7 ViewModel tests validate property changes, message visibility, plugin filtering, selection state, progress state, or dispatcher abstraction behavior
- **THEN** they use regular xUnit facts with UI-neutral test dispatchers instead of `AvaloniaFact`

#### Scenario: Service tests reference core behavior
- **WHEN** Phase 7 service tests validate database, processing, game detection, plugin loading, text processing, or load-order behavior
- **THEN** they compile against the shared core project and test utilities without requiring Avalonia UI startup

### Requirement: Avalonia headless test infrastructure is retired after replacement coverage exists
The system SHALL remove Avalonia headless test infrastructure from the automated test project after remaining Avalonia-specific tests have been converted, replaced, explicitly marked manual, or retired.

#### Scenario: Headless package references are removed
- **WHEN** Phase 7 test rework is complete
- **THEN** the test project no longer references `Avalonia.Headless.XUnit` or uses `AvaloniaFact` for automated behavior tests

#### Scenario: Test host files are removed
- **WHEN** Phase 7 test rework is complete
- **THEN** `TestInitialization.cs` and `UiTestHost` are removed from the automated test project unless a documented temporary blocker keeps them for Phase 8

### Requirement: WinUI and platform-service tests replace Avalonia UI service tests
The system SHALL replace tests for Avalonia-specific window, dispatcher, startup, and picker behavior with WinUI-aligned source, smoke, or abstraction-boundary tests.

#### Scenario: Dispatcher coverage targets current dispatchers
- **WHEN** Phase 7 verifies dispatcher behavior
- **THEN** tests cover core dispatcher behavior and WinUI dispatcher access, queueing, awaited invocation, and exception propagation instead of `AvaloniaThreadDispatcher`

#### Scenario: Picker coverage targets file-dialog abstraction
- **WHEN** Phase 7 verifies file and folder picker workflow logic
- **THEN** tests use `IFileDialogService` consumers and mockable picker results for automated coverage, while real picker behavior is documented as smoke or manual verification when needed

#### Scenario: WinUI surface coverage remains migration-focused
- **WHEN** Phase 7 verifies WinUI window wiring
- **THEN** source or smoke tests cover binding-critical and platform-service construction behavior without depending on Avalonia controls or AXAML files

### Requirement: Integration and performance tests remain data-behavior focused
The system SHALL keep integration, performance, load, stress, and regression tests focused on Mutagen, SQLite, processing, and game-environment behavior rather than UI framework mechanics.

#### Scenario: Data tests do not require desktop UI infrastructure
- **WHEN** Phase 7 runs integration and performance-oriented tests
- **THEN** those tests do not require Avalonia headless startup, WinUI window activation, or file picker dialogs unless explicitly marked as manual or environment-specific

### Requirement: Windows CI runs automated migration tests
The system SHALL provide a Windows CI workflow that restores, builds, and runs the automated test suite for the WinUI migration branch.

#### Scenario: CI builds and tests on Windows
- **WHEN** the Windows CI workflow runs on a clean runner
- **THEN** it builds the solution and runs the automated test project using the repository's documented `dotnet` commands or equivalent CI-safe variants

#### Scenario: CI respects environment-specific tests
- **WHEN** tests require installed games, manual desktop interaction, or performance/load/stress execution
- **THEN** the CI workflow excludes, skips, or reports them using the repository's existing test attributes and category conventions

### Requirement: Phase 7 test rework is verified
The system SHALL verify and document Phase 7 test rework before Phase 8 Avalonia removal begins.

#### Scenario: Automated verification is recorded
- **WHEN** Phase 7 verification is complete
- **THEN** durable change verification notes record focused test results, WinUI project build results, full solution build results, full automated test results, and any skipped/manual test rationale

#### Scenario: Remaining Avalonia test references are known
- **WHEN** Phase 7 verification is complete
- **THEN** the migration notes identify that automated test infrastructure is free of Avalonia headless dependencies or records the specific remaining blocker that must be resolved before Phase 8

### Requirement: Legacy Avalonia application is removed from the active solution
The system SHALL remove the legacy Avalonia desktop application from the active buildable solution after WinUI parity and test coverage are stable.

#### Scenario: Solution no longer includes the Avalonia app project
- **WHEN** Phase 8 removal is complete
- **THEN** `FormID Database Manager.slnx` includes the core, WinUI, test, and test-utilities projects without including the legacy `FormID Database Manager` Avalonia project

#### Scenario: Active projects do not reference Avalonia packages
- **WHEN** active project files are inspected after Phase 8 removal
- **THEN** no buildable project references Avalonia runtime, desktop, theme, font, diagnostics, or headless test packages

#### Scenario: Active Avalonia source files are removed
- **WHEN** active source files are inspected after Phase 8 removal
- **THEN** the legacy AXAML views, Avalonia startup files, Avalonia dispatcher, Avalonia picker service, and Avalonia converter are absent from the active source tree

### Requirement: WinUI remains the supported desktop startup path
The system SHALL keep the WinUI shell as the only supported desktop application startup path while preserving UI-neutral behavior in the core project.

#### Scenario: WinUI project remains buildable
- **WHEN** Phase 8 verification builds the WinUI project with an explicit Windows platform
- **THEN** the project compiles successfully using `FormID Database Manager.Core` for shared models, ViewModels, and services

#### Scenario: Full solution builds without the legacy app
- **WHEN** Phase 8 verification builds the solution
- **THEN** the solution compiles without requiring the removed Avalonia project or Avalonia packages

#### Scenario: Core remains UI-neutral
- **WHEN** the core project is inspected after Phase 8 removal
- **THEN** it contains no WinUI or Avalonia source dependencies and continues to expose UI-neutral abstractions for dispatcher and file-dialog behavior

### Requirement: Current tests and guidance do not depend on Avalonia
The system SHALL remove Avalonia assumptions from active automated tests and current developer guidance after the WinUI shell becomes the supported application.

#### Scenario: Automated tests have no Avalonia dependency
- **WHEN** test project references and active test sources are inspected after Phase 8 removal
- **THEN** they do not reference `Avalonia`, `Avalonia.Headless`, `AvaloniaFact`, AXAML test hosts, or legacy Avalonia platform services

#### Scenario: Current developer guidance names WinUI as the app
- **WHEN** current build, run, and architecture guidance is inspected after Phase 8 removal
- **THEN** it describes the WinUI project as the desktop application and does not instruct developers to run or publish the removed Avalonia project

#### Scenario: Historical references are classified
- **WHEN** remaining `Avalonia`, `axaml`, `AvaloniaFact`, or `Headless` text matches are reviewed after Phase 8 removal
- **THEN** each remaining match is either removed from active guidance/source/test/project files or recorded as an intentionally historical reference in docs or archived OpenSpec material

### Requirement: Phase 8 Avalonia removal is verified
The system SHALL verify and document Phase 8 removal before deployment and publish work begins.

#### Scenario: Reference cleanup is verified
- **WHEN** Phase 8 verification is run
- **THEN** focused searches for `Avalonia`, `axaml`, `AvaloniaFact`, and `Headless` report no active source, test, or project dependency on Avalonia

#### Scenario: Automated verification remains green
- **WHEN** Phase 8 verification is run
- **THEN** the WinUI project build, full solution build, and current automated test suite pass or report skipped/manual tests using the existing project conventions

#### Scenario: Migration plan records Phase 8 checkpoint
- **WHEN** Phase 8 verification is complete
- **THEN** `docs/WinUI-Migration-Plan.md` records the removal scope, reference-search results, build results, test results, and any remaining historical Avalonia references
