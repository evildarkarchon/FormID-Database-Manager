## 1. GameReleaseHelper (foundation — unblocks everything else)

- [x] 1.1 Create `Services/GameReleaseHelper.cs` with `internal static string GetSafeTableName(GameRelease release)` containing all supported releases (no `OblivionRE`); default throws `ArgumentException`
- [x] 1.2 Add `internal static string ResolveDataPath(string gameDirectory)` to `GameReleaseHelper`
- [x] 1.3 Replace `GetSafeTableName` in `DatabaseService` with a call to `GameReleaseHelper.GetSafeTableName`; remove the private copy; remove `OblivionRE` entry
- [x] 1.4 Replace `GetSafeTableName` in `ModProcessor` with a call to `GameReleaseHelper.GetSafeTableName`; remove the private copy
- [x] 1.5 Replace `GetSafeTableName` in `FormIdTextProcessor.BatchInserter` with a call to `GameReleaseHelper.GetSafeTableName`; remove the private copy
- [x] 1.6 Replace the `dataPath` calculation in `PluginListManager` with `GameReleaseHelper.ResolveDataPath(gameDirectory)`
- [x] 1.7 Replace the `dataPath` calculation in `ModProcessor.ProcessPlugin` with `GameReleaseHelper.ResolveDataPath(gameDir)`
- [x] 1.8 Write unit tests for `GameReleaseHelper.GetSafeTableName` covering all supported releases and the unsupported (`OblivionRE`) throw case
- [x] 1.9 Write unit tests for `GameReleaseHelper.ResolveDataPath` covering the `Data`-suffix and root-directory cases

## 2. Extend Game Release Coverage in ModProcessor

- [x] 2.1 Add `GameRelease.SkyrimLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath, SkyrimRelease.SkyrimLE)` to the overlay switch
- [x] 2.2 Add `GameRelease.EnderalLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath, SkyrimRelease.EnderalLE)` to the overlay switch
- [x] 2.3 Add `GameRelease.EnderalSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath, SkyrimRelease.EnderalSE)` to the overlay switch
- [x] 2.4 Add `GameRelease.Fallout4VR => Fallout4Mod.CreateFromBinaryOverlay(pluginPath, Fallout4Release.Fallout4VR)` to the overlay switch

## 3. Mutagen Resource Disposal

- [x] 3.1 Change `IModGetter mod = gameRelease switch { ... }` to `using IModDisposeGetter mod = gameRelease switch { ... }` in `ModProcessor.ProcessPlugin`
- [x] 3.2 Add `using var env = ...` to the `GameEnvironment.Typical.Builder(...)` call in `PluginListManager.RefreshPluginList` (inside the `Task.Run` lambda, after `ToList()`)
- [x] 3.3 Verify `using var env` placement: `env` must be disposed after `ToList()` materialises the load order but before the lambda returns

## 4. GameEnvironment Correctness in PluginProcessingService

- [x] 4.1 Replace `GameEnvironment.Typical.Construct(parameters.GameRelease)` with `GameEnvironment.Typical.Builder(parameters.GameRelease).WithTargetDataFolder(dataPath).Build()`
- [x] 4.2 Resolve `dataPath` via `GameReleaseHelper.ResolveDataPath(parameters.GameDirectory!)` before building the environment
- [x] 4.3 Wrap the environment build in `using` and materialise `loadOrder` via `.ToList()` before the `using` block exits
- [x] 4.4 Build the load order dictionary once before the plugin loop: `var loadOrderDict = loadOrder.ToDictionary(l => l.ModKey.FileName, StringComparer.OrdinalIgnoreCase)` (or equivalent, handling duplicates)
- [x] 4.5 Update the `_modProcessor.ProcessPlugin(...)` call to pass `loadOrderDict` (`IReadOnlyDictionary<string, IModListingGetter<IModGetter>>`) instead of `loadOrder`

## 5. ModProcessor Signature and Async Cleanup

