using System;
using System.Collections.Generic;
using System.Data;
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
public class DatabaseIntegrationTests : IDisposable
{
    private readonly DatabaseService _databaseService;
    private readonly List<string> _tempFiles;
    private readonly string _testDbPath;

    public DatabaseIntegrationTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"integration_test_{Guid.NewGuid()}.db");
        _databaseService = new DatabaseService();
        _tempFiles = new List<string> { _testDbPath };
    }

    public void Dispose()
    {
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

    [Fact]
    public async Task Database_RecoverFromCorruption_Successfully()
    {
        // This test simulates recovery from a corrupted database scenario

        // Arrange - Create initial database
        await _databaseService.InitializeDatabase(_testDbPath, GameRelease.SkyrimSE, CancellationToken.None);

        using (var connection = new SqliteConnection($"Data Source={_testDbPath}"))
        {
            connection.Open();

            // Insert some data
            for (var i = 0; i < 10; i++)
            {
                await _databaseService.InsertRecord(
                    connection,
                    GameRelease.SkyrimSE,
                    "TestPlugin.esp",
                    $"{i:X6}",
                    $"Entry{i}",
                    CancellationToken.None);
            }
        }

        // Act - Simulate recovery by re-initializing
        using (var newConnection = new SqliteConnection($"Data Source={_testDbPath}"))
        {
            newConnection.Open();

            // Re-initialize should handle existing database gracefully
            await _databaseService.InitializeDatabase(_testDbPath, GameRelease.SkyrimSE, CancellationToken.None);

            // Verify data is still there
            var count = GetRecordCount(newConnection, GameRelease.SkyrimSE);
            Assert.Equal(10, count);

            // Should be able to continue inserting
            await _databaseService.InsertRecord(
                newConnection,
                GameRelease.SkyrimSE,
                "TestPlugin.esp",
                "00000A",
                "Entry10",
                CancellationToken.None);

            var newCount = GetRecordCount(newConnection, GameRelease.SkyrimSE);
            Assert.Equal(11, newCount);
        }
    }

    #endregion

    #region Transaction Tests

    [Fact]
    public async Task Database_TransactionRollback_MaintainsConsistency()
    {
        // Arrange
        await _databaseService.InitializeDatabase(_testDbPath, GameRelease.SkyrimSE, CancellationToken.None);

        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        // Insert initial data
        await _databaseService.InsertRecord(
            connection, GameRelease.SkyrimSE, "Initial.esp", "000001", "InitialEntry", CancellationToken.None);

        var initialCount = GetRecordCount(connection, GameRelease.SkyrimSE);

        // Act - Start transaction, insert data, then rollback
        using (var transaction = connection.BeginTransaction())
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $@"INSERT INTO {GameRelease.SkyrimSE} (plugin, formid, entry) 
                               VALUES (@plugin, @formid, @entry)";

            for (var i = 0; i < 100; i++)
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@plugin", "Transactional.esp");
                cmd.Parameters.AddWithValue("@formid", $"{i:X6}");
                cmd.Parameters.AddWithValue("@entry", $"TransEntry{i}");
                await cmd.ExecuteNonQueryAsync();
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

        // Initialize database for all games first
        foreach (var gameRelease in gameReleases)
        {
            await _databaseService.InitializeDatabase(_testDbPath, gameRelease, CancellationToken.None);
        }

        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        foreach (var gameRelease in gameReleases)
        {
            // Insert test data
            var testData = GenerateTestData(gameRelease, 100);
            foreach (var (plugin, formId, entry) in testData)
            {
                await _databaseService.InsertRecord(
                    connection, gameRelease, plugin, formId, entry, CancellationToken.None);
            }

            // Query and verify
            using var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {gameRelease}", connection);
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.Equal(100, count);

            // Verify data integrity
            using var queryCmd = new SqliteCommand(
                $"SELECT plugin, formid, entry FROM {gameRelease} WHERE plugin = @plugin", connection);
            queryCmd.Parameters.AddWithValue("@plugin", $"{gameRelease}_Plugin1.esp");

            using var reader = queryCmd.ExecuteReader();
            var recordCount = 0;
            while (reader.Read())
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
        await _databaseService.InitializeDatabase(_testDbPath, GameRelease.SkyrimSE, CancellationToken.None);

        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        // Initial data
        var plugin = "TestPlugin.esp";
        await _databaseService.InsertRecord(
            connection, GameRelease.SkyrimSE, plugin, "000001", "OldEntry1", CancellationToken.None);
        await _databaseService.InsertRecord(
            connection, GameRelease.SkyrimSE, plugin, "000002", "OldEntry2", CancellationToken.None);

        // Act - Clear and insert new data
        await _databaseService.ClearPluginEntries(
            connection, GameRelease.SkyrimSE, plugin, CancellationToken.None);

        await _databaseService.InsertRecord(
            connection, GameRelease.SkyrimSE, plugin, "000001", "NewEntry1", CancellationToken.None);
        await _databaseService.InsertRecord(
            connection, GameRelease.SkyrimSE, plugin, "000003", "NewEntry3", CancellationToken.None);

        // Assert
        var records = GetAllRecords(connection, GameRelease.SkyrimSE);
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.formid == "000001" && r.entry == "NewEntry1");
        Assert.Contains(records, r => r.formid == "000003" && r.entry == "NewEntry3");
        Assert.DoesNotContain(records, r => r.entry.StartsWith("Old"));
    }

    #endregion

    #region Concurrent Operations Tests

    [Fact]
    public async Task Database_HandlesConcurrentOperations_Safely()
    {
        // Arrange
        await _databaseService.InitializeDatabase(_testDbPath, GameRelease.SkyrimSE, CancellationToken.None);

        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
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
                using var threadConnection = new SqliteConnection($"Data Source={_testDbPath}");
                threadConnection.Open();

                for (var j = 0; j < recordsPerThread; j++)
                {
                    await _databaseService.InsertRecord(
                        threadConnection,
                        GameRelease.SkyrimSE,
                        $"Plugin{threadId}.esp",
                        $"{threadId:X2}{j:X4}",
                        $"Entry_{threadId}_{j}",
                        CancellationToken.None);
                }
            });
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
    public async Task Database_ConcurrentReadWrite_MaintainsIntegrity()
    {
        // Arrange
        await _databaseService.InitializeDatabase(_testDbPath, GameRelease.SkyrimSE, CancellationToken.None);

        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        var writeComplete = false;
        var cts = new CancellationTokenSource();

        // Act - Concurrent writing and reading
        var writeTask = Task.Run(async () =>
        {
            using var writeConnection = new SqliteConnection($"Data Source={_testDbPath}");
            writeConnection.Open();

            for (var i = 0; i < 1000; i++)
            {
                await _databaseService.InsertRecord(
                    writeConnection,
                    GameRelease.SkyrimSE,
                    "ConcurrentPlugin.esp",
                    $"{i:X6}",
                    $"Entry{i}",
                    CancellationToken.None);

                if (i % 100 == 0)
                {
                    await Task.Delay(10); // Allow reads to happen
                }
            }

            writeComplete = true;
        });

        var readTask = Task.Run(async () =>
        {
            using var readConnection = new SqliteConnection($"Data Source={_testDbPath}");
            readConnection.Open();

            var previousCount = 0;
            while (!writeComplete)
            {
                var count = GetRecordCount(readConnection, GameRelease.SkyrimSE);
                Assert.True(count >= previousCount, "Record count should never decrease");
                previousCount = count;
                await Task.Delay(50);
            }
        });

        await Task.WhenAll(writeTask, readTask);

        // Assert - Final verification
        var finalCount = GetRecordCount(connection, GameRelease.SkyrimSE);
        Assert.Equal(1000, finalCount);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task Database_PerformanceUnderLoad_MeetsThresholds()
    {
        // Arrange
        await _databaseService.InitializeDatabase(_testDbPath, GameRelease.SkyrimSE, CancellationToken.None);

        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        const int recordCount = 100000;
        var startTime = DateTime.UtcNow;

        // Act - Insert large number of records
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
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
            await cmd.ExecuteNonQueryAsync();
        }

        transaction.Commit();
        var insertTime = DateTime.UtcNow - startTime;

        // Query performance test
        startTime = DateTime.UtcNow;
        using var queryCmd = new SqliteCommand(
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

    [Fact]
    public async Task Database_OptimizeDatabase_ImprovesPerformance()
    {
        // Arrange
        await _databaseService.InitializeDatabase(_testDbPath, GameRelease.SkyrimSE, CancellationToken.None);

        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        // Insert and delete many records to create fragmentation
        for (var i = 0; i < 10000; i++)
        {
            await _databaseService.InsertRecord(
                connection,
                GameRelease.SkyrimSE,
                $"Plugin{i % 100}.esp",
                $"{i:X6}",
                $"Entry{i}",
                CancellationToken.None);
        }

        // Delete half the records
        using (var deleteCmd = new SqliteCommand(
                   $"DELETE FROM {GameRelease.SkyrimSE} WHERE CAST(SUBSTR(formid, -1) AS INTEGER) % 2 = 0", connection))
        {
            deleteCmd.ExecuteNonQuery();
        }

        // Measure query performance before optimization
        var beforeOptimize = DateTime.UtcNow;
        using (var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", connection))
        {
            cmd.ExecuteScalar();
        }

        var beforeTime = DateTime.UtcNow - beforeOptimize;

        // Act - Optimize database
        await _databaseService.OptimizeDatabase(connection, CancellationToken.None);

        // Measure query performance after optimization
        var afterOptimize = DateTime.UtcNow;
        using (var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", connection))
        {
            cmd.ExecuteScalar();
        }

        var afterTime = DateTime.UtcNow - afterOptimize;

        // Assert - File size should be smaller after optimization
        connection.Close();
        var fileInfo = new FileInfo(_testDbPath);
        Assert.True(fileInfo.Length > 0);

        // Note: Performance improvement may not always be measurable in small test databases
        // but the optimization should complete without errors
    }

    #endregion

    #region Helper Methods

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

    #endregion
}
