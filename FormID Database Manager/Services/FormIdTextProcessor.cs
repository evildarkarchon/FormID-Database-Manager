using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Provides functionality for processing FormID list files and managing their insertion into a SQLite database.
///     This class is designed to handle parsing and batching of FormID records for efficient database operations.
/// </summary>
public class FormIdTextProcessor(DatabaseService databaseService)
{
    /*
        private readonly Action<string> _errorCallback = errorCallback;
    */
    /// <summary>
    ///     Number of records to batch before inserting to the database.
    ///     10000 is optimized for pure text I/O workloads without Mutagen processing overhead.
    ///     Compare with ModProcessor.BatchSize (1000) which handles mixed INSERT + record processing
    ///     and needs smaller batches to manage memory during heavy object instantiation.
    /// </summary>
    private const int BatchSize = 10000;
    private const int UiUpdateInterval = 1000; // Update UI every 1000 records

    /// <summary>
    ///     Processes a FormID list file by reading records, organizing them by the associated plugin,
    ///     and batching them for insertion into a SQLite database.
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
        SqliteConnection conn,
        GameRelease gameRelease,
        bool updateMode,
        CancellationToken cancellationToken,
        IProgress<(string Message, double? Value)>? progress = null)
    {
        var processedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var batchInserter = new BatchInserter(conn, gameRelease, BatchSize);
        var currentPlugin = string.Empty;
        var recordCount = 0;

        // Use stream position for progress tracking (avoids per-line UTF8.GetByteCount overhead)
        var fileInfo = new FileInfo(formIdListPath);
        var totalBytes = fileInfo.Length;

        progress?.Report(("Starting processing...", 0));

        // Read the file line by line with byte-based progress tracking
        using var stream = new FileStream(formIdListPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
            {
                break;
            }

            // Use stream position directly for progress (no per-line byte counting)
            var bytesRead = stream.Position;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Span-based parsing: find pipe positions without allocating Split array
            var span = line.AsSpan();
            var firstPipe = span.IndexOf('|');
            if (firstPipe < 0)
            {
                continue;
            }

            var rest = span[(firstPipe + 1)..];
            var secondPipe = rest.IndexOf('|');
            if (secondPipe < 0)
            {
                continue;
            }

            // Check for extra pipes (more than 3 parts)
            var afterSecond = rest[(secondPipe + 1)..];
            if (afterSecond.IndexOf('|') >= 0)
            {
                continue;
            }

            var pluginName = span[..firstPipe].Trim().ToString();
            var formId = rest[..secondPipe].Trim().ToString();
            var entry = afterSecond.Trim().ToString();

            recordCount++;

            // Update progress periodically (Progress<T> already marshals to UI thread)
            if (recordCount % UiUpdateInterval == 0)
            {
                var progressPercent = totalBytes > 0 ? (double)bytesRead / totalBytes * 100 : 0;
                progress?.Report(($"Processing: {progressPercent:F1}% ({recordCount:N0} records)",
                    progressPercent));
            }

            // If we've moved to a new plugin
            if (!string.Equals(currentPlugin, pluginName, StringComparison.OrdinalIgnoreCase))
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
    ///     Optimized batch inserter designed to handle efficient insertion of large numbers of records
    ///     into a SQLite database. This class facilitates batching operations to reduce database overhead
    ///     and supports asynchronous processing to enhance performance in bulk insertion scenarios.
    /// </summary>
    private sealed class BatchInserter : IAsyncDisposable, IDisposable
    {
        private readonly List<(string plugin, string formId, string entry)> _batch;
        private readonly int _batchSize;
        private readonly SqliteConnection _conn;
        private readonly string _tableName;
        private SqliteCommand? _insertCommand;

        public BatchInserter(SqliteConnection conn, GameRelease gameRelease, int batchSize)
        {
            _conn = conn;
            _tableName = GameReleaseHelper.GetSafeTableName(gameRelease);
            _batchSize = batchSize;
            _batch = new List<(string plugin, string formId, string entry)>(batchSize);
        }

        public async ValueTask DisposeAsync()
        {
            if (_insertCommand != null)
            {
                await _insertCommand.DisposeAsync().ConfigureAwait(false);
                _insertCommand = null;
            }
        }

        public void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;
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
        ///     Flushes the current batch using a prepared statement in a single transaction.
        ///     Uses synchronous ExecuteNonQuery within the loop for minimal overhead
        ///     (SQLite is in-process, so async round-trip savings are negligible).
        /// </summary>
        public async Task FlushBatchAsync(CancellationToken cancellationToken)
        {
            if (_batch.Count == 0)
            {
                return;
            }

            if (_insertCommand == null)
            {
                _insertCommand = _conn.CreateCommand();
                _insertCommand.CommandText = $"INSERT INTO {_tableName} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";
                _insertCommand.Parameters.Add(new SqliteParameter { ParameterName = "@plugin" });
                _insertCommand.Parameters.Add(new SqliteParameter { ParameterName = "@formid" });
                _insertCommand.Parameters.Add(new SqliteParameter { ParameterName = "@entry" });
                _insertCommand.Prepare();
            }

            await using var transaction = await _conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            _insertCommand.Transaction = transaction as SqliteTransaction;

            try
            {
                foreach (var (plugin, formId, entry) in _batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _insertCommand.Parameters["@plugin"].Value = plugin;
                    _insertCommand.Parameters["@formid"].Value = formId;
                    _insertCommand.Parameters["@entry"].Value = entry;

                    _insertCommand.ExecuteNonQuery();
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
    }
}
