## Why

The codebase contains a cluster of correctness bugs, resource leaks, and async anti-patterns introduced during initial AI-assisted development. Several Mutagen and SQLite resources are never disposed (leaking file handles and memory per processing session), certain game releases silently throw at processing time despite the DB schema supporting them, and the SQLite connection pool is misconfigured for WAL mode. These issues compound in normal use: a user processing 200 plugins leaks 200 file handles, and users of SkyrimLE, EnderalLE, EnderalSE, or Fallout4VR cannot process plugins at all.

## What Changes

- **New shared helper** `GameReleaseHelper` consolidates `GetSafeTableName` (currently copy-pasted across three files with diverged content) and `ResolveDataPath` (duplicated in two services) into a single internal static class
- **Game release coverage** extended in `ModProcessor`'s `CreateFromBinaryOverlay` switch to include `SkyrimLE`, `EnderalLE`, `EnderalSE`, and `Fallout4VR`; `OblivionRE` removed from `DatabaseService.GetSafeTableName` (no end-to-end support)
- **Game detection** extended in `GameDetectionService` to distinguish `SkyrimLE` from `SkyrimSE`, and to detect `EnderalLE` and `EnderalSE`
- **Mutagen resource disposal**: `IModDisposeGetter` (binary overlay) now wrapped in `using` per plugin; `IGameEnvironment` disposed after load order materialisation in both `PluginListManager` and `PluginProcessingService`
- **GameEnvironment correctness**: `PluginProcessingService` now builds the environment with `WithTargetDataFolder` using the user-specified directory (was using registry/default path)
- **Load order dict** built once before the plugin loop in `PluginProcessingService`, not per-plugin inside `ModProcessor`
- **`_debounceCts` dispose** added before reassignment in `MainWindowViewModel.DebounceApplyFilter`
- **`MainWindow.Dispose()`** hooked to the `Closed` event so the `CancellationTokenSource` in `PluginProcessingService` is released on window close
- **`ProcessModRecordsAsync`** converted to a synchronous `void` method (was falsely declared `Task`, returned `Task.CompletedTask`, did all work synchronously)
- **`Task.Run` wrapper** in `MainWindow.ProcessFormIdsAsync` simplified; direct `await` without the redundant outer wrap
- **SQLite connection** pool fixed: `SqliteCacheMode.Shared` → `Default` (incompatible with WAL), dead `page_size` PRAGMA removed, `mmap_size` added
- **`OptimizeDatabase`** replaces `VACUUM` with `PRAGMA wal_checkpoint(TRUNCATE)` + `PRAGMA optimize` (appropriate for WAL; VACUUM rewrites the entire DB file unnecessarily)

## Capabilities

### New Capabilities

- `game-release-support`: Defines the supported game release matrix and the consistency requirement that a release must be fully supported end-to-end (detection → table name → Mutagen overlay) or explicitly unsupported everywhere
- `sqlite-wal-configuration`: Defines correct SQLite connection configuration for WAL mode including cache mode, memory-mapped I/O, and end-of-session optimisation behaviour

### Modified Capabilities

_(none — no existing specs)_

## Impact

- `Services/DatabaseService.cs` — remove `OblivionRE` from `GetSafeTableName`, remove `page_size` PRAGMA, fix `SqliteCacheMode`, update `OptimizeDatabase`
- `Services/ModProcessor.cs` — remove `GetSafeTableName` (use helper), remove per-call dict build, add missing game releases to overlay switch, fix `IModDisposeGetter` disposal, convert `ProcessModRecordsAsync` to sync void, fix transaction early-return pattern
- `Services/FormIdTextProcessor.cs` — remove `GetSafeTableName` from `BatchInserter` (use helper)
- `Services/PluginProcessingService.cs` — fix `GameEnvironment` construction (add `WithTargetDataFolder`), dispose `GameEnvironment`, build load order dict once, update `ModProcessor` call signature
- `Services/PluginListManager.cs` — dispose `GameEnvironment`, remove `dataPath` duplication (use helper)
- `Services/GameDetectionService.cs` — add `SkyrimLE`, `EnderalLE`, `EnderalSE` detection
- `ViewModels/MainWindowViewModel.cs` — fix `_debounceCts` dispose, add `IDisposable`
- `MainWindow.axaml.cs` — hook `Closed` → `Dispose()`, simplify `Task.Run` wrapper
- **New file**: `Services/GameReleaseHelper.cs` — `GetSafeTableName`, `ResolveDataPath`
- No changes to public APIs, data formats, or NuGet dependencies
