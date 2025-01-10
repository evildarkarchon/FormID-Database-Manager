using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

public class FormIdTextProcessor
{
    private readonly DatabaseService _databaseService;
    private readonly Action<string> _errorCallback;
    private const int BatchSize = 10000; // Increased batch size for better performance
    private const int UiUpdateInterval = 1000; // Update UI every 1000 records

    public FormIdTextProcessor(DatabaseService databaseService, Action<string> errorCallback)
    {
        _databaseService = databaseService;
        _errorCallback = errorCallback;
    }

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
        await foreach (var line in ReadLinesAsync(formIdListPath))
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
                        await _databaseService.ClearPluginEntries(conn, gameRelease, pluginName);
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

    private async IAsyncEnumerable<string> ReadLinesAsync(string filePath)
    {
        using var reader = new StreamReader(filePath);
        while (!reader.EndOfStream)
        {
            yield return await reader.ReadLineAsync() ?? string.Empty;
        }
    }

    // Optimized batch inserter to handle large numbers of records efficiently
    private class BatchInserter
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
            _batch = new List<(string, string, string)>(batchSize);
        }

        public async Task AddRecordAsync(string plugin, string formId, string entry,
            CancellationToken cancellationToken)
        {
            _batch.Add((plugin, formId, entry));

            if (_batch.Count >= _batchSize)
            {
                await FlushBatchAsync(cancellationToken);
            }
        }

        public async Task FlushBatchAsync(CancellationToken cancellationToken)
        {
            if (_batch.Count == 0) return;

            if (_insertCommand == null)
            {
                _insertCommand = new SQLiteCommand(_conn);
                _insertCommand.CommandText = $@"INSERT INTO {_gameRelease} (plugin, formid, entry) 
                                               VALUES (@plugin, @formid, @entry)";
                _insertCommand.Parameters.Add(new SQLiteParameter("@plugin"));
                _insertCommand.Parameters.Add(new SQLiteParameter("@formid"));
                _insertCommand.Parameters.Add(new SQLiteParameter("@entry"));
            }

            using var transaction = _conn.BeginTransaction();
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