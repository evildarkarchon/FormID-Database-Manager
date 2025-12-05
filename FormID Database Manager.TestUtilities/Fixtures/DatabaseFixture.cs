#nullable enable

using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FormID_Database_Manager.TestUtilities.Fixtures;

public class DatabaseFixture : IAsyncLifetime
{
    private SqliteConnection? _connection;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = "Data Source=:memory:";
        _connection = new SqliteConnection(ConnectionString);
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

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public async Task<SqliteConnection> CreateConnectionAsync()
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task InitializeSchemaAsync(SqliteConnection connection, string tableName)
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

    public async Task SeedDataAsync(SqliteConnection connection, string tableName, int recordCount = 10)
    {
        using var transaction = connection.BeginTransaction();

        for (var i = 0; i < recordCount; i++)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
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
