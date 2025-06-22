using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Provides functionality to process mod plugin files, updating them in a database while handling errors and supporting cancellation.
/// </summary>
public class ModProcessor(DatabaseService databaseService, Action<string> errorCallback)
{
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

    /// <summary>
    /// Processes the specified plugin file, updates the database with its entries,
    /// and provides error handling during processing.
    /// </summary>
    /// <param name="gameDir">The root directory of the game where the plugin resides.</param>
    /// <param name="conn">The SQLite database connection used for processing.</param>
    /// <param name="gameRelease">The specific game release associated with the plugin.</param>
    /// <param name="pluginItem">The plugin to be processed, represented as a list item.</param>
    /// <param name="loadOrder">The load order that includes all the plugins for the current session.</param>
    /// <param name="updateMode">Indicates if the plugin entries should be updated in the database.</param>
    /// <param name="cancellationToken">The cancellation token to handle processing termination requests.</param>
    /// <returns>A task that processes the plugin asynchronously and manages its database entries accordingly.</returns>
    public async Task ProcessPlugin(
        string gameDir,
        SQLiteConnection conn,
        GameRelease gameRelease,
        PluginListItem pluginItem,
        IList<IModListingGetter<IModGetter>> loadOrder,
        bool updateMode,
        CancellationToken cancellationToken)
    {
        SQLiteTransaction? transaction = null;
        try
        {
            transaction = conn.BeginTransaction();
            var plugin = loadOrder.FirstOrDefault(p =>
                string.Equals(p.ModKey.FileName, pluginItem.Name, StringComparison.OrdinalIgnoreCase));

            if (plugin == null)
            {
                errorCallback($"Warning: Could not find plugin in load order: {pluginItem.Name}");
                return;
            }

            var dataPath = Path.GetFileName(gameDir).Equals("Data", StringComparison.OrdinalIgnoreCase)
                ? gameDir
                : Path.Combine(gameDir, "Data");

            var pluginPath = Path.Combine(dataPath, pluginItem.Name);

            if (!File.Exists(pluginPath))
            {
                errorCallback($"Warning: Could not find plugin file: {pluginPath}");
                return;
            }

            if (updateMode)
            {
                errorCallback($"Deleting existing entries for {pluginItem.Name}");
                await databaseService.ClearPluginEntries(conn, gameRelease, pluginItem.Name);
            }

            try
            {
                bool success = false;
                await Task.Run(() =>
                {
                    try
                    {
                        IModGetter mod = gameRelease switch
                        {
                            GameRelease.Oblivion => OblivionMod.CreateFromBinaryOverlay(pluginPath,
                                OblivionRelease.Oblivion),
                            GameRelease.SkyrimSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                                SkyrimRelease.SkyrimSE),
                            GameRelease.SkyrimSEGog => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                                SkyrimRelease.SkyrimSEGog),
                            GameRelease.SkyrimVR => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                                SkyrimRelease.SkyrimVR),
                            GameRelease.Fallout4 => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                                Fallout4Release.Fallout4),
                            GameRelease.Starfield => StarfieldMod.CreateFromBinaryOverlay(pluginPath,
                                StarfieldRelease.Starfield),
                            _ => throw new NotSupportedException($"Unsupported game release: {gameRelease}")
                        };

                        ProcessModRecords(conn, gameRelease, pluginItem.Name, mod, cancellationToken);
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        errorCallback($"Error processing {pluginItem.Name}: {ex.Message}");
                        transaction?.Rollback();
                        success = false;
                    }
                }, cancellationToken);

                if (success)
                {
                    transaction?.Commit();
                }
            }
            catch (Exception)
            {
                transaction?.Rollback();
            }
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    /// <summary>
    /// Processes the records from the provided mod plugin, extracts relevant data,
    /// batches the results, and inserts them into the database while supporting cancellation and error handling.
    /// </summary>
    /// <param name="conn">The active SQLite database connection used for data insertion.</param>
    /// <param name="gameRelease">The game release version associated with the mod plugin.</param>
    /// <param name="pluginName">The name of the plugin being processed.</param>
    /// <param name="mod">The mod plugin containing the records to be processed.</param>
    /// <param name="cancellationToken">The cancellation token to handle termination of the processing operation.</param>
    private void ProcessModRecords(
        SQLiteConnection conn,
        GameRelease gameRelease,
        string pluginName,
        IModGetter mod,
        CancellationToken cancellationToken)
    {
        var batch = new List<(string formId, string entry)>(BatchSize);

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
                        InsertBatch(conn, gameRelease, pluginName, batch);
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
                if (IgnorableErrorPatterns.Any(pattern =>
                        ex.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                }
                else
                {
                    errorCallback($"Warning: Error processing record in {pluginName}: {ex.Message}");
                }
            }
        }

        if (batch.Count > 0)
        {
            try
            {
                InsertBatch(conn, gameRelease, pluginName, batch);
            }
            catch (Exception ex)
            {
                errorCallback($"Warning: Failed to insert final batch in {pluginName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Inserts a batch of form ID and entry pairs into the specified database table for the given game release and plugin.
    /// </summary>
    /// <param name="conn">The SQLite database connection used to execute the insertion commands.</param>
    /// <param name="gameRelease">The specific game release that determines the target database table.</param>
    /// <param name="pluginName">The name of the plugin associated with the records being inserted.</param>
    /// <param name="batch">The list of form ID and entry pairs to be inserted into the database.</param>
    private void InsertBatch(
        SQLiteConnection conn,
        GameRelease gameRelease,
        string pluginName,
        List<(string formId, string entry)> batch)
    {
        using var cmd = new SQLiteCommand(conn);
        cmd.CommandText = $@"INSERT INTO {gameRelease} (plugin, formid, entry) 
                           VALUES (@plugin, @formid, @entry)";

        var pluginParam = cmd.CreateParameter();
        pluginParam.ParameterName = "@plugin";
        pluginParam.Value = pluginName;
        cmd.Parameters.Add(pluginParam);

        var formIdParam = cmd.CreateParameter();
        formIdParam.ParameterName = "@formid";
        cmd.Parameters.Add(formIdParam);

        var entryParam = cmd.CreateParameter();
        entryParam.ParameterName = "@entry";
        cmd.Parameters.Add(entryParam);

        foreach (var (formId, entry) in batch)
        {
            formIdParam.Value = formId;
            entryParam.Value = entry;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Retrieves a human-readable name for a given major record, prioritizing the editor ID,
    /// or extracting the name from the record type, if applicable. Fallbacks to a default
    /// formatted string if no name or editor ID is available.
    /// </summary>
    /// <param name="record">The major record for which the name is to be retrieved.</param>
    /// <returns>A string representing the name of the record, or a formatted fallback string if no name or editor ID is found.</returns>
    private string GetRecordName(IMajorRecordGetter record)
    {
        if (!string.IsNullOrEmpty(record.EditorID))
            return record.EditorID;

        var namedRecord = record.GetType().GetInterfaces()
            .FirstOrDefault(i => i.Name.Contains("INamedGetter"));
        if (namedRecord != null)
        {
            var nameProperty = namedRecord.GetProperty("Name");
            var nameValue = nameProperty?.GetValue(record);
            if (nameValue != null)
            {
                var stringProperty = nameValue.GetType().GetProperty("String");
                var stringValue = stringProperty?.GetValue(nameValue) as string;
                if (!string.IsNullOrEmpty(stringValue))
                    return stringValue;
            }
        }

        return $"[{record.GetType().Name}_{record.FormKey.ID:X6}]";
    }
}