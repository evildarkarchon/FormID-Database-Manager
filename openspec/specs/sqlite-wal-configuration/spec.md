## Purpose

Define correct SQLite and Mutagen resource handling behavior for WAL-mode processing sessions.

## Requirements

### Requirement: SQLite connections use private cache mode
The system SHALL configure SQLite connections with `SqliteCacheMode.Default` (private cache per connection). `SqliteCacheMode.Shared` SHALL NOT be used.

#### Scenario: Connection string uses Default cache mode
- **WHEN** `DatabaseService.GetOptimizedConnectionString` is called
- **THEN** the resulting connection string specifies private cache (not `cache=shared`)

---

### Requirement: ConfigureConnection does not set page_size
The system SHALL NOT issue `PRAGMA page_size` in `ConfigureConnection`. The `page_size` PRAGMA is a no-op on any database that has already been written to and SHALL be omitted.

#### Scenario: ConfigureConnection pragmas are valid and effective
- **WHEN** `ConfigureConnection` is called on an existing database connection
- **THEN** it sets `journal_mode`, `synchronous`, `cache_size`, `mmap_size`, and `temp_store` without issuing `page_size`

---

### Requirement: ConfigureConnection sets memory-mapped I/O size
The system SHALL configure `PRAGMA mmap_size = 268435456` (256 MB) in `ConfigureConnection` to enable memory-mapped reads for large databases.

#### Scenario: mmap_size is applied
- **WHEN** `ConfigureConnection` is called on an open connection
- **THEN** executing `PRAGMA mmap_size` on that connection returns a value greater than zero

---

### Requirement: End-of-session optimisation uses WAL checkpoint and query planner update
`DatabaseService.OptimizeDatabase` SHALL execute `PRAGMA wal_checkpoint(TRUNCATE)` followed by `PRAGMA optimize`. It SHALL NOT execute `VACUUM`.

#### Scenario: OptimizeDatabase checkpoints and optimises
- **WHEN** `OptimizeDatabase` is called on an open WAL-mode connection
- **THEN** the WAL file is checkpointed (all frames written back to main DB file) and query planner statistics are updated

#### Scenario: OptimizeDatabase does not perform a full rewrite
- **WHEN** `OptimizeDatabase` is called
- **THEN** it does not execute `VACUUM` (which would rewrite the entire database file)

---

### Requirement: Mutagen binary overlays are disposed after each plugin
The system SHALL declare the result of `CreateFromBinaryOverlay` as `IModDisposeGetter` and wrap it in a `using` statement. The overlay's underlying stream SHALL be released before processing the next plugin.

#### Scenario: Overlay disposed after successful processing
- **WHEN** `ModProcessor.ProcessPlugin` completes successfully
- **THEN** the `IModDisposeGetter` overlay is disposed, releasing its `IBinaryReadStream`

#### Scenario: Overlay disposed after processing failure
- **WHEN** `ModProcessor.ProcessPlugin` throws during record enumeration
- **THEN** the `IModDisposeGetter` overlay is still disposed before the exception propagates

---

### Requirement: GameEnvironment is disposed after load order materialisation
The system SHALL dispose the `IGameEnvironment` returned by `GameEnvironment.Typical.Builder(...).Build()` immediately after calling `.ToList()` on the load order. In `PluginListManager` this occurs inside the `Task.Run` lambda; in `PluginProcessingService` this occurs before the plugin processing loop begins.

#### Scenario: GameEnvironment disposed in PluginListManager
- **WHEN** `PluginListManager.RefreshPluginList` completes its background scan
- **THEN** the `IGameEnvironment` instance has been disposed, releasing load order file handles and link cache memory

#### Scenario: GameEnvironment disposed in PluginProcessingService
- **WHEN** `PluginProcessingService.ProcessPlugins` begins the plugin processing loop
- **THEN** the `IGameEnvironment` used to build the load order has already been disposed