- [x] 5.1 Change `ModProcessor.ProcessPlugin` parameter from `IList<IModListingGetter<IModGetter>> loadOrder` to `IReadOnlyDictionary<string, IModListingGetter<IModGetter>> loadOrderDict`
- [x] 5.2 Remove the per-call `loadOrderDict` construction inside `ProcessPlugin` (now done by caller)
- [x] 5.3 Rename `ProcessModRecordsAsync` to `ProcessModRecords`, change return type from `Task` to `void`, remove `return Task.CompletedTask`
- [x] 5.4 Update the call site: replace `await ProcessModRecordsAsync(...).ConfigureAwait(false)` with a direct `ProcessModRecords(...)` call
- [x] 5.5 Fix transaction placement: move `transaction = conn.BeginTransaction()` to after the `plugin == null` and `!File.Exists` guard checks (avoids opening a transaction that is never committed for invalid plugins)
- [x] 5.6 Update `ModProcessor` tests to pass `IReadOnlyDictionary` at call sites

## 6. SQLite Connection and WAL Configuration

- [x] 6.1 Change `Cache = SqliteCacheMode.Shared` to `Cache = SqliteCacheMode.Default` in `DatabaseService.GetOptimizedConnectionString`
- [x] 6.2 Remove `PRAGMA page_size = 4096` from the multi-statement string in `ConfigureConnection`
- [x] 6.3 Add `PRAGMA mmap_size = 268435456;` to the `ConfigureConnection` PRAGMA block
- [x] 6.4 Replace `VACUUM` in `OptimizeDatabase` with `PRAGMA wal_checkpoint(TRUNCATE); PRAGMA optimize;`

## 7. GameDetectionService — Extended Detection

- [x] 7.1 Add Enderal detection inside the `Skyrim.esm` branch: before the GOG/VR/SE checks, test for `Enderal - Forgotten Stories.esm` in the data directory
- [x] 7.2 If Enderal master found and `SkyrimSE.exe` present in the game root → return `GameRelease.EnderalSE`
- [x] 7.3 If Enderal master found and `TESV.exe` present (no `SkyrimSE.exe`) → return `GameRelease.EnderalLE`
- [x] 7.4 After GOG/VR checks, add: if `SkyrimSE.exe` present → `SkyrimSE` (existing), else → `SkyrimLE` (new fallthrough)
- [x] 7.5 Apply the same detection tree to the game-root branch (the `else` path that appends `"Data"` and re-enters)
- [x] 7.6 Write unit tests for `GameDetectionService` covering `SkyrimLE`, `EnderalLE`, `EnderalSE` detection scenarios (using `GameDetectionBuilder` from `TestUtilities`)

## 8. ViewModel and Window Lifecycle

- [x] 8.1 In `MainWindowViewModel.DebounceApplyFilter`, add `_debounceCts?.Dispose();` after `_debounceCts?.Cancel();` and before reassigning
- [x] 8.2 Implement `IDisposable` on `MainWindowViewModel`: dispose `_debounceCts` in `Dispose()`
- [x] 8.3 In `MainWindow` constructor, add `this.Closed += (_, _) => Dispose();`
- [x] 8.4 Simplify `MainWindow.ProcessFormIdsAsync`: replace `await Task.Run(async () => { await _pluginProcessingService.ProcessPlugins(...).ConfigureAwait(false); }).ConfigureAwait(true)` with `await _pluginProcessingService.ProcessPlugins(parameters, progress).ConfigureAwait(false)` — note: keep `ConfigureAwait(false)` only if `processButton.Content` assignment is moved into the ViewModel; otherwise use no `ConfigureAwait` to remain on the UI thread for the `finally` block

## 9. Build Verification and Test Pass

- [x] 9.1 Run `dotnet build "FormID Database Manager.slnx"` — confirm zero warnings and errors
- [x] 9.2 Run `dotnet test "FormID Database Manager.Tests"` — confirm all tests pass
- [x] 9.3 Verify no remaining private `GetSafeTableName` methods exist: `grep -r "GetSafeTableName" --include="*.cs"` should show only `GameReleaseHelper.cs` and call sites
- [x] 9.4 Verify no remaining `dataPath` duplication: `grep -r "GetFileName.*Data" --include="*.cs"` should show only `GameReleaseHelper.cs`
