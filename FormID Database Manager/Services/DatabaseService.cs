// Services/DatabaseService.cs

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Provides services for managing a database, including initialization, record insertion, clearing plugin entries,
///     and database optimization. Specifically designed for handling FormID records in the context of a game database.
/// </summary>
public class DatabaseService
{
    /// <summary>
    ///     Creates an optimized SQLite connection string with connection pooling.
    ///     Additional performance pragmas (WAL mode, etc.) are applied via ConfigureConnection.
    /// </summary>
    /// <param name="dbPath">The file path of the database.</param>
    /// <returns>An optimized connection string for SQLite.</returns>
    public static string GetOptimizedConnectionString(string dbPath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = true,
            Cache = SqliteCacheMode.Default,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    /// <summary>
    ///     Configures the connection with performance pragmas like WAL mode and synchronous settings.
    /// </summary>
    /// <param name="conn">The open connection to configure.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    public virtual async Task ConfigureConnection(SqliteConnection conn, CancellationToken cancellationToken = default)
    {
        await using var command = conn.CreateCommand();
        command.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA cache_size = -64000;
            PRAGMA mmap_size = 268435456;
            PRAGMA temp_store = MEMORY;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Initializes a database for managing FormID records. If the specified database file does not exist,
    ///     it is created. Tables and indices required for storing and querying FormID records are created
    ///     if they do not already exist.
    /// </summary>
    /// <param name="dbPath">The file path of the database to initialize.</param>
    /// <param name="gameRelease">
    ///     The specific game release (e.g., Skyrim, Fallout) for which the database is being
    ///     initialized.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns>A task that represents the asynchronous operation of initializing the database.</returns>
    public virtual async Task InitializeDatabase(string dbPath, GameRelease gameRelease,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(GetOptimizedConnectionString(dbPath));
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureConnection(conn, cancellationToken).ConfigureAwait(false);

        await using var command = conn.CreateCommand();
        var tableName = GameReleaseHelper.GetSafeTableName(gameRelease);

        // Create main table (CLASSIC schema - no NOT NULL constraints)
        command.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {tableName} (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    plugin TEXT,
                    formid TEXT,
                    entry TEXT
                )";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Covering index for SELECT queries (formid + plugin + entry covers typical lookups)
        command.CommandText = $"CREATE INDEX IF NOT EXISTS {tableName}_covering_idx ON {tableName}(formid, plugin COLLATE nocase, entry)";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Plugin-only index for DELETE performance (WHERE plugin = @plugin)
        command.CommandText = $"CREATE INDEX IF NOT EXISTS {tableName}_plugin_idx ON {tableName}(plugin COLLATE nocase)";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Drop legacy redundant index if it exists (covered by covering_idx)
        command.CommandText = $"DROP INDEX IF EXISTS {tableName}_index";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Clears all database entries for a specified plugin within the context of a specific game release.
    ///     This operation removes all FormID records associated with the given plugin from the database.
    /// </summary>
    /// <param name="conn">The SQLite database connection used to execute the operation.</param>
    /// <param name="gameRelease">The game release (e.g., Skyrim, Fallout) for which the plugin's entries will be cleared.</param>
    /// <param name="pluginName">The name of the plugin whose entries are to be cleared from the database.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A task that represents the asynchronous operation of clearing plugin entries from the database.</returns>
    public virtual async Task ClearPluginEntries(SqliteConnection conn, GameRelease gameRelease, string pluginName,
        CancellationToken cancellationToken = default)
    {
        await using var command = conn.CreateCommand();
        var tableName = GameReleaseHelper.GetSafeTableName(gameRelease);
        command.CommandText = $"DELETE FROM {tableName} WHERE plugin = @plugin";
        command.Parameters.AddWithValue("@plugin", pluginName);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Inserts a new record into the database for a specific game release, plugin, and FormID.
    /// </summary>
    /// <param name="conn">An open SQLite connection to the database.</param>
    /// <param name="gameRelease">The specific game release (e.g., Skyrim, Fallout) for which the record is being inserted.</param>
    /// <param name="pluginName">The name of the plugin associated with the record.</param>
    /// <param name="formId">The FormID associated with the record.</param>
    /// <param name="entry">The entry details to be stored in the database.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A task that represents the asynchronous operation of inserting the record into the database.</returns>
    public virtual async Task InsertRecord(SqliteConnection conn, GameRelease gameRelease, string pluginName,
        string formId,
        string entry, CancellationToken cancellationToken = default)
    {
        await using var command = conn.CreateCommand();
        var tableName = GameReleaseHelper.GetSafeTableName(gameRelease);
        command.CommandText = $"INSERT INTO {tableName} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";
        command.Parameters.AddWithValue("@plugin", pluginName);
        command.Parameters.AddWithValue("@formid", formId);
        command.Parameters.AddWithValue("@entry", entry);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Optimizes the database by rebuilding the entire database file to reclaim unused space
    ///     and improve query performance. This operation can help reduce database file size
    ///     and enhance overall efficiency.
    /// </summary>
    /// <param name="conn">
    ///     The SQLite database connection used to execute the optimization command. The connection must be open
    ///     before calling this method.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns>A task that represents the asynchronous operation of optimizing the database.</returns>
    public virtual async Task OptimizeDatabase(SqliteConnection conn, CancellationToken cancellationToken = default)
    {
        await using var command = conn.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA optimize;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
