// Services/DatabaseService.cs

using System.IO;
using Mutagen.Bethesda;
using System.Data.SQLite;
using System.Threading.Tasks;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Provides services for managing a database, including initialization, record insertion, clearing plugin entries,
/// and database optimization. Specifically designed for handling FormID records in the context of a game database.
/// </summary>
public class DatabaseService
{
    /// <summary>
    /// Initializes a database for managing FormID records. If the specified database file does not exist,
    /// it is created. Tables and indices required for storing and querying FormID records are created
    /// if they do not already exist.
    /// </summary>
    /// <param name="dbPath">The file path of the database to initialize.</param>
    /// <param name="gameRelease">The specific game release (e.g., Skyrim, Fallout) for which the database is being initialized.</param>
    /// <returns>A task that represents the asynchronous operation of initializing the database.</returns>
    public async Task InitializeDatabase(string dbPath, GameRelease gameRelease)
    {
        if (!File.Exists(dbPath))
        {
            SQLiteConnection.CreateFile(dbPath);
        }

        await using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
        await conn.OpenAsync();
        await using var command = new SQLiteCommand(conn);

        // Create main table
        command.CommandText = $@"
            CREATE TABLE IF NOT EXISTS {gameRelease} (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plugin TEXT NOT NULL,
                formid TEXT NOT NULL,
                entry TEXT NOT NULL
            )";
        await command.ExecuteNonQueryAsync();

        // Create indices
        command.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{gameRelease}_plugin ON {gameRelease}(plugin)";
        await command.ExecuteNonQueryAsync();

        command.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{gameRelease}_formid ON {gameRelease}(formid)";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Clears all database entries for a specified plugin within the context of a specific game release.
    /// This operation removes all FormID records associated with the given plugin from the database.
    /// </summary>
    /// <param name="conn">The SQLite database connection used to execute the operation.</param>
    /// <param name="gameRelease">The game release (e.g., Skyrim, Fallout) for which the plugin's entries will be cleared.</param>
    /// <param name="pluginName">The name of the plugin whose entries are to be cleared from the database.</param>
    /// <returns>A task that represents the asynchronous operation of clearing plugin entries from the database.</returns>
    public async Task ClearPluginEntries(SQLiteConnection conn, GameRelease gameRelease, string pluginName)
    {
        await using var command = new SQLiteCommand(conn);
        command.CommandText = $"DELETE FROM {gameRelease} WHERE plugin = @plugin";
        command.Parameters.AddWithValue("@plugin", pluginName);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts a new record into the database for a specific game release, plugin, and FormID.
    /// </summary>
    /// <param name="conn">An open SQLite connection to the database.</param>
    /// <param name="gameRelease">The specific game release (e.g., Skyrim, Fallout) for which the record is being inserted.</param>
    /// <param name="pluginName">The name of the plugin associated with the record.</param>
    /// <param name="formId">The FormID associated with the record.</param>
    /// <param name="entry">The entry details to be stored in the database.</param>
    /// <returns>A task that represents the asynchronous operation of inserting the record into the database.</returns>
    public async Task InsertRecord(SQLiteConnection conn, GameRelease gameRelease, string pluginName, string formId,
        string entry)
    {
        await using var command = new SQLiteCommand(conn);
        command.CommandText = $"INSERT INTO {gameRelease} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";
        command.Parameters.AddWithValue("@plugin", pluginName);
        command.Parameters.AddWithValue("@formid", formId);
        command.Parameters.AddWithValue("@entry", entry);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Optimizes the database by rebuilding the entire database file to reclaim unused space
    /// and improve query performance. This operation can help reduce database file size
    /// and enhance overall efficiency.
    /// </summary>
    /// <param name="conn">The SQLite database connection used to execute the optimization command. The connection must be open before calling this method.</param>
    /// <returns>A task that represents the asynchronous operation of optimizing the database.</returns>
    public async Task OptimizeDatabase(SQLiteConnection conn)
    {
        await using var command = new SQLiteCommand("VACUUM", conn);
        await command.ExecuteNonQueryAsync();
    }
}