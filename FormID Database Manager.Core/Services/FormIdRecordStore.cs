using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     A plugin-scoped FormID row that can be written to the FormID Record Store.
/// </summary>
/// <param name="FormId">The hexadecimal FormID value to store.</param>
/// <param name="Entry">The human-readable Entry associated with the FormID.</param>
public readonly record struct FormIdRecord(string FormId, string Entry);

/// <summary>
///     Controls how newly ingested FormID records are applied to existing Plugin rows.
/// </summary>
public enum UpdateMode
{
    /// <summary>
    ///     Keep existing rows and append the newly ingested rows.
    /// </summary>
    Append,

    /// <summary>
    ///     Replace existing rows for each ingested Plugin, matching Plugin names case-insensitively.
    /// </summary>
    ReplacePluginRecords
}

/// <summary>
///     A stored FormID row read from the FormID Record Store.
/// </summary>
/// <param name="Plugin">The Plugin value stored with the row.</param>
/// <param name="FormId">The FormID value stored with the row.</param>
/// <param name="Entry">The Entry value stored with the row.</param>
public readonly record struct FormIdStoredRecord(string? Plugin, string? FormId, string? Entry);

/// <summary>
///     Query parameters for reading FormID records from the store.
/// </summary>
public sealed record FormIdRecordQuery
{
    /// <summary>
    ///     A query that returns all records for the store's GameRelease table.
    /// </summary>
    public static FormIdRecordQuery All { get; } = new();

    /// <summary>
    ///     Optional case-insensitive Plugin filter.
    /// </summary>
    public string? PluginName { get; init; }

    /// <summary>
    ///     Optional exact FormID filter.
    /// </summary>
    public string? FormId { get; init; }
}

/// <summary>
///     Progress reported while importing a FormID text file.
/// </summary>
/// <param name="Message">The user-facing progress message.</param>
/// <param name="Value">The optional progress percentage.</param>
public readonly record struct FormIdStoreProgress(string Message, double? Value);

/// <summary>
///     Result of importing a FormID text file into the store.
/// </summary>
/// <param name="PluginCount">The number of distinct Plugins encountered, matched case-insensitively.</param>
/// <param name="RecordCount">The number of valid FormID text rows imported.</param>
public readonly record struct FormIdTextFileImportResult(int PluginCount, int RecordCount);

/// <summary>
///     Result of writing one Plugin's FormID records into the store.
/// </summary>
/// <param name="RecordCount">The number of FormID records inserted for the Plugin.</param>
public readonly record struct FormIdPluginWriteResult(int RecordCount);

/// <summary>
///     Owns SQLite writes for the FormID Record Store during one processing run.
/// </summary>
/// <remarks>
///     This module keeps plugin replacement atomic by owning the connection, batching, prepared commands,
///     per-plugin transactions, and FormID text staging table behind a small caller interface.
/// </remarks>
public sealed class FormIdRecordStore : IAsyncDisposable
{
    private const int PluginBatchSize = 1000;
    private const int TextProgressInterval = 1000;
    private const int TextStagingBatchSize = 10000;
    private const string StagingTableName = "temp_formid_record_staging";

    private readonly SqliteConnection _connection;
    private readonly string _tableName;
    private readonly List<(string PluginName, string FormId, string Entry)> _stagingBatch = new(TextStagingBatchSize);
    private SqliteCommand? _stagingInsertCommand;

    private FormIdRecordStore(SqliteConnection connection, GameRelease gameRelease)
    {
        _connection = connection;
        _tableName = GameReleaseHelper.GetSafeTableName(gameRelease);
    }

    /// <summary>
    ///     Opens a run-scoped SQLite FormID Record Store for the specified database and GameRelease.
    /// </summary>
    /// <param name="databasePath">The SQLite database path.</param>
    /// <param name="gameRelease">The GameRelease whose FormID table will be used.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    /// <returns>An opened FormID Record Store that must be disposed after the processing run.</returns>
    public static Task<FormIdRecordStore> OpenAsync(
        string databasePath,
        GameRelease gameRelease,
        CancellationToken cancellationToken = default)
    {
        return OpenAsync(new DatabaseService(), databasePath, gameRelease, cancellationToken);
    }

