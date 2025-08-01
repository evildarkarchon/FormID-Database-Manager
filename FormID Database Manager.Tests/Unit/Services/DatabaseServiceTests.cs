using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Data.SQLite;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Fixtures;
using Mutagen.Bethesda;
using Xunit;

#nullable enable

namespace FormID_Database_Manager.Tests.Unit.Services;

[Collection("Database Tests")]
public class DatabaseServiceTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private readonly DatabaseService _service;
    private SQLiteConnection? _connection;

    public DatabaseServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _service = new DatabaseService();
    }

    public async Task InitializeAsync()
    {
        _connection = await _fixture.CreateConnectionAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
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
            await _service.InitializeDatabase(tempDbPath, gameRelease);

            using (var conn = new SQLiteConnection($"Data Source={tempDbPath}"))
            {
                await conn.OpenAsync();

                using var command = conn.CreateCommand();
                command.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{gameRelease}'";

                var tableName = await command.ExecuteScalarAsync();

                Assert.NotNull(tableName);
                Assert.Equal(gameRelease.ToString(), tableName);
            }

            // Force garbage collection to ensure SQLite releases the file
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
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
            await _service.InitializeDatabase(tempDbPath, gameRelease);

            using (var conn = new SQLiteConnection($"Data Source={tempDbPath}"))
            {
                await conn.OpenAsync();

                using var command = conn.CreateCommand();
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name=@table";
                command.Parameters.AddWithValue("@table", gameRelease.ToString());

                var indices = new List<string>();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    indices.Add(reader.GetString(0));
                }

                Assert.Contains($"idx_{gameRelease}_plugin", indices);
                Assert.Contains($"idx_{gameRelease}_formid", indices);
            }

            // Force garbage collection to ensure SQLite releases the file
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
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
            await _service.InitializeDatabase(tempDbPath, gameRelease);

            await _service.InitializeDatabase(tempDbPath, gameRelease);

            Assert.True(File.Exists(tempDbPath));
        }
        finally
        {
            // Force garbage collection to ensure SQLite releases the file
            GC.Collect();
            GC.WaitForPendingFinalizers();

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
            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(
                () => _service.InitializeDatabase(tempDbPath, GameRelease.SkyrimSE, cts.Token));
        }
        finally
        {
            // Force garbage collection to ensure SQLite releases the file
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (File.Exists(tempDbPath))
            {
                File.Delete(tempDbPath);
            }
        }
    }

    [Fact]
    public async Task InsertRecord_InsertsDataCorrectly()
    {
        var gameRelease = GameRelease.SkyrimSE;
        await _fixture.InitializeSchemaAsync(_connection!, gameRelease.ToString());

        await _service.InsertRecord(_connection!, gameRelease, "TestPlugin.esp", "0x00000001", "TestNPC");

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {gameRelease}";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(1, count);

        command.CommandText = $"SELECT plugin, formid, entry FROM {gameRelease} WHERE formid = '0x00000001'";
        using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("TestPlugin.esp", reader.GetString(0));
        Assert.Equal("0x00000001", reader.GetString(1));
        Assert.Equal("TestNPC", reader.GetString(2));
    }

    [Fact]
    public async Task InsertRecord_HandlesSpecialCharacters()
    {
        var gameRelease = GameRelease.SkyrimSE;
        await _fixture.InitializeSchemaAsync(_connection!, gameRelease.ToString());

        var specialEntry = "Test'Entry\"With;Special--Characters";
        await _service.InsertRecord(_connection!, gameRelease, "Plugin.esp", "0x00000001", specialEntry);

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT entry FROM {gameRelease} WHERE formid = '0x00000001'";
        var result = await command.ExecuteScalarAsync();

        Assert.Equal(specialEntry, result);
    }

    [Fact]
    public async Task ClearPluginEntries_RemovesOnlySpecifiedPlugin()
    {
        var gameRelease = GameRelease.SkyrimSE;
        await _fixture.InitializeSchemaAsync(_connection!, gameRelease.ToString());

        await _service.InsertRecord(_connection!, gameRelease, "Plugin1.esp", "0x00000001", "Entry1");
        await _service.InsertRecord(_connection!, gameRelease, "Plugin1.esp", "0x00000002", "Entry2");
        await _service.InsertRecord(_connection!, gameRelease, "Plugin2.esp", "0x00000003", "Entry3");

        await _service.ClearPluginEntries(_connection!, gameRelease, "Plugin1.esp");

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {gameRelease} WHERE plugin = 'Plugin1.esp'";
        var plugin1Count = Convert.ToInt32(await command.ExecuteScalarAsync());

        command.CommandText = $"SELECT COUNT(*) FROM {gameRelease} WHERE plugin = 'Plugin2.esp'";
        var plugin2Count = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(0, plugin1Count);
        Assert.Equal(1, plugin2Count);
    }

    [Fact]
    public async Task ClearPluginEntries_HandlesNonExistentPlugin()
    {
        var gameRelease = GameRelease.SkyrimSE;
        await _fixture.InitializeSchemaAsync(_connection!, gameRelease.ToString());

        await _service.ClearPluginEntries(_connection!, gameRelease, "NonExistent.esp");
    }

    [Fact]
    public async Task OptimizeDatabase_ExecutesSuccessfully()
    {
        var gameRelease = GameRelease.SkyrimSE;
        await _fixture.InitializeSchemaAsync(_connection!, gameRelease.ToString());

        for (int i = 0; i < 100; i++)
        {
            await _service.InsertRecord(_connection!, gameRelease, "Plugin.esp", $"0x{i:X8}", $"Entry{i}");
        }

        await _service.OptimizeDatabase(_connection!);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public async Task BatchInsertPerformance_HandlesVariousSizes(int batchSize)
    {
        var gameRelease = GameRelease.SkyrimSE;
        await _fixture.InitializeSchemaAsync(_connection!, gameRelease.ToString());

        var tasks = new List<Task>();
        for (int i = 0; i < batchSize; i++)
        {
            tasks.Add(_service.InsertRecord(_connection!, gameRelease, "BatchPlugin.esp", $"0x{i:X8}", $"BatchEntry{i}"));
        }

        await Task.WhenAll(tasks);

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {gameRelease}";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(batchSize, count);
    }

    [Fact]
    public async Task DatabaseOperations_HandleConcurrentAccess()
    {
        var gameRelease = GameRelease.SkyrimSE;
        await _fixture.InitializeSchemaAsync(_connection!, gameRelease.ToString());

        var tasks = new List<Task>();

        for (int i = 0; i < 5; i++)
        {
            var pluginName = $"ConcurrentPlugin{i}.esp";
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < 20; j++)
                {
                    await _service.InsertRecord(_connection!, gameRelease, pluginName, $"0x{i:X4}{j:X4}", $"Entry_{i}_{j}");
                }
            }));
        }

        await Task.WhenAll(tasks);

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {gameRelease}";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(100, count);
    }
}
