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
The system SHALL reference `FormID Database Manager.Core` from the WinUI shell so later phases can bind the Windows UI to the extracted models, ViewModels, services, and abstractions.

#### Scenario: Core project reference is available
- **WHEN** the WinUI shell project is built
- **THEN** it restores and compiles with a project reference to `FormID Database Manager.Core`

#### Scenario: Production UI porting is deferred
- **WHEN** the Phase 2 WinUI shell is reviewed
- **THEN** it does not port `MainWindow.axaml`, WinUI file pickers, WinUI dispatcher services, or production processing workflows yet

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