    /// <summary>
    ///     Opens a run-scoped SQLite FormID Record Store for the specified database and GameRelease.
    /// </summary>
    /// <param name="databaseService">The database service that owns existing schema and connection configuration logic.</param>
    /// <param name="databasePath">The SQLite database path.</param>
    /// <param name="gameRelease">The GameRelease whose FormID table will be written.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    /// <returns>An opened FormID Record Store that must be disposed after the processing run.</returns>
    internal static async Task<FormIdRecordStore> OpenAsync(
        DatabaseService databaseService,
        string databasePath,
        GameRelease gameRelease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(databaseService);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        // Resolve the table name before opening a connection so unsupported releases fail through the whitelist seam.
        GameReleaseHelper.GetSafeTableName(gameRelease);

        await databaseService.InitializeDatabase(databasePath, gameRelease, cancellationToken).ConfigureAwait(false);

        var connection = new SqliteConnection(DatabaseService.GetOptimizedConnectionString(databasePath));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await databaseService.ConfigureConnection(connection, cancellationToken).ConfigureAwait(false);

            var store = new FormIdRecordStore(connection, gameRelease);
            await store.CreateStagingTableAsync(cancellationToken).ConfigureAwait(false);
            return store;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     Writes one Plugin's FormID records atomically, optionally replacing existing rows for that Plugin first.
    /// </summary>
    /// <param name="pluginName">The Plugin whose rows are being written.</param>
    /// <param name="records">The FormID records to insert for the Plugin.</param>
    /// <param name="updateMode">Controls whether rows are appended or replace existing rows for the Plugin.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    /// <returns>The number of records inserted for the Plugin.</returns>
    public Task<FormIdPluginWriteResult> WritePluginAsync(
        string pluginName,
        IEnumerable<FormIdRecord> records,
        UpdateMode updateMode,
        CancellationToken cancellationToken = default)
    {
        return WritePluginRecordsCoreAsync(
            pluginName,
            records,
            ShouldReplacePluginRows(updateMode),
            cancellationToken);
    }

    /// <summary>
    ///     Imports a FormID text file into the store using store-owned staging and Plugin-scoped commits.
    /// </summary>
    /// <param name="formIdTextFilePath">The FormID text file to import.</param>
    /// <param name="updateMode">Controls whether rows are appended or replace existing rows for each encountered Plugin.</param>
    /// <param name="progress">Optional progress reporter for user-facing import status.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    /// <returns>Counts describing the imported valid records and distinct Plugins.</returns>
    public async Task<FormIdTextFileImportResult> ImportFormIdTextFileAsync(
        string formIdTextFilePath,
        UpdateMode updateMode,
        IProgress<FormIdStoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formIdTextFilePath);

        var processedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recordCount = 0;

        // Use stream position for progress tracking; it avoids per-line UTF-8 byte counting on large files.
        var fileInfo = new FileInfo(formIdTextFilePath);
        var totalBytes = fileInfo.Length;

        progress?.Report(new FormIdStoreProgress("Starting processing...", 0));

        await using var stream =
            new FileStream(formIdTextFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
            {
                break;
            }

            var bytesRead = stream.Position;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!TryParseFormIdTextLine(line, out var pluginName, out var formId, out var entry))
            {
                continue;
            }

            recordCount++;

            if (recordCount % TextProgressInterval == 0)
            {
                var progressPercent = totalBytes > 0 ? (double)bytesRead / totalBytes * 100 : 0;
                progress?.Report(new FormIdStoreProgress(
                    $"Processing: {progressPercent:F1}% ({recordCount:N0} records)",
                    progressPercent));
            }

            if (processedPlugins.Add(pluginName) && updateMode == UpdateMode.ReplacePluginRecords)
            {
                progress?.Report(new FormIdStoreProgress($"Processing plugin: {pluginName}", null));
            }

            await StageTextRecordAsync(pluginName, formId, entry, cancellationToken).ConfigureAwait(false);
        }

        await CommitStagedTextRecordsAsync(ShouldReplacePluginRows(updateMode), cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new FormIdStoreProgress(
            $"Completed processing {processedPlugins.Count} plugins ({recordCount:N0} total records)",
            100));

