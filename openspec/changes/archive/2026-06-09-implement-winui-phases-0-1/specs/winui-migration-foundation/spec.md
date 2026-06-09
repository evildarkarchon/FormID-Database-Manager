## ADDED Requirements

### Requirement: Phase 0 baseline is recorded
The system SHALL record the current migration branch, build baseline, test baseline, WinUI template availability, and selected deployment model before Phase 1 code movement is treated as complete.

#### Scenario: Baseline record is available to future migration phases
- **WHEN** a developer reviews the migration documentation after Phase 0
- **THEN** the documentation identifies the current branch/checkpoint, baseline build result, baseline test result, WinUI template status, and packaged MSIX as the selected target deployment model for later WinUI phases

### Requirement: UI-neutral core owns reusable application behavior
The system SHALL provide a `.NET 10` core class library containing UI-neutral models, ViewModels, services, and abstractions needed by both the existing Avalonia app and the future WinUI app.

#### Scenario: Core project builds without Avalonia references
- **WHEN** the solution is built after Phase 1 extraction
- **THEN** the core project compiles without Avalonia package references or Avalonia-specific source files

#### Scenario: Current application consumes core behavior
- **WHEN** the existing Avalonia app builds after Phase 1 extraction
- **THEN** it references the core project for reusable processing, game detection, database, load-order, model, and ViewModel code while keeping Avalonia startup and platform services in the app project

### Requirement: Platform file dialogs are abstracted for future WinUI implementation
The system SHALL define a core file-dialog abstraction for selecting the game directory, database output path, and optional FormID text file without depending on a specific UI framework.

#### Scenario: File dialog contract is UI-neutral
- **WHEN** a future WinUI picker service is added
- **THEN** it can implement the same core abstraction used by the current UI-facing code for game directory, database save path, and FormID list file selection

### Requirement: Existing processing behavior remains unchanged
The system SHALL preserve existing Mutagen parsing, SQLite writing, plugin loading, game detection, filtering, progress, and cancellation behavior during Phase 1 extraction.

#### Scenario: Existing tests remain the behavior guardrail
- **WHEN** the full test suite is run after Phase 1 extraction
- **THEN** UI-independent service and ViewModel tests continue to validate the same behavior through the core project reference
