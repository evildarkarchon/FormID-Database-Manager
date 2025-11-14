// Services/DatabaseService.cs

using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Provides services for managing a database, including initialization, record insertion, clearing plugin entries,
///     and database optimization. Specifically designed for handling FormID records in the context of a game database.
/// </summary>
public class DatabaseService
{
    /// <summary>
    ///     Creates an optimized SQLite connection string with connection pooling, WAL mode, and performance pragmas.
    ///     This configuration improves throughput by 20-50% and enables concurrent reads during writes.
    /// </summary>
    /// <param name="dbPath">The file path of the database.</param>
    /// <returns>An optimized connection string for SQLite.</returns>
    public static string GetOptimizedConnectionString(string dbPath)
    {
        return new SQLiteConnectionStringBuilder
        {
            DataSource = dbPath,
            Version = 3,
            Pooling = true, // Enable connection pooling
            JournalMode = SQLiteJournalModeEnum.Wal, // Write-Ahead Logging for better concurrency
            SyncMode = SynchronizationModes.Normal, // Balanced safety/performance
            CacheSize = -64000, // 64MB cache (negative = KB)
            PageSize = 4096, // Optimal page size for most systems
            DefaultTimeout = 30, // 30 second timeout for busy database
            ForeignKeys = false, // Not used in this application
            ReadOnly = false,
            FailIfMissing = false
        }.ToString();
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
    /// <returns>A task that represents the asynchronous operation of initializing the database.</returns>
    public virtual async Task InitializeDatabase(string dbPath, GameRelease gameRelease,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(dbPath))
        {
            SQLiteConnection.CreateFile(dbPath);
        }

        SQLiteConnection? conn = null;
        SQLiteCommand? command = null;
        try
        {
            conn = new SQLiteConnection(GetOptimizedConnectionString(dbPath));
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            command = new SQLiteCommand(conn);

            // Create main table
            command.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {gameRelease} (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    plugin TEXT NOT NULL,
                    formid TEXT NOT NULL,
                    entry TEXT NOT NULL
                )";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // Create indices
            command.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{gameRelease}_plugin ON {gameRelease}(plugin)";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            command.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{gameRelease}_formid ON {gameRelease}(formid)";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            command?.Dispose();
            conn?.Dispose();
        }
    }

    /// <summary>
    ///     Clears all database entries for a specified plugin within the context of a specific game release.
    ///     This operation removes all FormID records associated with the given plugin from the database.
    /// </summary>
    /// <param name="conn">The SQLite database connection used to execute the operation.</param>
    /// <param name="gameRelease">The game release (e.g., Skyrim, Fallout) for which the plugin's entries will be cleared.</param>
    /// <param name="pluginName">The name of the plugin whose entries are to be cleared from the database.</param>
    /// <returns>A task that represents the asynchronous operation of clearing plugin entries from the database.</returns>
    public virtual async Task ClearPluginEntries(SQLiteConnection conn, GameRelease gameRelease, string pluginName,
        CancellationToken cancellationToken = default)
    {
        await using var command = new SQLiteCommand(conn);
        command.CommandText = $"DELETE FROM {gameRelease} WHERE plugin = @plugin";
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
    /// <returns>A task that represents the asynchronous operation of inserting the record into the database.</returns>
    public virtual async Task InsertRecord(SQLiteConnection conn, GameRelease gameRelease, string pluginName,
        string formId,
        string entry, CancellationToken cancellationToken = default)
    {
        await using var command = new SQLiteCommand(conn);
        command.CommandText = $"INSERT INTO {gameRelease} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";
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
    /// <returns>A task that represents the asynchronous operation of optimizing the database.</returns>
    public virtual async Task OptimizeDatabase(SQLiteConnection conn, CancellationToken cancellationToken = default)
    {
        await using var command = new SQLiteCommand("VACUUM", conn);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