        return new FormIdTextFileImportResult(processedPlugins.Count, recordCount);
    }

    /// <summary>
    ///     Reads records from the store's GameRelease table using store-owned query behavior.
    /// </summary>
    /// <param name="query">The query filter to apply.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    /// <returns>The matching records in insertion order.</returns>
    public async Task<IReadOnlyList<FormIdStoredRecord>> ReadRecordsAsync(
        FormIdRecordQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var records = new List<FormIdStoredRecord>();
        await using var command = _connection.CreateCommand();
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.PluginName))
        {
            filters.Add("plugin COLLATE NOCASE = @plugin");
            command.Parameters.AddWithValue("@plugin", query.PluginName);
        }

        if (!string.IsNullOrWhiteSpace(query.FormId))
        {
            filters.Add("formid = @formid");
            command.Parameters.AddWithValue("@formid", query.FormId);
        }

        var whereClause = filters.Count > 0 ? $" WHERE {string.Join(" AND ", filters)}" : string.Empty;
        command.CommandText = $"SELECT plugin, formid, entry FROM {_tableName}{whereClause} ORDER BY id";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(new FormIdStoredRecord(
                GetNullableString(reader, 0),
                GetNullableString(reader, 1),
                GetNullableString(reader, 2)));
        }

        return records;
    }

    /// <summary>
    ///     Reads the distinct Plugins that currently have at least one stored FormID record.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    /// <returns>A case-insensitive set of Plugin names.</returns>
    public async Task<IReadOnlySet<string>> ReadPluginsWithEntriesAsync(CancellationToken cancellationToken = default)
    {
        var plugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var command = _connection.CreateCommand();
        command.CommandText =
            $"SELECT DISTINCT plugin FROM {_tableName} WHERE plugin IS NOT NULL ORDER BY plugin COLLATE NOCASE";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            plugins.Add(reader.GetString(0));
        }

        return plugins;
    }

    private async Task<FormIdPluginWriteResult> WritePluginRecordsCoreAsync(
        string pluginName,
        IEnumerable<FormIdRecord> records,
        bool replaceExistingPluginRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentNullException.ThrowIfNull(records);

        await using var transaction = (SqliteTransaction)await _connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await using var insertCommand = CreateTargetInsertCommand(transaction);
            var batch = new List<FormIdRecord>(PluginBatchSize);
            var recordCount = 0;

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (recordCount == 0 && replaceExistingPluginRows)
                {
                    await DeletePluginRowsAsync(pluginName, transaction, cancellationToken).ConfigureAwait(false);
                }

                recordCount++;
                batch.Add(record);

                if (batch.Count >= PluginBatchSize)
                {
                    FlushPluginBatch(insertCommand, pluginName, batch, cancellationToken);
                }
            }

            FlushPluginBatch(insertCommand, pluginName, batch, cancellationToken);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new FormIdPluginWriteResult(recordCount);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     Stages one parsed FormID text row in SQLite for a later plugin-scoped commit.
    /// </summary>
    /// <param name="pluginName">The Plugin value from the FormID text row.</param>
    /// <param name="formId">The FormID value from the FormID text row.</param>
    /// <param name="entry">The Entry value from the FormID text row.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    internal async Task StageTextRecordAsync(
        string pluginName,
        string formId,
        string entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pluginName);
        ArgumentNullException.ThrowIfNull(formId);
        ArgumentNullException.ThrowIfNull(entry);

        cancellationToken.ThrowIfCancellationRequested();

        _stagingBatch.Add((pluginName, formId, entry));

        if (_stagingBatch.Count >= TextStagingBatchSize)
        {
            await FlushStagingBatchAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Commits all staged FormID text rows into the target table using one transaction per unique Plugin key.
    /// </summary>
    /// <param name="replaceExistingPluginRows">Whether existing rows for staged Plugins should be replaced case-insensitively.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    internal async Task CommitStagedTextRecordsAsync(
        bool replaceExistingPluginRows,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await FlushStagingBatchAsync(cancellationToken).ConfigureAwait(false);
            var pluginKeys = await GetStagedPluginKeysAsync(cancellationToken).ConfigureAwait(false);
            var failures = new List<Exception>();

            foreach (var pluginKey in pluginKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await CommitStagedPluginAsync(pluginKey, replaceExistingPluginRows, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(ex);
                }
            }

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            if (failures.Count > 1)
            {
                throw new AggregateException("One or more Plugin commits failed.", failures);
            }
        }
        finally
        {
            _stagingBatch.Clear();
            await ClearStagingTableBestEffortAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs database optimization for the store-owned connection.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    public Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        return new DatabaseService().OptimizeDatabase(_connection, cancellationToken);
    }

    /// <summary>
    ///     Releases the run-scoped SQLite connection and temporary staging resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DropStagingTableBestEffortAsync().ConfigureAwait(false);

        if (_stagingInsertCommand is not null)
        {
            await _stagingInsertCommand.DisposeAsync().ConfigureAwait(false);
            _stagingInsertCommand = null;
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task CreateStagingTableAsync(CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = $@"
            CREATE TEMP TABLE IF NOT EXISTS {StagingTableName} (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plugin_key TEXT NOT NULL COLLATE NOCASE,
                plugin TEXT NOT NULL,
                formid TEXT NOT NULL,
                entry TEXT NOT NULL
            )";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool TryParseFormIdTextLine(
        string line,
        out string pluginName,
        out string formId,
        out string entry)
    {
        var span = line.AsSpan();
        var firstPipe = span.IndexOf('|');
        if (firstPipe < 0)
        {
            pluginName = string.Empty;
            formId = string.Empty;
            entry = string.Empty;
            return false;
        }

        var rest = span[(firstPipe + 1)..];
        var secondPipe = rest.IndexOf('|');
        if (secondPipe < 0)
        {
            pluginName = string.Empty;
            formId = string.Empty;
            entry = string.Empty;
            return false;
        }

        var afterSecond = rest[(secondPipe + 1)..];
        if (afterSecond.IndexOf('|') >= 0)
        {
            pluginName = string.Empty;
            formId = string.Empty;
            entry = string.Empty;
            return false;
        }

        pluginName = span[..firstPipe].Trim().ToString();
        formId = rest[..secondPipe].Trim().ToString();
        entry = afterSecond.Trim().ToString();
        return true;
    }

    private static bool ShouldReplacePluginRows(UpdateMode updateMode)
    {
        return updateMode switch
        {
            UpdateMode.Append => false,
            UpdateMode.ReplacePluginRecords => true,
            _ => throw new ArgumentOutOfRangeException(nameof(updateMode), updateMode, "Unsupported update mode.")
        };
    }

    private static string? GetNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private SqliteCommand CreateTargetInsertCommand(SqliteTransaction transaction)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {_tableName} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";
        command.Parameters.Add(new SqliteParameter { ParameterName = "@plugin" });
        command.Parameters.Add(new SqliteParameter { ParameterName = "@formid" });
        command.Parameters.Add(new SqliteParameter { ParameterName = "@entry" });
        command.Prepare();
        return command;
    }

    private static void FlushPluginBatch(
        SqliteCommand insertCommand,
        string pluginName,
        List<FormIdRecord> batch,
        CancellationToken cancellationToken)
    {
        foreach (var record in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            insertCommand.Parameters["@plugin"].Value = pluginName;
            insertCommand.Parameters["@formid"].Value = record.FormId;
            insertCommand.Parameters["@entry"].Value = record.Entry;
            insertCommand.ExecuteNonQuery();
        }

        batch.Clear();
    }

    private async Task DeletePluginRowsAsync(
        string pluginName,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {_tableName} WHERE plugin COLLATE NOCASE = @plugin";
        command.Parameters.AddWithValue("@plugin", pluginName);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task FlushStagingBatchAsync(CancellationToken cancellationToken)
    {
        if (_stagingBatch.Count == 0)
        {
            return;
        }

        _stagingInsertCommand ??= CreateStagingInsertCommand();
        await using var transaction = (SqliteTransaction)await _connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        _stagingInsertCommand.Transaction = transaction;

        try
        {
            foreach (var (pluginName, formId, entry) in _stagingBatch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _stagingInsertCommand.Parameters["@plugin_key"].Value = pluginName;
                _stagingInsertCommand.Parameters["@plugin"].Value = pluginName;
                _stagingInsertCommand.Parameters["@formid"].Value = formId;
                _stagingInsertCommand.Parameters["@entry"].Value = entry;
                _stagingInsertCommand.ExecuteNonQuery();
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _stagingBatch.Clear();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private SqliteCommand CreateStagingInsertCommand()
    {
        var command = _connection.CreateCommand();
        command.CommandText = $@"
            INSERT INTO {StagingTableName} (plugin_key, plugin, formid, entry)
            VALUES (@plugin_key, @plugin, @formid, @entry)";
        command.Parameters.Add(new SqliteParameter { ParameterName = "@plugin_key" });
        command.Parameters.Add(new SqliteParameter { ParameterName = "@plugin" });
        command.Parameters.Add(new SqliteParameter { ParameterName = "@formid" });
        command.Parameters.Add(new SqliteParameter { ParameterName = "@entry" });
        command.Prepare();
        return command;
    }

    private async Task<List<string>> GetStagedPluginKeysAsync(CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pluginKeys = new List<string>();

        await using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT plugin_key FROM {StagingTableName} ORDER BY id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pluginKey = reader.GetString(0);
            if (seen.Add(pluginKey))
            {
                pluginKeys.Add(pluginKey);
            }
        }

        return pluginKeys;
    }

    private async Task CommitStagedPluginAsync(
        string pluginKey,
        bool replaceExistingPluginRows,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await _connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (replaceExistingPluginRows)
            {
                await DeletePluginRowsAsync(pluginKey, transaction, cancellationToken).ConfigureAwait(false);
            }

            await using var insertCommand = _connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $@"
                INSERT INTO {_tableName} (plugin, formid, entry)
                SELECT plugin, formid, entry
                FROM {StagingTableName}
                WHERE plugin_key COLLATE NOCASE = @plugin_key
                ORDER BY id";
            insertCommand.Parameters.AddWithValue("@plugin_key", pluginKey);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ClearStagingTableBestEffortAsync()
    {
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = $"DELETE FROM {StagingTableName}";
            await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Staging is temporary cleanup only; preserve the original processing exception.
        }
    }

    private async Task DropStagingTableBestEffortAsync()
    {
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = $"DROP TABLE IF EXISTS {StagingTableName}";
            await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The owning connection is being disposed anyway.
        }
    }
}
