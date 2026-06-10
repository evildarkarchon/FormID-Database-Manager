## MODIFIED Requirements

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

## ADDED Requirements

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
