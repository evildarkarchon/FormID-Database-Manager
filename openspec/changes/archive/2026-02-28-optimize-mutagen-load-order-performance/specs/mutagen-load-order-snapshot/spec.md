## ADDED Requirements

### Requirement: Load-order metadata is built from lightweight listings
The system SHALL build plugin load-order metadata for refresh and processing startup from Mutagen load-order listing APIs, without constructing a full `GameEnvironment` solely for listing or membership checks.

#### Scenario: Plugin list refresh uses listing-based metadata
- **WHEN** `PluginListManager.RefreshPluginList` loads plugins for a detected game directory
- **THEN** it derives candidate plugin names from listing metadata
- **AND** it does not require `GameEnvironment.Typical.Builder(...).Build()` to obtain those names

#### Scenario: Processing startup uses listing-based metadata
- **WHEN** `PluginProcessingService.ProcessPlugins` starts plugin processing mode
- **THEN** it derives load-order membership from listing metadata
- **AND** it does not construct a second full environment just to create a membership dictionary

### Requirement: Processing uses a run-scoped immutable load-order snapshot
The system SHALL capture one load-order snapshot at the start of a processing run and use that snapshot for all plugin membership checks in that run.

#### Scenario: Membership checks use run snapshot
- **WHEN** multiple selected plugins are processed in one run
- **THEN** each plugin membership check is evaluated against the same snapshot instance

#### Scenario: Mid-run external load-order changes do not alter active run behavior
- **WHEN** load-order files change while a processing run is already executing
- **THEN** the active run continues using its start-of-run snapshot
- **AND** updated listings are only considered on a subsequent refresh or processing run

### Requirement: Snapshot generation preserves existing missing-file filtering behavior
The system SHALL keep existing behavior that excludes listed plugins whose files do not exist in the resolved data path during plugin list presentation.

#### Scenario: Missing listed plugin file is excluded from UI list
- **WHEN** listing metadata contains `MissingPlugin.esp` but the file is not present in the data folder
- **THEN** `PluginListManager` excludes `MissingPlugin.esp` from the displayed plugin collection
