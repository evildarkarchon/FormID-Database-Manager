// Services/DatabaseService.cs

using System.IO;
using Mutagen.Bethesda;
using System.Data.SQLite;
using System.Threading.Tasks;

namespace FormID_Database_Manager.Services;

public class DatabaseService
{
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

    public async Task ClearPluginEntries(SQLiteConnection conn, GameRelease gameRelease, string pluginName)
    {
        await using var command = new SQLiteCommand(conn);
        command.CommandText = $"DELETE FROM {gameRelease} WHERE plugin = @plugin";
        command.Parameters.AddWithValue("@plugin", pluginName);
        await command.ExecuteNonQueryAsync();
    }

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

    public async Task OptimizeDatabase(SQLiteConnection conn)
    {
        await using var command = new SQLiteCommand("VACUUM", conn);
        await command.ExecuteNonQueryAsync();
    }
}