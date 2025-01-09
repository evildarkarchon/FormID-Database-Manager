using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Environments;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using FormID_Database_Manager.Models;

namespace FormID_Database_Manager.Services;

public class PluginProcessingService(DatabaseService databaseService, Action<string> logCallback)
{
    private CancellationTokenSource? _cancellationTokenSource;

    public async Task ProcessPlugins(
        string gameDir,
        string dbPath,
        GameRelease gameRelease,
        IEnumerable<PluginListItem> selectedPlugins,
        bool updateMode,
        bool verbose,
        bool dryRun,
        IProgress<string>? progress = null)
    {
        _cancellationTokenSource = new CancellationTokenSource();

        if (dryRun)
        {
            foreach (var plugin in selectedPlugins)
            {
                if (updateMode)
                    logCallback($"Would delete existing entries for {plugin.Name}");
                logCallback($"Would process {plugin.Name}");
            }

            return;
        }

        await databaseService.InitializeDatabase(dbPath, gameRelease);
        await using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
        await conn.OpenAsync(_cancellationTokenSource.Token);

        try
        {
            var env = GameEnvironment.Typical.Construct(gameRelease);
            var loadOrder = env.LoadOrder.ListedOrder.ToList();
            var pluginList = selectedPlugins.ToList();
            var successfulPlugins = 0;
            var failedPlugins = 0;

            for (var i = 0; i < pluginList.Count; i++)
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    break;

                var pluginItem = pluginList[i];
                progress?.Report($"Processing plugin {i + 1} of {pluginList.Count}: {pluginItem.Name}");

                try
                {
                    await ProcessPlugin(
                        gameDir,
                        conn,
                        gameRelease,
                        pluginItem,
                        loadOrder,
                        updateMode,
                        verbose,
                        _cancellationTokenSource.Token);
                    successfulPlugins++;
                }
                catch (Exception ex)
                {
                    failedPlugins++;
                    logCallback($"Failed to process plugin {pluginItem.Name}: {ex.Message}");
                    logCallback("Continuing with next plugin...");
                }
            }

            if (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                await databaseService.OptimizeDatabase(conn);
                if (failedPlugins > 0)
                {
                    progress?.Report(
                        $"Processing completed with {successfulPlugins} successful and {failedPlugins} failed plugins.");
                }
                else
                {
                    progress?.Report("Processing completed successfully!");
                }
            }
            else
            {
                progress?.Report("Processing cancelled.");
            }
        }
        catch (OperationCanceledException)
        {
            progress?.Report("Processing cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report($"Error during processing: {ex.Message}");
            throw;
        }
        finally
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
    }

    public void CancelProcessing()
    {
        _cancellationTokenSource?.Cancel();
    }

