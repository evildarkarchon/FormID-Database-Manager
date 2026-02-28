## Purpose

Define separated-master overlay read behavior so runtime processing passes correct master-style lookup data where required.

## Requirements

### Requirement: Separated-master releases provide master-style lookup for overlay reads
For game releases that use separated master load orders, the system SHALL construct `BinaryReadParameters` with `MasterFlagsLookup` from the run's load-order snapshot and pass those parameters to plugin overlay reads.

#### Scenario: Starfield overlay read receives master lookup
- **WHEN** `ModProcessor.ProcessPlugin` opens a plugin for `GameRelease.Starfield`
- **THEN** overlay creation receives `BinaryReadParameters` containing a non-null `MasterFlagsLookup` built from listed master-style metadata

#### Scenario: Lookup is reused across plugins in the same run
- **WHEN** a processing run handles multiple Starfield plugins
- **THEN** each overlay read reuses the run-scoped master-style lookup derived at startup

---

### Requirement: Non-separated-master releases remain compatible with existing overlay reads
For game releases without separated master load orders, the system SHALL continue to open overlays successfully without requiring a separated-master lookup.

#### Scenario: SkyrimSE overlay read works without separated lookup
- **WHEN** `ModProcessor.ProcessPlugin` opens a plugin for `GameRelease.SkyrimSE`
- **THEN** overlay creation succeeds using read parameters compatible with non-separated-master behavior

---

### Requirement: Missing load-order membership still surfaces user-visible warning
If a selected plugin cannot be matched in the processing run's load-order snapshot, the system SHALL emit the existing warning path and skip processing that plugin.

#### Scenario: Selected plugin absent from snapshot
- **WHEN** a selected plugin name is not present in load-order snapshot membership
- **THEN** processing reports a warning that the plugin could not be found in load order
- **AND** that plugin is skipped without terminating the full run
