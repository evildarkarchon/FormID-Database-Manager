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
    private const int MaxErrorsPerPlugin = 50; // Limit errors reported per plugin

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
                await Task.Run(async () =>
                {
                    try
                    {
                        IModGetter mod = gameRelease switch
                        {
                            GameRelease.SkyrimSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                                SkyrimRelease.SkyrimSE),
                            GameRelease.Fallout4 => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                                Fallout4Release.Fallout4),
                            GameRelease.Starfield => StarfieldMod.CreateFromBinaryOverlay(pluginPath,
                                StarfieldRelease.Starfield),
                            _ => throw new NotSupportedException($"Unsupported game release: {gameRelease}")
                        };

                        await ProcessModRecords(conn, gameRelease, pluginItem.Name, mod, cancellationToken);
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        errorCallback($"Error processing {pluginItem.Name}: {ex.Message}");
                        if (transaction != null)
                        {
                            transaction.Rollback();
                        }

                        success = false;
                    }
                }, cancellationToken);

                if (success && transaction != null)
                {
                    transaction.Commit();
                }
            }
            catch (Exception)
            {
                if (transaction != null)
                {
                    transaction.Rollback();
                }

                throw;
            }
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    /// <summary>
    /// Processes the records from the provided mod plugin, extracts relevant data,
    /// and batches the results for database insertion.
    /// </summary>
    private async Task ProcessModRecords(
        SQLiteConnection conn,
        GameRelease gameRelease,
        string pluginName,
        IModGetter mod,
        CancellationToken cancellationToken)
    {
        var batch = new List<(string formId, string entry)>(BatchSize);
        var errorCount = 0;
        var processedRecords = 0;
        var failedRecords = 0;

        try
        {
            IEnumerable<IMajorRecordGetter> majorRecords;
            try
            {
                majorRecords = mod.EnumerateMajorRecords();
            }
            catch (Exception ex)
            {
                errorCallback($"Failed to enumerate records in plugin {pluginName}: {ex.Message}");
                errorCallback("Attempting to enumerate records using fallback method...");
                majorRecords = AttemptFallbackEnumeration(mod);
            }

            foreach (var record in majorRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (TryProcessRecord(record, out var formId, out var entry))
                    {
                        batch.Add((formId, entry));
                        processedRecords++;

                        if (batch.Count >= BatchSize)
                        {
                            await TryInsertBatch(conn, gameRelease, pluginName, batch);
                            batch.Clear();
                        }
                    }
                    else
                    {
                        failedRecords++;
                    }
                }
                catch (Exception ex) when (IsHandleableError(ex))
                {
                    failedRecords++;
                    LogErrorWithLimit(pluginName, ex, ref errorCount);
                }
            }

            // Process any remaining records in the batch
            if (batch.Count > 0)
            {
                await TryInsertBatch(conn, gameRelease, pluginName, batch);
            }

            // Log summary for this plugin
            if (failedRecords > 0)
            {
                errorCallback(
                    $"Plugin {pluginName}: Processed {processedRecords} records, Failed {failedRecords} records");
            }
        }
        catch (Exception ex)
        {
            errorCallback($"Critical error processing plugin {pluginName}: {ex.Message}");
            throw; // Rethrow critical errors
        }
    }

    private IEnumerable<IMajorRecordGetter> AttemptFallbackEnumeration(IModGetter mod)
    {
        var records = new List<IMajorRecordGetter>();

        switch (mod)
        {
            case ISkyrimModGetter skyrimMod:
                EnumerateSkyrimRecords(skyrimMod, records);
                break;
            case IFallout4ModGetter falloutMod:
                EnumerateFallout4Records(falloutMod, records);
                break;
            case IStarfieldModGetter starfieldMod:
                EnumerateStarfieldRecords(starfieldMod, records);
                break;
        }

        errorCallback($"Fallback enumeration found {records.Count} records");
        return records;
    }

    private void EnumerateSkyrimRecords(ISkyrimModGetter mod, List<IMajorRecordGetter> records)
    {
        try
        {
            // Items and Equipment
            AddToRecords(records, mod.Weapons);
            AddToRecords(records, mod.Armors);
            AddToRecords(records, mod.Books);

            // Characters
            AddToRecords(records, mod.Npcs);

            // World Objects
            foreach (var block in mod.Cells)
            {
                foreach (var subBlock in block.SubBlocks)
                {
                    foreach (var cell in subBlock.Cells)
                    {
                        records.Add(cell);
                    }
                }
            }

            AddToRecords(records, mod.Worldspaces);
            AddToRecords(records, mod.Locations);

            // Magic
            AddToRecords(records, mod.Spells);
            AddToRecords(records, mod.MagicEffects);

            // Base Records
            AddToRecords(records, mod.Keywords);
        }
        catch (Exception ex)
        {
            errorCallback($"Error enumerating Skyrim records: {ex.Message}");
        }
    }

    private void EnumerateFallout4Records(IFallout4ModGetter mod, List<IMajorRecordGetter> records)
    {
        try
        {
            // Items and Equipment
            AddToRecords(records, mod.Weapons);
            AddToRecords(records, mod.Armors);
            AddToRecords(records, mod.Books);
            AddToRecords(records, mod.Ammunitions);
            AddToRecords(records, mod.Components);
            AddToRecords(records, mod.Holotapes);

            // Characters
            AddToRecords(records, mod.Npcs);

            // World Objects
            foreach (var block in mod.Cells)
            {
                foreach (var subBlock in block.SubBlocks)
                {
                    foreach (var cell in subBlock.Cells)
                    {
                        records.Add(cell);
                    }
                }
            }

            AddToRecords(records, mod.Worldspaces);
            AddToRecords(records, mod.Locations);
            AddToRecords(records, mod.EncounterZones);
            AddToRecords(records, mod.Regions);

            // Base Records
            AddToRecords(records, mod.Keywords);

            // Other Records
            AddToRecords(records, mod.Activators);
            AddToRecords(records, mod.Furniture);
            AddToRecords(records, mod.Factions);
            AddToRecords(records, mod.Quests);
            AddToRecords(records, mod.Races);
            AddToRecords(records, mod.Relationships);
            AddToRecords(records, mod.MagicEffects);
            AddToRecords(records, mod.Spells);
            AddToRecords(records, mod.FormLists);
        }
        catch (Exception ex)
        {
            errorCallback($"Error enumerating Fallout 4 records: {ex.Message}");
        }
    }

    private void EnumerateStarfieldRecords(IStarfieldModGetter mod, List<IMajorRecordGetter> records)
    {
        try
        {
            // Items and Equipment
            AddToRecords(records, mod.Weapons);
            AddToRecords(records, mod.Armors);
            AddToRecords(records, mod.Books);

            // Characters
            AddToRecords(records, mod.Npcs);

            // World Objects
            foreach (var block in mod.Cells)
            {
                foreach (var subBlock in block.SubBlocks)
                {
                    foreach (var cell in subBlock.Cells)
                    {
                        records.Add(cell);
                    }
                }
            }

            AddToRecords(records, mod.Worldspaces);
            AddToRecords(records, mod.Locations);

            // Base Records
            AddToRecords(records, mod.Keywords);
        }
        catch (Exception ex)
        {
            errorCallback($"Error enumerating Starfield records: {ex.Message}");
        }
    }

    private static void AddToRecords(List<IMajorRecordGetter> records, IEnumerable<IMajorRecordGetter>? items)
    {
        if (items != null)
        {
            records.AddRange(items);
        }
    }

    private bool TryProcessRecord(IMajorRecordGetter record, out string formId, out string entry)
    {
        formId = string.Empty;
        entry = string.Empty;

        try
        {
            formId = record.FormKey.ID.ToString("X6");

            entry = !string.IsNullOrEmpty(record.EditorID) ? record.EditorID : GetRecordName(record);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task TryInsertBatch(
        SQLiteConnection conn,
        GameRelease gameRelease,
        string pluginName,
        List<(string formId, string entry)> batch)
    {
        try
        {
            await InsertBatch(conn, gameRelease, pluginName, batch);
        }
        catch (Exception ex)
        {
            errorCallback($"Warning: Failed to insert batch in {pluginName}: {ex.Message}");
            // Try to insert records individually to salvage what we can
            foreach (var (formId, entry) in batch)
            {
                try
                {
                    await databaseService.InsertRecord(conn, gameRelease, pluginName, formId, entry);
                }
                catch
                {
                    // Ignore individual insertion failures
                }
            }
        }
    }

    private async Task InsertBatch(
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
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private bool IsHandleableError(Exception ex)
    {
        return IgnorableErrorPatterns.Any(pattern =>
            ex.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private void LogErrorWithLimit(string pluginName, Exception ex, ref int errorCount)
    {
        if (errorCount < MaxErrorsPerPlugin)
        {
            if (!IsHandleableError(ex))
            {
                errorCallback($"Warning: Error processing record in {pluginName}: {ex.Message}");
                errorCount++;
            }
        }
        else if (errorCount == MaxErrorsPerPlugin)
        {
            errorCallback($"Warning: Maximum error limit reached for {pluginName}. Suppressing further errors.");
            errorCount++;
        }
    }

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