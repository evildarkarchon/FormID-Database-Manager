using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FormID_Database_Manager.TestUtilities.Fixtures;

public class DatabaseFixture : IAsyncLifetime
{
    private SqliteConnection? _connection;

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Creates and opens the shared in-memory SQLite connection used by tests in this fixture.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        ConnectionString = "Data Source=:memory:";
        _connection = new SqliteConnection(ConnectionString);
        await _connection.OpenAsync();
    }

    /// <summary>
    /// Closes and disposes the shared SQLite connection when xUnit tears down the fixture.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }
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
        // CLASSIC schema - no NOT NULL constraints
        createTableCommand.CommandText = $@"
            CREATE TABLE IF NOT EXISTS {tableName} (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plugin TEXT,
                formid TEXT,
                entry TEXT
            )";
        await createTableCommand.ExecuteNonQueryAsync();
    }
}
