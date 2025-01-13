using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Provides functionality for processing FormID list files and managing their insertion into a SQLite database.
/// This class is designed to handle parsing and batching of FormID records for efficient database operations.
/// </summary>
public class FormIdTextProcessor(DatabaseService databaseService)
{
/*
    private readonly Action<string> _errorCallback = errorCallback;
*/
    private const int BatchSize = 10000; // Increased batch size for better performance
    private const int UiUpdateInterval = 1000; // Update UI every 1000 records

    /// <summary>
    /// Processes a FormID list file by reading records, organizing them by the associated plugin,
    /// and batching them for insertion into a SQLite database.
    /// </summary>
    /// <param name="formIdListPath">The file path of the FormID list to process.</param>
    /// <param name="conn">The SQLite connection to the database where records will be inserted.</param>
    /// <param name="gameRelease">The game release associated with the FormID records.</param>
    /// <param name="updateMode">Indicates whether the operation is performed in update mode or not.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <param name="progress">Optional progress reporter for updating task details and progress percentage.</param>
    /// <returns>A task that represents the asynchronous operation of processing the FormID list file.</returns>
    public async Task ProcessFormIdListFile(
        string formIdListPath,
        SQLiteConnection conn,
        GameRelease gameRelease,
        bool updateMode,
        CancellationToken cancellationToken,
        IProgress<(string Message, double? Value)>? progress = null)
    {
        var processedPlugins = new HashSet<string>();
        var batchInserter = new BatchInserter(conn, gameRelease, BatchSize);
        var currentPlugin = string.Empty;
        var recordCount = 0;

        // First, count total lines for progress reporting
        var totalLines = File.ReadLines(formIdListPath).Count();
        progress?.Report(("Counting records...", 0));

        // Read the file line by line
        await foreach (var line in ReadLinesAsync(formIdListPath).WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length != 3) continue;

            recordCount++;

            // Allow UI to process events periodically
            if (recordCount % UiUpdateInterval == 0)
            {
                await Task.Delay(1, cancellationToken); // Brief delay to let UI process events
                var progressPercent = (double)recordCount / totalLines * 100;
                progress?.Report(($"Processing: {progressPercent:F1}% ({recordCount:N0} / {totalLines:N0} records)",
                    progressPercent));
            }

            var pluginName = parts[0];
            var formId = parts[1];
            var entry = parts[2];

            // If we've moved to a new plugin
            if (currentPlugin != pluginName)
            {
                // Flush any existing batch for the previous plugin
                await batchInserter.FlushBatchAsync(cancellationToken);

                currentPlugin = pluginName;

                if (!processedPlugins.Contains(pluginName))
                {
                    if (updateMode)
                    {
                        progress?.Report(($"Processing plugin: {pluginName}", null));
                        await databaseService.ClearPluginEntries(conn, gameRelease, pluginName);
                    }

                    processedPlugins.Add(pluginName);
                }
            }

            // Add record to batch
            await batchInserter.AddRecordAsync(pluginName, formId, entry, cancellationToken);
        }

        // Handle any remaining batch items
        await batchInserter.FlushBatchAsync(cancellationToken);

        progress?.Report(($"Completed processing {processedPlugins.Count} plugins ({recordCount:N0} total records)",
            100));
    }

    /// <summary>
    /// Asynchronously reads lines from a specified file, one at a time.
    /// </summary>
    /// <param name="filePath">The path to the file to be read.</param>
    /// <returns>An asynchronous enumerable of strings representing each line in the file.</returns>
    private async IAsyncEnumerable<string> ReadLinesAsync(string filePath)
    {
        using var reader = new StreamReader(filePath);
        while (!reader.EndOfStream)
        {
            yield return await reader.ReadLineAsync() ?? string.Empty;
        }
    }

    // Optimized batch inserter to handle large numbers of records efficiently
    /// <summary>
    /// Optimized batch inserter designed to handle efficient insertion of large numbers of records
    /// into a SQLite database. This class facilitates batching operations to reduce database overhead
    /// and supports asynchronous processing to enhance performance in bulk insertion scenarios.
    /// </summary>
    private class BatchInserter(SQLiteConnection conn, GameRelease gameRelease, int batchSize)
    {
        private readonly List<(string plugin, string formId, string entry)> _batch = new(batchSize);
        private SQLiteCommand? _insertCommand;

        public async Task AddRecordAsync(string plugin, string formId, string entry,
            CancellationToken cancellationToken)
        {
            _batch.Add((plugin, formId, entry));

            if (_batch.Count >= batchSize)
            {
                await FlushBatchAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Flushes the current batch of records to the database, committing them in a single transaction.
        /// This method ensures the batched records are inserted and any associated resources, such as transactions, are properly handled.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests during the flushing process.</param>
        /// <returns>A task representing the asynchronous operation of flushing the batch to the database.</returns>
        public async Task FlushBatchAsync(CancellationToken cancellationToken)
        {
            if (_batch.Count == 0) return;

            if (_insertCommand == null)
            {
                _insertCommand = new SQLiteCommand(conn);
                _insertCommand.CommandText = $@"INSERT INTO {gameRelease} (plugin, formid, entry) 
                                               VALUES (@plugin, @formid, @entry)";
                _insertCommand.Parameters.Add(new SQLiteParameter("@plugin"));
                _insertCommand.Parameters.Add(new SQLiteParameter("@formid"));
                _insertCommand.Parameters.Add(new SQLiteParameter("@entry"));
            }

            await using var transaction = conn.BeginTransaction();
            try
            {
                foreach (var (plugin, formId, entry) in _batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _insertCommand.Parameters["@plugin"].Value = plugin;
                    _insertCommand.Parameters["@formid"].Value = formId;
                    _insertCommand.Parameters["@entry"].Value = entry;

                    await _insertCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                transaction.Commit();
                _batch.Clear();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}