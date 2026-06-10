## ADDED Requirements

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
