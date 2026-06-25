## Purpose

Define supported WinUI release publishing lanes and writable default database output behavior.
## Requirements
### Requirement: WinUI project is unpackaged self-contained by default
The system SHALL configure `FormID Database Manager.WinUI` as an unpackaged, Windows App SDK self-contained app by default and SHALL NOT keep MSIX tooling or package manifests in the base project.

#### Scenario: Base project opts into portable unpackaged execution
- **WHEN** the WinUI project is inspected
- **THEN** `WindowsPackageType=None` is set in the base WinUI project configuration
- **AND** `WindowsAppSDKSelfContained=true` is set in the base WinUI project configuration
- **AND** `EnableMsixTooling` is not set in the base WinUI project configuration
- **AND** the project does not include an MSIX `ProjectCapability`

#### Scenario: MSIX artifacts are absent
- **WHEN** the WinUI project contents are inspected
- **THEN** `Package.appxmanifest` does not exist
- **AND** MSIX logo assets are not included by the project
- **AND** no MSIX publish profile is present

### Requirement: WinUI unpackaged release lane is explicit
The system SHALL provide a documented or profiled unpackaged release lane for `FormID Database Manager.WinUI` that supports a primary portable self-contained output without MSIX tooling.

#### Scenario: Framework-dependent output is removed
- **WHEN** release publishing documentation is reviewed
- **THEN** it records that the framework-dependent lane is not selected because it requires separately installed .NET and Windows App SDK runtimes
- **AND** no framework-dependent publish profile is present

#### Scenario: Self-contained output is the primary release lane
- **WHEN** release publishing documentation is reviewed
- **THEN** it includes the `win-x64-unpackaged-self-contained.pubxml` publish profile or portable pack script
- **AND** it states that the .NET and Windows App SDK runtimes are carried with the app output
- **AND** it states that the portable output runs without an installer

#### Scenario: Single-file output is omitted
- **WHEN** release publishing documentation is reviewed
- **THEN** it records that single-file output is intentionally not provided because the WinUI single-file lane requires MSIX tooling
- **AND** no single-file publish profile is present

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
- **THEN** it includes the portable pack script or the unpackaged self-contained WinUI publish command
- **AND** it identifies that portable output carries required runtimes with the app

#### Scenario: Migration plan records phase 9 verification
- **WHEN** Phase 9 implementation is complete
- **THEN** `docs/WinUI-Migration-Plan.md` records build, test, publish, and clean-machine or VM verification results for the selected release lanes

### Requirement: Release outputs are verified before completion
The system SHALL verify the selected portable release outputs before the release publishing work is treated as complete.

#### Scenario: Automated verification remains green
- **WHEN** Phase 9 verification is run
- **THEN** the WinUI project build, solution build, and current automated test suite pass or report skipped/manual tests using existing project conventions

#### Scenario: Portable output launches or has a recorded blocker
- **WHEN** portable self-contained release verification is performed on a clean Windows machine or VM
- **THEN** the produced unpackaged output launches successfully without separate runtime installation
- **OR** the migration plan records the exact runtime, launch, or environment blocker preventing completion

#### Scenario: Portable zip artifact is produced
- **WHEN** the portable pack script or Windows CI workflow runs
- **THEN** it produces `publish/FormID-Database-Manager-portable-win-x64.zip`
- **AND** the artifact contains the self-contained unpackaged publish output
