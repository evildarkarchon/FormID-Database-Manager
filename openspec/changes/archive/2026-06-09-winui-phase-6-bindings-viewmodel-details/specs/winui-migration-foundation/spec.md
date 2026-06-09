## ADDED Requirements

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
