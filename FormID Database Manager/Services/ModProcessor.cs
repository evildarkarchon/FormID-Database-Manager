using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Provides functionality to process mod plugin files, updating them in a database while handling errors and
///     supporting cancellation.
/// </summary>
public class ModProcessor(DatabaseService databaseService, Action<string> errorCallback)
{
    /// <summary>
    ///     Number of records to batch before inserting to the database.
    ///     1000 is optimized for mixed INSERT + processing workloads. Larger batches
    ///     reduce transaction overhead but increase memory usage per batch.
    ///     Compare with FormIdTextProcessor.BatchSize (10000) which handles pure text I/O
    ///     without Mutagen processing overhead.
    /// </summary>
    private const int BatchSize = 1000;

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
    /// <param name="conn">The SQLite database connection used for processing.</param>
    /// <param name="gameRelease">The specific game release associated with the plugin.</param>
    /// <param name="pluginItem">The plugin to be processed, represented as a list item.</param>
    /// <param name="loadOrder">The load order that includes all the plugins for the current session.</param>
    /// <param name="updateMode">Indicates if the plugin entries should be updated in the database.</param>
    /// <param name="cancellationToken">The cancellation token to handle processing termination requests.</param>
    /// <returns>A task that processes the plugin asynchronously and manages its database entries accordingly.</returns>
    [RequiresUnreferencedCode("Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types for name extraction.")]
    public async Task ProcessPlugin(
        string gameDir,
        SqliteConnection conn,
        GameRelease gameRelease,
        PluginListItem pluginItem,
        IReadOnlyDictionary<string, IModListingGetter<IModGetter>> loadOrderDict,
        bool updateMode,
        CancellationToken cancellationToken)
    {
        SqliteTransaction? transaction = null;
        try
        {
            loadOrderDict.TryGetValue(pluginItem.Name, out var plugin);

            if (plugin == null)
            {
                errorCallback($"Warning: Could not find plugin in load order: {pluginItem.Name}");
                return;
            }

            var dataPath = GameReleaseHelper.ResolveDataPath(gameDir);

            var pluginPath = Path.Combine(dataPath, pluginItem.Name);

            if (!File.Exists(pluginPath))
            {
                errorCallback($"Warning: Could not find plugin file: {pluginPath}");
                return;
            }

            transaction = conn.BeginTransaction();

            if (updateMode)
            {
                await databaseService.ClearPluginEntries(conn, gameRelease, pluginItem.Name, cancellationToken)
                    .ConfigureAwait(false);
            }

            try
            {
                // Direct execution without Task.Run to avoid cross-thread transaction issues
                // Note: Mutagen's CreateFromBinaryOverlay methods are synchronous and do not accept
                // cancellation tokens. For large plugins (100MB+), this may cause several seconds
                // of uninterruptible loading. Cancellation will be checked after loading completes.
                cancellationToken.ThrowIfCancellationRequested();

                using IModDisposeGetter mod = gameRelease switch
                {
                    GameRelease.Oblivion => OblivionMod.CreateFromBinaryOverlay(pluginPath,
                        OblivionRelease.Oblivion),
                    GameRelease.SkyrimLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                        SkyrimRelease.SkyrimLE),
                    GameRelease.SkyrimSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                        SkyrimRelease.SkyrimSE),
                    GameRelease.SkyrimSEGog => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                        SkyrimRelease.SkyrimSEGog),
                    GameRelease.SkyrimVR => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                        SkyrimRelease.SkyrimVR),
                    GameRelease.EnderalLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                        SkyrimRelease.EnderalLE),
                    GameRelease.EnderalSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                        SkyrimRelease.EnderalSE),
                    GameRelease.Fallout4 => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                        Fallout4Release.Fallout4),
                    GameRelease.Fallout4VR => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                        Fallout4Release.Fallout4VR),
                    GameRelease.Starfield => StarfieldMod.CreateFromBinaryOverlay(pluginPath,
                        StarfieldRelease.Starfield),
                    _ => throw new NotSupportedException($"Unsupported game release: {gameRelease}")
                };

                ProcessModRecords(conn, gameRelease, pluginItem.Name, mod, cancellationToken);
                transaction?.Commit();
            }
            catch (Exception ex)
            {
                errorCallback($"Error processing {pluginItem.Name}: {ex.Message}");
                transaction?.Rollback();
                throw;
            }
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    /// <summary>
    ///     Processes the records from the provided mod plugin, extracts relevant data,
    ///     batches the results, and inserts them into the database while supporting cancellation and error handling.
    /// </summary>
    /// <param name="conn">The active SQLite database connection used for data insertion.</param>
    /// <param name="gameRelease">The game release version associated with the mod plugin.</param>
    /// <param name="pluginName">The name of the plugin being processed.</param>
    /// <param name="mod">The mod plugin containing the records to be processed.</param>
    /// <param name="cancellationToken">The cancellation token to handle termination of the processing operation.</param>
    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records via GetRecordName.")]
    private void ProcessModRecords(
        SqliteConnection conn,
        GameRelease gameRelease,
        string pluginName,
        IModGetter mod,
        CancellationToken cancellationToken)
    {
        var batch = new List<(string formId, string entry)>(BatchSize);

        // Create a prepared command once and reuse for all batch inserts.
        // Prepared statements avoid repeated SQL parsing and parameter allocation,
        // which is faster than multi-value INSERT for SQLite (in-process, negligible round-trip cost).
        var tableName = GameReleaseHelper.GetSafeTableName(gameRelease);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {tableName} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";
        cmd.Parameters.Add(new SqliteParameter { ParameterName = "@plugin" });
        cmd.Parameters.Add(new SqliteParameter { ParameterName = "@formid" });
        cmd.Parameters.Add(new SqliteParameter { ParameterName = "@entry" });
        cmd.Prepare();

        var majorRecords = mod.EnumerateMajorRecords();

        foreach (var record in majorRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

                batch.Add((formId, entry));

                if (batch.Count >= BatchSize)
                {
                    try
                    {
                        FlushBatch(cmd, pluginName, batch);
                        batch.Clear();
                    }
                    catch (Exception ex)
                    {
                        errorCallback($"Warning: Failed to insert batch in {pluginName}: {ex.Message}");
                        batch.Clear();
                    }
                }
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
        }

        if (batch.Count > 0)
        {
            try
            {
                FlushBatch(cmd, pluginName, batch);
            }
            catch (Exception ex)
            {
                errorCallback($"Warning: Failed to insert final batch in {pluginName}: {ex.Message}");
            }
        }

    }

    /// <summary>
    ///     Flushes a batch of records using a pre-prepared command.
    ///     Uses synchronous ExecuteNonQuery for minimal overhead (SQLite is in-process).
    /// </summary>
    private static void FlushBatch(
        SqliteCommand cmd,
        string pluginName,
        List<(string formId, string entry)> batch)
    {
        foreach (var (formId, entry) in batch)
        {
            cmd.Parameters["@plugin"].Value = pluginName;
            cmd.Parameters["@formid"].Value = formId;
            cmd.Parameters["@entry"].Value = entry;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    ///     Retrieves a human-readable name for a given major record, prioritizing the editor ID,
    ///     then trying a direct INamedGetter cast (avoids reflection), and finally falling back
    ///     to cached reflection for edge cases.
    /// </summary>
    /// <param name="record">The major record for which the name is to be retrieved.</param>
    /// <returns>A string representing the name of the record, or a formatted fallback string if no name or editor ID is found.</returns>
    [RequiresUnreferencedCode("Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types.")]
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
                    System.Diagnostics.Debug.WriteLine($"Name extraction failed for {rec.GetType().Name}: {ex.Message}");
                    return null;
                }
            };
        });

        var name = extractor(record);
        return !string.IsNullOrEmpty(name) ? name : $"[{record.GetType().Name}_{record.FormKey.ID:X6}]";
    }
}
