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
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
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
                await Task.Run(() =>
                {
                    IModGetter? mod;
                    try
                    {
                        mod = gameRelease switch
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
                    }
                    catch (Exception ex)
                    {
                        _errorCallback($"Error processing {pluginItem.Name}: {ex.Message}");
                        throw;
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _errorCallback($"Error processing {pluginItem.Name}: {ex.Message}");
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
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

        try
        {
            var majorRecords = mod.EnumerateMajorRecords().ToList();
            var totalRecords = majorRecords.Count;

            foreach (var record in majorRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var formId = record.FormKey.ID.ToString("X6");
                    string entry;

                    try
                    {
                        entry = GetRecordName(record);
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
                        InsertBatch(conn, gameRelease, pluginName, batch);
                        batch.Clear();
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _errorCallback($"Warning: Error processing record in {pluginName}: {ex.Message}");
                }
            }

            if (batch.Count > 0)
            {
                InsertBatch(conn, gameRelease, pluginName, batch);
            }

            if (errorCount > 0)
            {
                _errorCallback(
                    $"Completed processing {pluginName} with {errorCount} records using fallback names out of {totalRecords} total records");
            }
        }
        catch (Exception ex)
        {
            _errorCallback($"Error processing {pluginName}: {ex.Message}");
            _errorCallback($"Processed {processedCount} records before error occurred");
            throw;
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