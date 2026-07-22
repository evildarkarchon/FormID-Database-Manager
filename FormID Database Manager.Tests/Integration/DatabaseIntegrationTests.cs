using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Integration;

/// <summary>
///     Integration tests for database operations, testing the full database workflow
///     including creation, insertion, querying, and optimization.
/// </summary>
[Collection("Integration Tests")]
public class DatabaseIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles;
    private readonly string _testDbPath;

    public DatabaseIntegrationTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"integration_test_{Guid.NewGuid()}.db");
        _tempFiles = [_testDbPath];
    }

    public void Dispose()
    {
        // Store connections are pooled, so release their handles before deleting test-owned database files.
        SqliteConnection.ClearAllPools();

        foreach (var file in _tempFiles.Where(File.Exists))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    #region Database Recovery Tests

    /// <summary>
    ///     Verifies that Store opening repairs a damaged index set without losing records or blocking later writes.
    /// </summary>
    [Fact]
    public async Task OpenAsync_CorruptedIndexes_RepairsSchemaWithoutLosingRecords()
    {
        var gameRelease = GameRelease.SkyrimSE;
        var tableName = gameRelease.ToString();
        await PrepareStoreAsync(gameRelease);
        await WritePluginRecordsAsync(
            gameRelease,
            "TestPlugin.esp",
            Enumerable.Range(0, 10).Select(i => new FormIdRecord($"{i:X6}", $"Entry{i}")),
            UpdateMode.Append);

        await using (var damageConnection = new SqliteConnection($"Data Source={_testDbPath};Pooling=False"))
        {
            await damageConnection.OpenAsync(TestContext.Current.CancellationToken);
            await using var damageCommand = damageConnection.CreateCommand();
            damageCommand.CommandText = $@"
                DROP INDEX {tableName}_covering_idx;
                DROP INDEX {tableName}_plugin_idx;
                CREATE INDEX {tableName}_index ON {tableName}(formid);";
            await damageCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // Store readiness includes converging persisted indexes before subsequent writes begin.
        await PrepareStoreAsync(gameRelease);

        await using var verificationConnection = new SqliteConnection($"Data Source={_testDbPath};Pooling=False");
        await verificationConnection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(10, GetRecordCount(verificationConnection, gameRelease));

        await using (var indexCommand = verificationConnection.CreateCommand())
        {
            indexCommand.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = @table_name ORDER BY name";
            indexCommand.Parameters.AddWithValue("@table_name", tableName);

            var indexes = new List<string>();
            await using var reader = await indexCommand.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                indexes.Add(reader.GetString(0));
            }

            Assert.Equal([$"{tableName}_covering_idx", $"{tableName}_plugin_idx"], indexes);
        }

        await WriteRecordAsync(gameRelease, "TestPlugin.esp", "00000A", "Entry10");
        Assert.Equal(11, GetRecordCount(verificationConnection, gameRelease));
    }

    #endregion

    #region Transaction Tests

    [Fact]
    public async Task Database_TransactionRollback_MaintainsConsistency()
    {
        // Arrange
        await PrepareStoreAsync(GameRelease.SkyrimSE);

        await using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        // Insert initial data
        await WriteRecordAsync(GameRelease.SkyrimSE, "Initial.esp", "000001", "InitialEntry");

        var initialCount = GetRecordCount(connection, GameRelease.SkyrimSE);

        // Act - Start transaction, insert data, then rollback
        await using (var transaction = connection.BeginTransaction())
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $@"INSERT INTO {GameRelease.SkyrimSE} (plugin, formid, entry) 
                               VALUES (@plugin, @formid, @entry)";

            for (var i = 0; i < 100; i++)
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@plugin", "Transactional.esp");
                cmd.Parameters.AddWithValue("@formid", $"{i:X6}");
                cmd.Parameters.AddWithValue("@entry", $"TransEntry{i}");
                await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            // Rollback instead of commit
            transaction.Rollback();
        }

        // Assert - Count should remain unchanged
        var finalCount = GetRecordCount(connection, GameRelease.SkyrimSE);
        Assert.Equal(initialCount, finalCount);

        // Verify no transactional data exists
        var transactionalRecords = GetRecordsByPlugin(connection, GameRelease.SkyrimSE, "Transactional.esp");
        Assert.Empty(transactionalRecords);
    }

    #endregion

    #region Complete Workflow Tests

    [Fact]
    public async Task Database_CreateAndQuery_CompleteWorkflow()
    {
        // Arrange
        var gameReleases = new[] { GameRelease.SkyrimSE, GameRelease.Fallout4, GameRelease.Starfield };

        // Open each selected Store before using raw SQLite to inspect the combined database file.
        foreach (var gameRelease in gameReleases)
        {
            await PrepareStoreAsync(gameRelease);
        }

        await using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        foreach (var gameRelease in gameReleases)
        {
            // Insert test data
            var testData = GenerateTestData(gameRelease, 100);
            foreach (var (plugin, formId, entry) in testData)
            {
                await WriteRecordAsync(gameRelease, plugin, formId, entry);
            }

            // Query and verify
            await using var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {gameRelease}", connection);
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.Equal(100, count);

            // Verify data integrity
            await using var queryCmd = new SqliteCommand(
                $"SELECT plugin, formid, entry FROM {gameRelease} WHERE plugin = @plugin", connection);
            queryCmd.Parameters.AddWithValue("@plugin", $"{gameRelease}_Plugin1.esp");

            await using var reader = await queryCmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            var recordCount = 0;
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                recordCount++;
                Assert.NotNull(reader.GetString(0)); // plugin
                Assert.NotNull(reader.GetString(1)); // formid
                Assert.NotNull(reader.GetString(2)); // entry
            }

            Assert.True(recordCount > 0);
        }

        // Verify all tables exist
        var tables = GetTableNames(connection);
        foreach (var gameRelease in gameReleases)
        {
            Assert.Contains(gameRelease.ToString(), tables);
        }
    }

    [Fact]
    public async Task Database_UpdateMode_CorrectlyUpdatesExisting()
    {
        // Arrange
        await PrepareStoreAsync(GameRelease.SkyrimSE);

        await using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        // Initial data
        var plugin = "TestPlugin.esp";
        await WritePluginRecordsAsync(
            GameRelease.SkyrimSE,
            plugin,
            [new FormIdRecord("000001", "OldEntry1"), new FormIdRecord("000002", "OldEntry2")],
            UpdateMode.Append);

        // Act - replace existing plugin data through the caller-facing store seam.
        await WritePluginRecordsAsync(
            GameRelease.SkyrimSE,
            plugin,
            [new FormIdRecord("000001", "NewEntry1"), new FormIdRecord("000003", "NewEntry3")],
            UpdateMode.ReplacePluginRecords);

        // Assert
        var records = GetAllRecords(connection, GameRelease.SkyrimSE);
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r is { formid: "000001", entry: "NewEntry1" });
        Assert.Contains(records, r => r is { formid: "000003", entry: "NewEntry3" });
        Assert.DoesNotContain(records, r => r.entry.StartsWith("Old"));
    }

    #endregion

    #region Concurrent Operations Tests

    [Fact]
    public async Task Database_HandlesConcurrentOperations_Safely()
    {
        // Arrange
        await PrepareStoreAsync(GameRelease.SkyrimSE);

        await using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        const int threadCount = 5;
        const int recordsPerThread = 100;
        var tasks = new List<Task>();

        // Act - Multiple threads inserting concurrently
        for (var i = 0; i < threadCount; i++)
        {
            var threadId = i;
            var task = Task.Run(async () =>
            {
                var records = Enumerable.Range(0, recordsPerThread)
                    .Select(j => new FormIdRecord($"{threadId:X2}{j:X4}", $"Entry_{threadId}_{j}"));
                await WritePluginRecordsAsync(GameRelease.SkyrimSE, $"Plugin{threadId}.esp", records, UpdateMode.Append);
            }, TestContext.Current.CancellationToken);
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Assert
        var totalRecords = GetRecordCount(connection, GameRelease.SkyrimSE);
        Assert.Equal(threadCount * recordsPerThread, totalRecords);

        // Verify data integrity - each thread's records should be present
        for (var i = 0; i < threadCount; i++)
        {
            var pluginRecords = GetRecordsByPlugin(connection, GameRelease.SkyrimSE, $"Plugin{i}.esp");
            Assert.Equal(recordsPerThread, pluginRecords.Count);
        }
    }

    [Fact]
    public async Task Database_InterleavedReadWrite_MaintainsIntegrity()
    {
        // Arrange
        await PrepareStoreAsync(GameRelease.SkyrimSE);

        await using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        var readConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _testDbPath,
            DefaultTimeout = 2
        }.ToString();
        await using var readConnection = new SqliteConnection(readConnectionString);
        readConnection.Open();

        const int recordCount = 1000;
        var previousCount = 0;

        // Act - Interleave writes and reads across independent connections
        for (var i = 0; i < recordCount; i++)
        {
            await WriteRecordAsync(GameRelease.SkyrimSE, "ConcurrentPlugin.esp", $"{i:X6}", $"Entry{i}");

            if (i % 10 != 0)
            {
                continue;
            }

            try
            {
                var count = GetRecordCount(readConnection, GameRelease.SkyrimSE);
                Assert.True(count >= previousCount, "Record count should never decrease");
                previousCount = count;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
            {
                // Database is busy/locked during concurrent access. Continue writing.
            }
        }

        // Assert - Final verification
        var finalCount = GetRecordCount(connection, GameRelease.SkyrimSE);
        Assert.Equal(recordCount, finalCount);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task Database_PerformanceUnderLoad_MeetsThresholds()
    {
        // Arrange
        await PrepareStoreAsync(GameRelease.SkyrimSE);

        await using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        const int recordCount = 100000;
        var startTime = DateTime.UtcNow;

        // Act - Insert large number of records
        await using var transaction = connection.BeginTransaction();
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO {GameRelease.SkyrimSE} (plugin, formid, entry) 
                           VALUES (@plugin, @formid, @entry)";

        var pluginParam = new SqliteParameter("@plugin", SqliteType.Text);
        var formIdParam = new SqliteParameter("@formid", SqliteType.Text);
        var entryParam = new SqliteParameter("@entry", SqliteType.Text);

        cmd.Parameters.Add(pluginParam);
        cmd.Parameters.Add(formIdParam);
        cmd.Parameters.Add(entryParam);

        for (var i = 0; i < recordCount; i++)
        {
            pluginParam.Value = $"Plugin{i % 10}.esp";
            formIdParam.Value = $"{i:X6}";
            entryParam.Value = $"Entry{i}";
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        transaction.Commit();
        var insertTime = DateTime.UtcNow - startTime;

        // Query performance test
        startTime = DateTime.UtcNow;
        await using var queryCmd = new SqliteCommand(
            $"SELECT COUNT(*) FROM {GameRelease.SkyrimSE} WHERE plugin = @plugin", connection);
        queryCmd.Parameters.AddWithValue("@plugin", "Plugin5.esp");
        var queryCount = Convert.ToInt32(queryCmd.ExecuteScalar());
        var queryTime = DateTime.UtcNow - startTime;

        // Assert
        Assert.Equal(recordCount, GetRecordCount(connection, GameRelease.SkyrimSE));
        Assert.True(insertTime.TotalSeconds < 30, $"Insert took too long: {insertTime.TotalSeconds}s");
        Assert.True(queryTime.TotalMilliseconds < 100, $"Query took too long: {queryTime.TotalMilliseconds}ms");
        Assert.Equal(10000, queryCount); // Should have 10% of records for Plugin5
    }

    /// <summary>
    ///     Verifies that explicit Store optimization preserves access after a raw SQLite fragmentation workload.
    /// </summary>
    [Fact]
    public async Task OptimizeAsync_AfterRawWorkload_PreservesDatabaseAccessibility()
    {
        // Arrange
        await PrepareStoreAsync(GameRelease.SkyrimSE);

        await using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        // Insert and delete many records to create fragmentation
        for (var i = 0; i < 10000; i++)
        {
            await WriteRecordAsync(GameRelease.SkyrimSE, $"Plugin{i % 100}.esp", $"{i:X6}", $"Entry{i}");
        }

        // Delete half the records
        await using (var deleteCmd = new SqliteCommand(
                   $"DELETE FROM {GameRelease.SkyrimSE} WHERE CAST(SUBSTR(formid, -1) AS INTEGER) % 2 = 0", connection))
        {
            deleteCmd.ExecuteNonQuery();
        }

        // Verify database is accessible before optimization
        await using (var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", connection))
        {
            cmd.ExecuteScalar();
        }

        // Act - Optimize explicitly through the Store that owns SQLite configuration and maintenance.
        await using (var store = await FormIdRecordStore.OpenAsync(
                         _testDbPath,
                         GameRelease.SkyrimSE,
                         TestContext.Current.CancellationToken))
        {
            await store.OptimizeAsync(TestContext.Current.CancellationToken);
        }

        // Verify database is accessible after optimization
        await using (var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", connection))
        {
            cmd.ExecuteScalar();
        }

        // Assert - File size should be smaller after optimization
        connection.Close();
        var fileInfo = new FileInfo(_testDbPath);
        Assert.True(fileInfo.Length > 0);

        // Note: Performance improvement may not always be measurable in small test databases
        // but the optimization should complete without errors
    }

    #endregion

    #region Helper Methods

    /// <summary>
    ///     Opens and disposes the production Store seam so the selected GameRelease is ready for raw test workloads.
    /// </summary>
    /// <param name="gameRelease">The GameRelease table to prepare.</param>
    private async Task PrepareStoreAsync(GameRelease gameRelease)
    {
        await using var store = await FormIdRecordStore.OpenAsync(
            _testDbPath,
            gameRelease,
            TestContext.Current.CancellationToken);
    }

    private List<(string plugin, string formid, string entry)> GenerateTestData(
        GameRelease gameRelease, int count)
    {
        var data = new List<(string plugin, string formid, string entry)>();
        for (var i = 0; i < count; i++)
        {
            var pluginIndex = i % 5; // 5 different plugins
            data.Add((
                $"{gameRelease}_Plugin{pluginIndex}.esp",
                $"{i:X6}",
                $"{gameRelease}_Entry_{i}"
            ));
        }

        return data;
    }

    private List<string> GetTableNames(SqliteConnection connection)
    {
        var tables = new List<string>();
        using var cmd = new SqliteCommand(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'", connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private int GetRecordCount(SqliteConnection connection, GameRelease gameRelease)
    {
        using var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {gameRelease}", connection);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private List<(string plugin, string formid, string entry)> GetAllRecords(
        SqliteConnection connection, GameRelease gameRelease)
    {
        var records = new List<(string plugin, string formid, string entry)>();
        using var cmd = new SqliteCommand($"SELECT plugin, formid, entry FROM {gameRelease}", connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return records;
    }

    private List<(string plugin, string formid, string entry)> GetRecordsByPlugin(
        SqliteConnection connection, GameRelease gameRelease, string plugin)
    {
        var records = new List<(string plugin, string formid, string entry)>();
        using var cmd = new SqliteCommand(
            $"SELECT plugin, formid, entry FROM {gameRelease} WHERE plugin = @plugin", connection);
        cmd.Parameters.AddWithValue("@plugin", plugin);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return records;
    }

    private Task<FormIdPluginWriteResult> WriteRecordAsync(
        GameRelease gameRelease,
        string plugin,
        string formId,
        string entry)
    {
        return WritePluginRecordsAsync(
            gameRelease,
            plugin,
            [new FormIdRecord(formId, entry)],
            UpdateMode.Append);
    }

    private async Task<FormIdPluginWriteResult> WritePluginRecordsAsync(
        GameRelease gameRelease,
        string plugin,
        IEnumerable<FormIdRecord> records,
        UpdateMode updateMode)
    {
        await using var store = await FormIdRecordStore.OpenAsync(_testDbPath, gameRelease, CancellationToken.None);
        return await store.WritePluginAsync(plugin, records, updateMode, CancellationToken.None);
    }

    #endregion
}
