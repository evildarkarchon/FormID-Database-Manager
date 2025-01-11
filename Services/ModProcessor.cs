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
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;

namespace FormID_Database_Manager.Services;

public class ModProcessor
{
    private readonly DatabaseService _databaseService;
    private readonly Action<string> _errorCallback;
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

    public ModProcessor(DatabaseService databaseService, Action<string> errorCallback)
    {
        _databaseService = databaseService;
        _errorCallback = errorCallback;
    }

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
                _errorCallback($"Warning: Could not find plugin in load order: {pluginItem.Name}");
                return;
            }

            var dataPath = Path.GetFileName(gameDir).Equals("Data", StringComparison.OrdinalIgnoreCase)
                ? gameDir
                : Path.Combine(gameDir, "Data");

            var pluginPath = Path.Combine(dataPath, pluginItem.Name);

            if (!File.Exists(pluginPath))
            {
                _errorCallback($"Warning: Could not find plugin file: {pluginPath}");
                return;
            }

            if (updateMode)
            {
                _errorCallback($"Deleting existing entries for {pluginItem.Name}");
                await _databaseService.ClearPluginEntries(conn, gameRelease, pluginItem.Name);
            }

            try
            {
                bool success = false;
                await Task.Run(() =>
                {
                    try
                    {
                        IModGetter? mod = gameRelease switch
                        {
                            GameRelease.SkyrimSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                                SkyrimRelease.SkyrimSE),
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
                        _errorCallback($"Error processing {pluginItem.Name}: {ex.Message}");
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

    private void ProcessModRecords(
        SQLiteConnection conn,
        GameRelease gameRelease,
        string pluginName,
        IModGetter mod,
        CancellationToken cancellationToken)
    {
        var batch = new List<(string formId, string entry)>(BatchSize);
        var errorCount = 0;
        var processedCount = 0;
        var skippedRecords = 0;

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
                    skippedRecords++;
                    continue;
                }

                string entry;
                try
                {
                    if (!string.IsNullOrEmpty(record.EditorID))
                    {
                        entry = record.EditorID;
                    }
                    else
                    {
                        entry = GetRecordName(record);
                    }
                }
                catch (Exception)
                {
                    errorCount++;
                    entry = $"[{record.GetType().Name}_{formId}]";
                }

                batch.Add((formId, entry));
                processedCount++;

                if (batch.Count >= BatchSize)
                {
                    try
                    {
                        InsertBatch(conn, gameRelease, pluginName, batch);
                        batch.Clear();
                    }
                    catch (Exception ex)
                    {
                        _errorCallback($"Warning: Failed to insert batch in {pluginName}: {ex.Message}");
                        errorCount++;
                        batch.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                if (IgnorableErrorPatterns.Any(pattern =>
                        ex.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    skippedRecords++;
                }
                else
                {
                    errorCount++;
                    _errorCallback($"Warning: Error processing record in {pluginName}: {ex.Message}");
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
                _errorCallback($"Warning: Failed to insert final batch in {pluginName}: {ex.Message}");
                errorCount++;
            }
        }
    }

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