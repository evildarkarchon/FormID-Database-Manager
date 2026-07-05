using System.Runtime.ExceptionServices;
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
///     Owns SQLite writes for the FormID Record Store during one processing run.
/// </summary>
/// <remarks>
///     This module keeps plugin replacement atomic by owning the connection, batching, prepared commands,
///     per-plugin transactions, and FormID text staging table behind a small caller interface.
/// </remarks>
public sealed class FormIdRecordStore : IAsyncDisposable
{
    private const int PluginBatchSize = 1000;
    private const int TextStagingBatchSize = 10000;
    private const string StagingTableName = "temp_formid_record_staging";

    private readonly DatabaseService _databaseService;
    private readonly SqliteConnection _connection;
    private readonly string _tableName;
    private readonly List<(string PluginName, string FormId, string Entry)> _stagingBatch = new(TextStagingBatchSize);
    private SqliteCommand? _stagingInsertCommand;

    private FormIdRecordStore(DatabaseService databaseService, SqliteConnection connection, GameRelease gameRelease)
    {
        _databaseService = databaseService;
        _connection = connection;
        _tableName = GameReleaseHelper.GetSafeTableName(gameRelease);
    }

    /// <summary>
    ///     Opens a run-scoped SQLite FormID Record Store for the specified database and GameRelease.
    /// </summary>
    /// <param name="databaseService">The database service that owns existing schema and connection configuration logic.</param>
    /// <param name="databasePath">The SQLite database path.</param>
    /// <param name="gameRelease">The GameRelease whose FormID table will be written.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    /// <returns>An opened FormID Record Store that must be disposed after the processing run.</returns>
    public static async Task<FormIdRecordStore> OpenAsync(
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

            var store = new FormIdRecordStore(databaseService, connection, gameRelease);
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
    /// <param name="replaceExistingPluginRows">Whether existing rows for this Plugin should be replaced case-insensitively.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    public async Task WritePluginRecordsAsync(
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
            if (replaceExistingPluginRows)
            {
                await DeletePluginRowsAsync(pluginName, transaction, cancellationToken).ConfigureAwait(false);
            }

            await using var insertCommand = CreateTargetInsertCommand(transaction);
            var batch = new List<FormIdRecord>(PluginBatchSize);

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Add(record);

                if (batch.Count >= PluginBatchSize)
                {
                    FlushPluginBatch(insertCommand, pluginName, batch, cancellationToken);
                }
            }

            FlushPluginBatch(insertCommand, pluginName, batch, cancellationToken);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
    public async Task StageTextRecordAsync(
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
    public async Task CommitStagedTextRecordsAsync(
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
        return _databaseService.OptimizeDatabase(_connection, cancellationToken);
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
