using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Performance;

[SimpleJob(RunStrategy.ColdStart, 1, 1, 5)]
[MemoryDiagnoser]
public class DatabaseBenchmarks : IDisposable
{
    private string _databasePath = null!;
    private DatabaseService _databaseService = null!;
    private List<(string plugin, string formid, string entry)> _testData = null!;

    [Params(1000, 10000, 100000)] public int RecordCount { get; set; }

    public void Dispose()
    {
        Cleanup();
    }

    [GlobalSetup]
    public void Setup()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"benchmark_{Guid.NewGuid()}.db");
        _databaseService = new DatabaseService();

        // Generate test data
        _testData = GenerateTestData(RecordCount);

        // Initialize database
        _databaseService.InitializeDatabase(_databasePath, GameRelease.SkyrimSE).Wait();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (File.Exists(_databasePath))
        {
            try
            {
                File.Delete(_databasePath);
            }
            catch { }
        }
    }

    [Benchmark(Baseline = true)]
    public async Task SingleInsert()
    {
        using var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
        await conn.OpenAsync();

        // Clear existing data
        await ClearAllData(conn, GameRelease.SkyrimSE);

        // Insert records one by one
        foreach (var (plugin, formid, entry) in _testData)
        {
            await _databaseService.InsertRecord(conn, GameRelease.SkyrimSE, plugin, formid, entry);
        }
    }

    [Benchmark]
    public async Task BatchInsert_WithTransaction()
    {
        using var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
        await conn.OpenAsync();

        // Clear existing data
        await ClearAllData(conn, GameRelease.SkyrimSE);

        // Insert all records in a transaction
        using var transaction = conn.BeginTransaction();
        try
        {
            foreach (var (plugin, formid, entry) in _testData)
            {
                await _databaseService.InsertRecord(conn, GameRelease.SkyrimSE, plugin, formid, entry);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    [Benchmark]
    public async Task BatchInsert_PreparedStatement()
    {
        using var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
        await conn.OpenAsync();

        // Clear existing data
        await ClearAllData(conn, GameRelease.SkyrimSE);

        // Use prepared statement for better performance
        using var transaction = conn.BeginTransaction();
        using var command = new SQLiteCommand(conn);
        command.CommandText =
            $"INSERT INTO {GameRelease.SkyrimSE} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";

        // Create parameters once
        var pluginParam = command.Parameters.Add("@plugin", DbType.String);
        var formidParam = command.Parameters.Add("@formid", DbType.String);
        var entryParam = command.Parameters.Add("@entry", DbType.String);

        try
        {
            foreach (var (plugin, formid, entry) in _testData)
            {
                pluginParam.Value = plugin;
                formidParam.Value = formid;
                entryParam.Value = entry;
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    [Benchmark]
    public async Task ClearPluginEntries()
    {
        using var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
        await conn.OpenAsync();

        // Ensure data exists
        if (await GetRecordCount(conn, GameRelease.SkyrimSE) == 0)
        {
            await InsertTestData(conn);
        }

        // Clear entries for each plugin
        var plugins = new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" };
        foreach (var plugin in plugins)
        {
            await _databaseService.ClearPluginEntries(conn, GameRelease.SkyrimSE, plugin);
        }
    }

    [Benchmark]
    public async Task SearchFormId()
    {
        using var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
        await conn.OpenAsync();

        // Ensure data exists
        if (await GetRecordCount(conn, GameRelease.SkyrimSE) == 0)
        {
            await InsertTestData(conn);
        }

        // Search for random FormIDs
        var random = new Random(42);
        using var command = new SQLiteCommand(conn);
        command.CommandText = $"SELECT * FROM {GameRelease.SkyrimSE} WHERE formid = @formid";
        var formidParam = command.Parameters.Add("@formid", DbType.String);

        for (var i = 0; i < 100; i++)
        {
            var index = random.Next(_testData.Count);
            formidParam.Value = _testData[index].formid;
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
            }
        }
    }

    [Benchmark]
    public async Task OptimizeDatabase()
    {
        using var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
        await conn.OpenAsync();

        // Ensure data exists
        if (await GetRecordCount(conn, GameRelease.SkyrimSE) == 0)
        {
            await InsertTestData(conn);
        }

        await _databaseService.OptimizeDatabase(conn);
    }

    private async Task<long> GetRecordCount(SQLiteConnection conn, GameRelease gameRelease)
    {
        using var command = new SQLiteCommand($"SELECT COUNT(*) FROM {gameRelease}", conn);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private async Task ClearAllData(SQLiteConnection conn, GameRelease gameRelease)
    {
        using var command = new SQLiteCommand($"DELETE FROM {gameRelease}", conn);
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertTestData(SQLiteConnection conn)
    {
        using var transaction = conn.BeginTransaction();
        foreach (var (plugin, formid, entry) in _testData)
        {
            await _databaseService.InsertRecord(conn, GameRelease.SkyrimSE, plugin, formid, entry);
        }

        transaction.Commit();
    }

    private List<(string plugin, string formid, string entry)> GenerateTestData(int count)
    {
        var data = new List<(string plugin, string formid, string entry)>(count);
        var plugins = new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" };
        var random = new Random(42);

        for (var i = 0; i < count; i++)
        {
            var plugin = plugins[random.Next(plugins.Length)];
            var formId = $"{random.Next(0x00000001, 0x00FFFFFF):X8}";
            var entry = $"TestEntry_{i}";

            data.Add((plugin, formId, entry));
        }

        return data;
    }
}
