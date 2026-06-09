## ADDED Requirements

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
