## Purpose

Define supported WinUI release publishing lanes and writable default database output behavior.
## Requirements
### Requirement: WinUI packaged release lane is explicit
The system SHALL provide a documented or profiled packaged release lane for `FormID Database Manager.WinUI` that produces MSIX or MSIX bundle output without disabling packaged capability in the base WinUI project.

#### Scenario: Packaged publish does not disable MSIX globally
- **WHEN** the WinUI project and packaged publish lane are inspected
- **THEN** `WindowsPackageType=None` is not set globally in the base WinUI project configuration
- **AND** the packaged lane remains capable of producing MSIX or MSIX bundle output

#### Scenario: Packaged distribution decision is documented
- **WHEN** release publishing documentation is reviewed
- **THEN** it identifies whether the packaged lane targets Store, AppInstaller, or direct MSIX distribution
- **AND** it documents the signing and update-flow status for the selected channel

### Requirement: WinUI unpackaged release lanes are explicit
The system SHALL provide documented or profiled unpackaged release lanes for `FormID Database Manager.WinUI` that scope unpackaged settings to the unpackaged publish path.

#### Scenario: Unpackaged framework-dependent output is available or documented
- **WHEN** release publishing documentation is reviewed
- **THEN** it includes an unpackaged framework-dependent command or profile, or explicitly records why that lane is not selected
- **AND** it states that the Windows App SDK runtime must be installed separately when required

#### Scenario: Unpackaged self-contained output is available or documented
- **WHEN** release publishing documentation is reviewed
- **THEN** it includes an unpackaged self-contained command or profile, or explicitly records why that lane is not selected
- **AND** it states whether the Windows App SDK runtime is carried with the app output

#### Scenario: Unpackaged settings are scoped
- **WHEN** unpackaged publish configuration is inspected
- **THEN** `WindowsPackageType=None` is applied only by the unpackaged profile or command
- **AND** packaged publishing remains available from the base WinUI project

#### Scenario: Single-file output is opt-in and verified
- **WHEN** release documentation mentions `PublishSingleFile`
- **THEN** it also includes the required self-contained and self-extract settings for the active Windows App SDK version
- **AND** it records verification of first-launch extraction behavior

### Requirement: Default database output uses a writable location
The system SHALL generate a user-writable default database path when processing starts without an explicit database path.

#### Scenario: Missing database path avoids system directories
- **WHEN** WinUI processing starts with a selected game and an empty `DatabasePath`
- **THEN** the generated database path is under a user-writable application data location rather than `C:\WINDOWS\system32` or the process current directory
- **AND** the generated filename uses `GameReleaseHelper.GetSafeTableName(selectedGame)` with a `.db` extension

#### Scenario: Generated database directory exists before processing
- **WHEN** WinUI processing starts with a generated default database path
- **THEN** the containing directory exists before `PluginProcessingService.ProcessPlugins` is called
- **AND** the resolved path is assigned back to `MainWindowViewModel.DatabasePath`

### Requirement: Release documentation covers build, run, publish, and verification
The system SHALL update project documentation with current WinUI build, run, publish, runtime, and verification guidance.

#### Scenario: README lists supported publish commands
- **WHEN** the README publish section is reviewed
- **THEN** it includes the supported packaged and unpackaged WinUI publish commands or profile names
- **AND** it identifies runtime prerequisites for each release lane

#### Scenario: Migration plan records phase 9 verification
- **WHEN** Phase 9 implementation is complete
- **THEN** `docs/WinUI-Migration-Plan.md` records build, test, publish, and clean-machine or VM verification results for the selected release lanes

### Requirement: Release outputs are verified before completion
The system SHALL verify the selected packaged and unpackaged release outputs before Phase 9 is treated as complete.

#### Scenario: Automated verification remains green
- **WHEN** Phase 9 verification is run
- **THEN** the WinUI project build, solution build, and current automated test suite pass or report skipped/manual tests using existing project conventions

#### Scenario: Packaged output is installable or has a recorded blocker
- **WHEN** packaged release verification is performed on a clean Windows machine or VM
- **THEN** the produced MSIX or MSIX bundle installs and launches successfully
- **OR** the migration plan records the exact signing, installation, or environment blocker preventing completion

#### Scenario: Unpackaged output launches or has a recorded blocker
- **WHEN** unpackaged release verification is performed on a clean Windows machine or VM
- **THEN** the produced unpackaged output launches successfully with its documented runtime prerequisites satisfied
- **OR** the migration plan records the exact runtime, launch, or environment blocker preventing completion
