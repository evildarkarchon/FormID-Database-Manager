using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
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
        using var batchInserter = new BatchInserter(conn, gameRelease, BatchSize);
        var currentPlugin = string.Empty;
        var recordCount = 0;

        // Use byte-based progress estimation instead of counting lines (avoids double file read)
        // This reduces I/O time by 50% for large files
        var fileInfo = new FileInfo(formIdListPath);
        var totalBytes = fileInfo.Length;
        long bytesRead = 0;

        progress?.Report(("Starting processing...", 0));

        // Read the file line by line with byte-based progress tracking
        using var stream = new FileStream(formIdListPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null) break;

            // Track bytes read for progress estimation
            bytesRead += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length != 3)
            {
                continue;
            }

            recordCount++;

            // Update progress periodically (Progress<T> already marshals to UI thread)
            if (recordCount % UiUpdateInterval == 0)
            {
                var progressPercent = totalBytes > 0 ? (double)bytesRead / totalBytes * 100 : 0;
                progress?.Report(($"Processing: {progressPercent:F1}% ({recordCount:N0} records)",
                    progressPercent));
            }

            var pluginName = parts[0];
            var formId = parts[1];
            var entry = parts[2];

            // If we've moved to a new plugin
            if (currentPlugin != pluginName)
            {
                // Flush any existing batch for the previous plugin
                await batchInserter.FlushBatchAsync(cancellationToken).ConfigureAwait(false);

                currentPlugin = pluginName;

                if (!processedPlugins.Contains(pluginName))
                {
                    if (updateMode)
                    {
                        progress?.Report(($"Processing plugin: {pluginName}", null));
                        await databaseService.ClearPluginEntries(conn, gameRelease, pluginName, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    processedPlugins.Add(pluginName);
                }
            }

            // Add record to batch
            await batchInserter.AddRecordAsync(pluginName, formId, entry, cancellationToken).ConfigureAwait(false);
        }

        // Handle any remaining batch items
        await batchInserter.FlushBatchAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(($"Completed processing {processedPlugins.Count} plugins ({recordCount:N0} total records)",
            100));
    }

    // Optimized batch inserter to handle large numbers of records efficiently
    /// <summary>
    /// Optimized batch inserter designed to handle efficient insertion of large numbers of records
    /// into a SQLite database. This class facilitates batching operations to reduce database overhead
    /// and supports asynchronous processing to enhance performance in bulk insertion scenarios.
    /// </summary>
    private class BatchInserter : IDisposable
    {
        private readonly SQLiteConnection _conn;
        private readonly GameRelease _gameRelease;
        private readonly int _batchSize;
        private readonly List<(string plugin, string formId, string entry)> _batch;
        private SQLiteCommand? _insertCommand;

        public BatchInserter(SQLiteConnection conn, GameRelease gameRelease, int batchSize)
        {
            _conn = conn;
            _gameRelease = gameRelease;
            _batchSize = batchSize;
            _batch = new List<(string plugin, string formId, string entry)>(batchSize);
        }

        public async Task AddRecordAsync(string plugin, string formId, string entry,
            CancellationToken cancellationToken)
        {
            _batch.Add((plugin, formId, entry));

            if (_batch.Count >= _batchSize)
            {
                await FlushBatchAsync(cancellationToken).ConfigureAwait(false);
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
            if (_batch.Count == 0)
            {
                return;
            }

            if (_insertCommand == null)
            {
                _insertCommand = new SQLiteCommand(_conn);
                _insertCommand.CommandText = $@"INSERT INTO {_gameRelease} (plugin, formid, entry) 
                                               VALUES (@plugin, @formid, @entry)";
                _insertCommand.Parameters.Add(new SQLiteParameter("@plugin"));
                _insertCommand.Parameters.Add(new SQLiteParameter("@formid"));
                _insertCommand.Parameters.Add(new SQLiteParameter("@entry"));
            }

            await using var transaction = await _conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var (plugin, formId, entry) in _batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _insertCommand.Parameters["@plugin"].Value = plugin;
                    _insertCommand.Parameters["@formid"].Value = formId;
                    _insertCommand.Parameters["@entry"].Value = entry;

                    await _insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _batch.Clear();
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        public void Dispose()
        {
            _insertCommand?.Dispose();
        }
    }
}
