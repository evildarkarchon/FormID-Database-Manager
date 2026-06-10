## ADDED Requirements

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
