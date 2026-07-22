using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using FormID_Database_Manager.Services;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Performance;

[SimpleJob(RunStrategy.ColdStart, 1, 1, 5)]
[MemoryDiagnoser]
public class DatabaseBenchmarks : IDisposable
{
    private string _databasePath = null!;
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

        // Generate test data
        _testData = GenerateTestData(RecordCount);

        // Prepare the full ready-on-return Store contract outside the measured database workloads.
        PrepareStoreAsync(_databasePath).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (string.IsNullOrWhiteSpace(_databasePath))
        {
            return;
        }

        SqliteConnection.ClearAllPools();

        DeleteTemporaryDatabaseFile(_databasePath);
        DeleteTemporaryDatabaseFile(_databasePath + "-wal");
        DeleteTemporaryDatabaseFile(_databasePath + "-shm");
    }

    [Benchmark(Baseline = true)]
    public async Task SingleInsert()
    {
        await using var conn = new SqliteConnection($"Data Source={_databasePath}");
        await conn.OpenAsync();

        // Clear existing data
        await ClearAllData(conn, GameRelease.SkyrimSE);

        foreach (var (plugin, formid, entry) in _testData)
        {
            await InsertRawRecord(conn, null, plugin, formid, entry);
        }
    }

    [Benchmark]
    public async Task BatchInsert_WithTransaction()
    {
        await using var conn = new SqliteConnection($"Data Source={_databasePath}");
        await conn.OpenAsync();

        // Clear existing data
        await ClearAllData(conn, GameRelease.SkyrimSE);

        await using var transaction = conn.BeginTransaction();
        try
        {
            foreach (var (plugin, formid, entry) in _testData)
            {
                await InsertRawRecord(conn, transaction, plugin, formid, entry);
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
        await using var conn = new SqliteConnection($"Data Source={_databasePath}");
        await conn.OpenAsync();

        // Clear existing data
        await ClearAllData(conn, GameRelease.SkyrimSE);

        // Use prepared statement for better performance
        await using var transaction = conn.BeginTransaction();
        await using var command = conn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO {GameRelease.SkyrimSE} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";

        // Create parameters once
        var pluginParam = new SqliteParameter("@plugin", SqliteType.Text);
        var formidParam = new SqliteParameter("@formid", SqliteType.Text);
        var entryParam = new SqliteParameter("@entry", SqliteType.Text);

        command.Parameters.Add(pluginParam);
        command.Parameters.Add(formidParam);
        command.Parameters.Add(entryParam);

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
    public async Task ReplacePluginRecords()
    {
        await using var conn = new SqliteConnection($"Data Source={_databasePath}");
        await conn.OpenAsync();

        // Ensure data exists
        if (await GetRecordCount(conn, GameRelease.SkyrimSE) == 0)
        {
            await InsertTestData();
        }

        // Replace entries for each plugin through the caller-facing store seam.
        var plugins = new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" };
        await using var store = await FormIdRecordStore.OpenAsync(_databasePath, GameRelease.SkyrimSE);
        foreach (var plugin in plugins)
        {
            await store.WritePluginAsync(
                plugin,
                [new FormIdRecord("00000000", "Replacement")],
                UpdateMode.ReplacePluginRecords);
        }
    }

    [Benchmark]
    public async Task SearchFormId()
    {
        await using var conn = new SqliteConnection($"Data Source={_databasePath}");
        await conn.OpenAsync();

        // Ensure data exists
        if (await GetRecordCount(conn, GameRelease.SkyrimSE) == 0)
        {
            await InsertTestData();
        }

        // Search for random FormIDs
        var random = new Random(42);
        await using var command = conn.CreateCommand();
        command.CommandText = $"SELECT * FROM {GameRelease.SkyrimSE} WHERE formid = @formid";
        var formidParam = new SqliteParameter("@formid", SqliteType.Text);
        command.Parameters.Add(formidParam);

        for (var i = 0; i < 100; i++)
        {
            var index = random.Next(_testData.Count);
            formidParam.Value = _testData[index].formid;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
            }
        }
    }

    /// <summary>
    ///     Measures explicit checkpointing and optimization through the Store-owned maintenance seam.
    /// </summary>
    [Benchmark]
    public async Task OptimizeFormIdRecordStore()
    {
        var hasData = false;
        await using (var conn = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await conn.OpenAsync();
            hasData = await GetRecordCount(conn, GameRelease.SkyrimSE) > 0;
        }

        // Ensure data exists
        if (!hasData)
        {
            await InsertTestData();
        }

        await using var store = await FormIdRecordStore.OpenAsync(_databasePath, GameRelease.SkyrimSE);
        await store.OptimizeAsync();
    }

    private async Task<long> GetRecordCount(SqliteConnection conn, GameRelease gameRelease)
    {
        await using var command = conn.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {gameRelease}";
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private async Task ClearAllData(SqliteConnection conn, GameRelease gameRelease)
    {
        await using var command = conn.CreateCommand();
        command.CommandText = $"DELETE FROM {gameRelease}";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertRawRecord(
        SqliteConnection conn,
        SqliteTransaction transaction,
        string plugin,
        string formid,
        string entry)
    {
        await using var command = conn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {GameRelease.SkyrimSE} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";
        command.Parameters.AddWithValue("@plugin", plugin);
        command.Parameters.AddWithValue("@formid", formid);
        command.Parameters.AddWithValue("@entry", entry);
        await command.ExecuteNonQueryAsync();
    }

    private static void DeleteTemporaryDatabaseFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Failed to delete temporary benchmark database file '{path}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Failed to delete temporary benchmark database file '{path}': {ex.Message}");
        }
    }

    private async Task InsertTestData()
    {
        await using var store = await FormIdRecordStore.OpenAsync(_databasePath, GameRelease.SkyrimSE);
        foreach (var pluginGroup in _testData.GroupBy(static record => record.plugin))
        {
            var records = pluginGroup.Select(static record => new FormIdRecord(record.formid, record.entry));
            await store.WritePluginAsync(pluginGroup.Key, records, UpdateMode.Append);
        }
    }

    /// <summary>
    ///     Opens and disposes a Store so benchmark setup includes schema, configuration, and staging readiness.
    /// </summary>
    private static async Task PrepareStoreAsync(string databasePath)
    {
        await using var store = await FormIdRecordStore.OpenAsync(databasePath, GameRelease.SkyrimSE);
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
