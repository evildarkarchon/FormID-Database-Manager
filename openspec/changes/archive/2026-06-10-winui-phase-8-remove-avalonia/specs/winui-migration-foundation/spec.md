## ADDED Requirements

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
