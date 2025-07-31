using System;
using System.Threading.Tasks;
using System.Data.SQLite;
using Xunit;

#nullable enable

namespace FormID_Database_Manager.TestUtilities.Fixtures;

public class DatabaseFixture : IAsyncLifetime
{
    private SQLiteConnection? _connection;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = "Data Source=:memory:";
        _connection = new SQLiteConnection(ConnectionString);
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }
    }

    public SQLiteConnection CreateConnection()
    {
        var connection = new SQLiteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public async Task<SQLiteConnection> CreateConnectionAsync()
    {
        var connection = new SQLiteConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task InitializeSchemaAsync(SQLiteConnection connection, string tableName)
    {
        var createTableCommand = connection.CreateCommand();
        createTableCommand.CommandText = $@"
            CREATE TABLE IF NOT EXISTS {tableName} (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plugin TEXT NOT NULL,
                formid TEXT NOT NULL,
                entry TEXT NOT NULL
            )";
        await createTableCommand.ExecuteNonQueryAsync();
    }

    public async Task SeedDataAsync(SQLiteConnection connection, string tableName, int recordCount = 10)
    {
        using var transaction = connection.BeginTransaction();

        for (int i = 0; i < recordCount; i++)
        {
            var command = connection.CreateCommand();
            command.CommandText = $@"
                INSERT INTO {tableName} (plugin, formid, entry)
                VALUES (@plugin, @formid, @entry)";

            command.Parameters.AddWithValue("@plugin", $"TestPlugin{i}.esp");
            command.Parameters.AddWithValue("@formid", $"0x{i:X8}");
            command.Parameters.AddWithValue("@entry", $"TestEntry{i}");

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}