    private async Task ProcessPlugin(
        string gameDir,
        SQLiteConnection conn,
        GameRelease gameRelease,
        PluginListItem pluginItem,
        IList<IModListingGetter<IModGetter>> loadOrder,
        bool updateMode,
        bool verbose,
        CancellationToken cancellationToken)
    {
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            var plugin = loadOrder.FirstOrDefault(p =>
                string.Equals(p.ModKey.FileName, pluginItem.Name, StringComparison.OrdinalIgnoreCase));

            if (plugin == null)
            {
                logCallback($"Warning: Could not find plugin in load order: {pluginItem.Name}");
                return;
            }

            var dataPath = Path.GetFileName(gameDir).Equals("Data", StringComparison.OrdinalIgnoreCase)
                ? gameDir
                : Path.Combine(gameDir, "Data");

            var pluginPath = Path.Combine(dataPath, pluginItem.Name);

            if (!File.Exists(pluginPath))
            {
                logCallback($"Warning: Could not find plugin file: {pluginPath}");
                return;
            }

            if (updateMode)
            {
                logCallback($"Deleting existing entries for {pluginItem.Name}");
                await databaseService.ClearPluginEntries(conn, gameRelease, pluginItem.Name);
            }

            if (!verbose)
            {
                logCallback($"Processing {pluginItem.Name}...");
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

                        switch (gameRelease)
                        {
                            case GameRelease.SkyrimSE:
                                ProcessModGeneric<ISkyrimMod, ISkyrimModGetter>(conn, gameRelease, pluginItem.Name,
                                    verbose, (ISkyrimModGetter)mod, cancellationToken);
                                break;
                            case GameRelease.Fallout4:
                                ProcessModGeneric<IFallout4Mod, IFallout4ModGetter>(conn, gameRelease, pluginItem.Name,
                                    verbose, (IFallout4ModGetter)mod, cancellationToken);
                                break;
                            case GameRelease.Starfield:
                                ProcessModGeneric<IStarfieldMod, IStarfieldModGetter>(conn, gameRelease,
                                    pluginItem.Name, verbose, (IStarfieldModGetter)mod, cancellationToken);
                                break;
                        }
                    }
                    catch (Exception ex) when (ex is Mutagen.Bethesda.Plugins.Exceptions.MalformedDataException)
                    {
                        logCallback($"Warning: Encountered malformed data in {pluginItem.Name}: {ex.Message}");
                        logCallback("Attempting to continue with relaxed error handling...");

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

                            logCallback($"Successfully loaded {pluginItem.Name} with relaxed error handling");
                            switch (gameRelease)
                            {
                                case GameRelease.SkyrimSE:
                                    ProcessModGeneric<ISkyrimMod, ISkyrimModGetter>(conn, gameRelease,
                                        pluginItem.Name, verbose, (ISkyrimModGetter)mod, cancellationToken);
                                    break;
                                case GameRelease.Fallout4:
                                    ProcessModGeneric<IFallout4Mod, IFallout4ModGetter>(conn, gameRelease,
                                        pluginItem.Name, verbose, (IFallout4ModGetter)mod, cancellationToken);
                                    break;
                                case GameRelease.Starfield:
                                    ProcessModGeneric<IStarfieldMod, IStarfieldModGetter>(conn, gameRelease,
                                        pluginItem.Name, verbose, (IStarfieldModGetter)mod, cancellationToken);
                                    break;
                            }
                        }
                        catch (Exception retryEx)
                        {
                            logCallback(
                                $"Failed to process {pluginItem.Name} even with relaxed error handling: {retryEx.Message}");
                            throw;
                        }
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                logCallback($"Error processing {pluginItem.Name}: {ex.Message}");
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

    private void ProcessModGeneric<TMod, TModGetter>(
        SQLiteConnection conn,
        GameRelease gameRelease,
        string pluginName,
        bool verbose,
        TModGetter mod,
        CancellationToken cancellationToken)
        where TMod : class, IContextMod<TMod, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TMod, TModGetter>
    {
        var batchSize = 1000;
        var batch = new List<(string formId, string entry)>(batchSize);
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
                        // If we can't get the name, use the fallback format
                        errorCount++;
                        entry = $"[{record.GetType().Name}_{formId}]";

                        if (verbose)
                        {
                            logCallback($"Warning: Using fallback name for record in {pluginName} at FormID {formId}");
                        }
                    }

                    if (verbose)
                    {
                        logCallback($"Adding {pluginName} | {formId} | {entry} | {record.GetType().Name}");
                    }

                    batch.Add((formId, entry));
                    processedCount++;

                    if (batch.Count >= batchSize)
                    {
                        InsertBatch(conn, gameRelease, pluginName, batch);
                        batch.Clear();
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    if (verbose)
                    {
                        logCallback($"Warning: Error processing record in {pluginName}: {ex.Message}");
                    }
                }
            }

            if (batch.Count > 0)
            {
                InsertBatch(conn, gameRelease, pluginName, batch);
            }

            if (errorCount > 0)
            {
                logCallback(
                    $"Completed processing {pluginName} with {errorCount} records using fallback names out of {totalRecords} total records");
            }
            else if (verbose)
            {
                logCallback($"Successfully processed all {totalRecords} records in {pluginName}");
            }
        }
        catch (Exception ex)
        {
            logCallback($"Error processing {pluginName}: {ex.Message}");
            logCallback($"Processed {processedCount} records before error occurred");
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