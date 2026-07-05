using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using FormID_Database_Manager.Models;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Provides functionality to process mod plugin files, updating them in a database while handling errors and
///     supporting cancellation.
/// </summary>
public class ModProcessor(Action<string> errorCallback)
{
    // HashSet for O(1) lookups of error patterns to ignore
    private static readonly HashSet<string> IgnorableErrorPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "KSIZ",
        "KWDA",
        "Expected EDID",
        "List with a non zero counter",
        "Unexpected record type",
        "Failed to parse record header",
        "Object reference not set to an instance"
    };

    // Cache reflection lookups to avoid O(n×m) complexity
    // Reflection is 10-100x slower than direct access - this reduces to O(1) per type
    private static readonly ConcurrentDictionary<Type, Func<IMajorRecordGetter, string?>> NameExtractorCache = new();

    /// <summary>
    ///     Processes the specified plugin file, updates the database with its entries,
    ///     and provides error handling during processing.
    /// </summary>
    /// <param name="gameDir">The root directory of the game where the plugin resides.</param>
    /// <param name="recordStore">The run-scoped FormID Record Store used for plugin writes.</param>
    /// <param name="gameRelease">The specific game release associated with the plugin.</param>
    /// <param name="pluginItem">The plugin to be processed, represented as a list item.</param>
    /// <param name="loadOrderDict">The load order listings used for membership and read parameter metadata.</param>
    /// <param name="updateMode">Indicates if the plugin entries should be updated in the database.</param>
    /// <param name="cancellationToken">The cancellation token to handle processing termination requests.</param>
    /// <returns>A task that processes the plugin asynchronously and manages its database entries accordingly.</returns>
    [RequiresUnreferencedCode(
        "Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types for name extraction.")]
    public async Task ProcessPlugin(
        string gameDir,
        FormIdRecordStore recordStore,
        GameRelease gameRelease,
        PluginListItem pluginItem,
        IReadOnlyDictionary<string, IModListingGetter<IModGetter>> loadOrderDict,
        bool updateMode,
        CancellationToken cancellationToken)
    {
        var listedPluginNames = loadOrderDict.Keys.ToList();
        var masterStyles = loadOrderDict.Values
            .Select(listing => listing.Mod)
            .OfType<IModMasterStyledGetter>()
            .ToList();
        var snapshot = masterStyles.Count > 0
            ? new GameLoadOrderSnapshot(listedPluginNames, masterStyles)
            : new GameLoadOrderSnapshot(listedPluginNames);

        await ProcessPlugin(
            gameDir,
            recordStore,
            gameRelease,
            pluginItem.Name,
            snapshot,
            ToStoreUpdateMode(updateMode),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Processes the specified plugin file, updates the database with its entries,
    ///     and provides error handling during processing.
    /// </summary>
    /// <param name="gameDir">The root directory of the game where the plugin resides.</param>
    /// <param name="recordStore">The run-scoped FormID Record Store used for plugin writes.</param>
    /// <param name="gameRelease">The specific game release associated with the plugin.</param>
    /// <param name="pluginItem">The plugin to be processed, represented as a list item.</param>
    /// <param name="loadOrderSnapshot">The run-scoped load order snapshot for membership and read parameters.</param>
    /// <param name="updateMode">Indicates if the plugin entries should be updated in the database.</param>
    /// <param name="cancellationToken">The cancellation token to handle processing termination requests.</param>
    /// <returns>A task that processes the plugin asynchronously and manages its database entries accordingly.</returns>
    [RequiresUnreferencedCode(
        "Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types for name extraction.")]
    public async Task ProcessPlugin(
        string gameDir,
        FormIdRecordStore recordStore,
        GameRelease gameRelease,
        PluginListItem pluginItem,
        GameLoadOrderSnapshot loadOrderSnapshot,
        bool updateMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pluginItem);

        await ProcessPlugin(
            gameDir,
            recordStore,
            gameRelease,
            pluginItem.Name,
            loadOrderSnapshot,
            ToStoreUpdateMode(updateMode),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Processes the specified plugin file, updates the database with its entries,
    ///     and provides error handling during processing.
    /// </summary>
    /// <param name="gameDir">The root directory of the game where the plugin resides.</param>
    /// <param name="recordStore">The run-scoped FormID Record Store used for plugin writes.</param>
    /// <param name="gameRelease">The specific game release associated with the plugin.</param>
    /// <param name="pluginName">The Plugin name to process.</param>
    /// <param name="loadOrderSnapshot">The run-scoped load order snapshot for membership and read parameters.</param>
    /// <param name="updateMode">Controls whether rows are appended or replace existing rows for the Plugin.</param>
    /// <param name="cancellationToken">The cancellation token to handle processing termination requests.</param>
    /// <returns>A task that processes the plugin asynchronously and manages its database entries accordingly.</returns>
    [RequiresUnreferencedCode(
        "Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types for name extraction.")]
    public async Task ProcessPlugin(
        string gameDir,
        FormIdRecordStore recordStore,
        GameRelease gameRelease,
        string pluginName,
        GameLoadOrderSnapshot loadOrderSnapshot,
        UpdateMode updateMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        if (!loadOrderSnapshot.ContainsPlugin(pluginName))
        {
            errorCallback($"Warning: Could not find plugin in load order: {pluginName}");
            return;
        }

        var dataPath = GameReleaseHelper.ResolveDataPath(gameDir);

        var pluginPath = Path.Combine(dataPath, pluginName);

        if (!File.Exists(pluginPath))
        {
            errorCallback($"Warning: Could not find plugin file: {pluginPath}");
            return;
        }

        try
        {
            // Direct execution without Task.Run to avoid cross-thread transaction issues
            // Note: Mutagen's CreateFromBinaryOverlay methods are synchronous and do not accept
            // cancellation tokens. For large plugins (100MB+), this may cause several seconds
            // of uninterruptible loading. Cancellation will be checked after loading completes.
            cancellationToken.ThrowIfCancellationRequested();

            using var mod = CreateOverlay(pluginPath, gameRelease, loadOrderSnapshot.ReadParameters);

            await recordStore.WritePluginAsync(
                    pluginName,
                    EnumerateModRecords(pluginName, mod, cancellationToken),
                    updateMode,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errorCallback($"Error processing {pluginName}: {ex.Message}");
            throw;
        }
    }

    internal virtual IModDisposeGetter CreateOverlay(
        string pluginPath,
        GameRelease gameRelease,
        BinaryReadParameters readParameters)
    {
        return gameRelease switch
        {
            GameRelease.Oblivion => OblivionMod.CreateFromBinaryOverlay(pluginPath,
                OblivionRelease.Oblivion,
                readParameters),
            GameRelease.SkyrimLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimLE,
                readParameters),
            GameRelease.SkyrimSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimSE,
                readParameters),
            GameRelease.SkyrimSEGog => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimSEGog,
                readParameters),
            GameRelease.SkyrimVR => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimVR,
                readParameters),
            GameRelease.EnderalLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.EnderalLE,
                readParameters),
            GameRelease.EnderalSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.EnderalSE,
                readParameters),
            GameRelease.Fallout4 => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                Fallout4Release.Fallout4,
                readParameters),
            GameRelease.Fallout4VR => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                Fallout4Release.Fallout4VR,
                readParameters),
            GameRelease.Starfield => StarfieldMod.CreateFromBinaryOverlay(pluginPath,
                StarfieldRelease.Starfield,
                readParameters),
            _ => throw new NotSupportedException($"Unsupported game release: {gameRelease}")
        };
    }

    /// <summary>
    ///     Enumerates records from the provided Plugin and extracts the FormID rows that should be stored.
    /// </summary>
    /// <param name="pluginName">The name of the plugin being processed.</param>
    /// <param name="mod">The mod plugin containing the records to be processed.</param>
    /// <param name="cancellationToken">The cancellation token to handle termination of the processing operation.</param>
    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records via GetRecordName.")]
    private IEnumerable<FormIdRecord> EnumerateModRecords(
        string pluginName,
        IModGetter mod,
        CancellationToken cancellationToken)
    {
        var majorRecords = mod.EnumerateMajorRecords();

        foreach (var record in majorRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FormIdRecord? formIdRecord = null;

            try
            {
                string formId;
                try
                {
                    formId = record.FormKey.ID.ToString("X6");
                }
                catch (Exception)
                {
                    continue;
                }

                string entry;
                try
                {
                    entry = !string.IsNullOrEmpty(record.EditorID) ? record.EditorID : GetRecordName(record);
                }
                catch (Exception)
                {
                    entry = $"[{record.GetType().Name}_{formId}]";
                }

                formIdRecord = new FormIdRecord(formId, entry);
            }
            catch (Exception ex)
            {
                // Only report errors that aren't in the ignorable patterns list
                // Ignorable errors are expected for certain malformed records and don't indicate processing problems
                if (!IgnorableErrorPatterns.Any(pattern =>
                        ex.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    errorCallback($"Warning: Error processing record in {pluginName}: {ex.Message}");
                }
            }

            if (formIdRecord is { } recordToStore)
            {
                yield return recordToStore;
            }
        }
    }

    /// <summary>
    ///     Retrieves a human-readable name for a given major record, prioritizing the editor ID,
    ///     then trying a direct INamedGetter cast (avoids reflection), and finally falling back
    ///     to cached reflection for edge cases.
    /// </summary>
    /// <param name="record">The major record for which the name is to be retrieved.</param>
    /// <returns>A string representing the name of the record, or a formatted fallback string if no name or editor ID is found.</returns>
    [RequiresUnreferencedCode(
        "Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types.")]
    private string GetRecordName(IMajorRecordGetter record)
    {
        if (!string.IsNullOrEmpty(record.EditorID))
        {
            return record.EditorID;
        }

        // Fast path: direct cast avoids reflection entirely for most record types
        // INamedGetter.Name returns String? directly, no reflection needed
        if (record is Mutagen.Bethesda.Plugins.Aspects.INamedGetter named && !string.IsNullOrEmpty(named.Name))
        {
            return named.Name;
        }

        // Slow path: reflection-based extraction for edge cases (e.g., types with
        // TranslatedString Name property that isn't exposed via INamedGetter)
        var extractor = NameExtractorCache.GetOrAdd(record.GetType(), type =>
        {
            // This reflection only happens ONCE per type, not once per record
            var namedInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.Name.Contains("INamedGetter"));

            if (namedInterface == null)
            {
                return _ => null;
            }

            var nameProperty = namedInterface.GetProperty("Name");
            if (nameProperty == null)
            {
                return _ => null;
            }

            var stringProperty = nameProperty.PropertyType.GetProperty("String");
            if (stringProperty == null)
            {
                return _ => null;
            }

            // Return a compiled delegate for fast access
            // Note: Exceptions during reflection access are caught and logged for diagnostics.
            // This is expected for records with corrupted or incompatible name properties.
            return rec =>
            {
                try
                {
                    var nameValue = nameProperty.GetValue(rec);
                    return nameValue != null ? stringProperty.GetValue(nameValue) as string : null;
                }
                catch (Exception ex)
                {
                    // Log for diagnostics but don't propagate - returning null triggers fallback naming
                    System.Diagnostics.Debug.WriteLine(
                        $"Name extraction failed for {rec.GetType().Name}: {ex.Message}");
                    return null;
                }
            };
        });

        var name = extractor(record);
        return !string.IsNullOrEmpty(name) ? name : $"[{record.GetType().Name}_{record.FormKey.ID:X6}]";
    }

    private static UpdateMode ToStoreUpdateMode(bool updateMode)
    {
        return updateMode ? UpdateMode.ReplacePluginRecords : UpdateMode.Append;
    }
}
