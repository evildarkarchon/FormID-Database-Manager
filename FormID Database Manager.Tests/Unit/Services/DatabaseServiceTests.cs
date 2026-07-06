#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Fixtures;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

[Collection("Database Tests")]
public class DatabaseServiceTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseService _service = new();
    private SqliteConnection? _connection;

    public async ValueTask InitializeAsync()
    {
        _connection = await fixture.CreateConnectionAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(GameRelease.SkyrimSE)]
    [InlineData(GameRelease.SkyrimVR)]
    [InlineData(GameRelease.Fallout4)]
    [InlineData(GameRelease.Starfield)]
    [InlineData(GameRelease.Oblivion)]
    public async Task InitializeDatabase_CreatesTableForEachGameRelease(GameRelease gameRelease)
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        try
        {
            await _service.InitializeDatabase(tempDbPath, gameRelease, TestContext.Current.CancellationToken);

            await using (var conn = new SqliteConnection($"Data Source={tempDbPath}"))
            {
                await conn.OpenAsync(TestContext.Current.CancellationToken);

                await using var command = conn.CreateCommand();
                command.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{gameRelease}'";

                var tableName = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

                Assert.NotNull(tableName);
                Assert.Equal(gameRelease.ToString(), tableName);
            }

            // Force garbage collection to ensure SQLite releases the file
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDbPath))
            {
                File.Delete(tempDbPath);
            }
        }
    }

    [Fact]
    public async Task InitializeDatabase_CreatesIndicesForTable()
    {
        var gameRelease = GameRelease.SkyrimSE;
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");

        try
        {
            await _service.InitializeDatabase(tempDbPath, gameRelease, TestContext.Current.CancellationToken);

            await using (var conn = new SqliteConnection($"Data Source={tempDbPath}"))
            {
                await conn.OpenAsync(TestContext.Current.CancellationToken);

                await using var command = conn.CreateCommand();
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name=@table";
                command.Parameters.AddWithValue("@table", gameRelease.ToString());

                var indices = new List<string>();
                await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
                while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                {
                    indices.Add(reader.GetString(0));
                }

                Assert.Contains($"{gameRelease}_covering_idx", indices);
                Assert.Contains($"{gameRelease}_plugin_idx", indices);
                Assert.DoesNotContain($"{gameRelease}_index", indices);
            }

            // Force garbage collection to ensure SQLite releases the file
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDbPath))
            {
                File.Delete(tempDbPath);
            }
        }
    }

    [Fact]
    public async Task InitializeDatabase_HandlesExistingDatabase()
    {
        var gameRelease = GameRelease.SkyrimSE;
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");

        try
        {
            await _service.InitializeDatabase(tempDbPath, gameRelease, TestContext.Current.CancellationToken);

            await _service.InitializeDatabase(tempDbPath, gameRelease, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(tempDbPath));
        }
        finally
        {
            // Force garbage collection to ensure SQLite releases the file
            GC.Collect();
            GC.WaitForPendingFinalizers();

            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDbPath))
            {
                File.Delete(tempDbPath);
            }
        }
    }

    [Fact]
    public async Task InitializeDatabase_ThrowsOnCancellation()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        try
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<TaskCanceledException>(() =>
                _service.InitializeDatabase(tempDbPath, GameRelease.SkyrimSE, cts.Token));
        }
        finally
        {
            // Force garbage collection to ensure SQLite releases the file
            GC.Collect();
            GC.WaitForPendingFinalizers();

            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDbPath))
            {
                File.Delete(tempDbPath);
            }
        }
    }

    [Fact]
    public async Task OptimizeDatabase_ExecutesSuccessfully()
    {
        var gameRelease = GameRelease.SkyrimSE;
        await fixture.InitializeSchemaAsync(_connection!, gameRelease.ToString());

        for (var i = 0; i < 100; i++)
        {
            await InsertRawRecordAsync(_connection!, gameRelease, "Plugin.esp", $"0x{i:X8}", $"Entry{i}");
        }

        await _service.OptimizeDatabase(_connection!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DatabaseOperations_HandleConcurrentAccess()
    {
        var gameRelease = GameRelease.SkyrimSE;
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"concurrent_{Guid.NewGuid()}.db");

        try
        {
            await _service.InitializeDatabase(tempDbPath, gameRelease, TestContext.Current.CancellationToken);

            var tasks = new List<Task>();

            for (var i = 0; i < 5; i++)
            {
                var pluginIndex = i;
                var pluginName = $"ConcurrentPlugin{pluginIndex}.esp";
                tasks.Add(Task.Run(async () =>
                {
                    await using var conn = new SqliteConnection($"Data Source={tempDbPath}");
                    await conn.OpenAsync(TestContext.Current.CancellationToken);
                    for (var j = 0; j < 20; j++)
                    {
                        await InsertRawRecordAsync(conn, gameRelease, pluginName, $"0x{pluginIndex:X4}{j:X4}",
                            $"Entry_{pluginIndex}_{j}");
                    }
                }, TestContext.Current.CancellationToken));
            }

            await Task.WhenAll(tasks);

            await using var verifyConn = new SqliteConnection($"Data Source={tempDbPath}");
            await verifyConn.OpenAsync(TestContext.Current.CancellationToken);
            var command = verifyConn.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {gameRelease}";
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

            Assert.Equal(100, count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDbPath))
            {
                File.Delete(tempDbPath);
            }
        }
    }

    private static async Task InsertRawRecordAsync(
        SqliteConnection connection,
        GameRelease gameRelease,
        string pluginName,
        string formId,
        string entry)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {gameRelease} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";
        command.Parameters.AddWithValue("@plugin", pluginName);
        command.Parameters.AddWithValue("@formid", formId);
        command.Parameters.AddWithValue("@entry", entry);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
