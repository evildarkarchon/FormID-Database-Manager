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

namespace FormID_Database_Manager.Tests.Unit.Services;

[Collection("Database Tests")]
public sealed class FormIdRecordStoreTests : IDisposable
{
    private readonly string _testDbPath = Path.Combine(Path.GetTempPath(), $"record_store_{Guid.NewGuid():N}.db");
    private readonly string _testFilesDirectory;

    /// <summary>
    ///     Creates an isolated database path and scratch directory for each test.
    /// </summary>
    public FormIdRecordStoreTests()
    {
        _testFilesDirectory = Path.Combine(Path.GetTempPath(), $"record_store_files_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testFilesDirectory);
    }

    /// <summary>
    ///     Removes the isolated database and scratch directory created by the test.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
            }
            catch
            {
                /* Ignore cleanup failures from SQLite file handles. */
            }
        }

        if (Directory.Exists(_testFilesDirectory))
        {
            try
            {
                Directory.Delete(_testFilesDirectory, true);
            }
            catch
            {
                /* Ignore cleanup failures from antivirus or file handles. */
            }
        }
    }

    [Fact]
    public async Task WritePluginAsync_AppendMode_AppendsRowsForPlugin()
    {
        await using var store = await OpenStoreAsync();

        await store.WritePluginAsync(
            "Plugin.esp",
            [new FormIdRecord("000001", "Entry1"), new FormIdRecord("000002", "Entry2")],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        var records = await GetAllRecordsAsync();

        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => record is { plugin: "Plugin.esp", formid: "000001", entry: "Entry1" });
        Assert.Contains(records, record => record is { plugin: "Plugin.esp", formid: "000002", entry: "Entry2" });
    }

    [Fact]
    public async Task WritePluginAsync_ReplaceMode_ReplacesOnePluginCaseInsensitively()
    {
        await InsertTestRecordAsync("Plugin.esp", "000001", "OldEntry");
        await InsertTestRecordAsync("Other.esp", "000002", "OtherEntry");
        await using var store = await OpenStoreAsync();

        await store.WritePluginAsync(
            "PLUGIN.ESP",
            [new FormIdRecord("000010", "NewEntry")],
            UpdateMode.ReplacePluginRecords,
            TestContext.Current.CancellationToken);

        var records = await GetAllRecordsAsync();

        Assert.Equal(2, records.Count);
        Assert.DoesNotContain(records, record => record.entry == "OldEntry");
        Assert.Contains(records, record => record is { plugin: "PLUGIN.ESP", formid: "000010", entry: "NewEntry" });
        Assert.Contains(records, record => record is { plugin: "Other.esp", formid: "000002", entry: "OtherEntry" });
    }

    [Fact]
    public async Task WritePluginAsync_ReplaceModeWithZeroRecords_PreservesExistingRows()
    {
        await InsertTestRecordAsync("Plugin.esp", "000001", "OldEntry");
        await using var store = await OpenStoreAsync();

        var result = await store.WritePluginAsync(
            "PLUGIN.ESP",
            [],
            UpdateMode.ReplacePluginRecords,
            TestContext.Current.CancellationToken);

        var records = await GetAllRecordsAsync();

        Assert.Equal(0, result.RecordCount);
        Assert.Single(records);
        Assert.Contains(records, record => record is { plugin: "Plugin.esp", formid: "000001", entry: "OldEntry" });
    }

    [Fact]
    public async Task WritePluginAsync_ReplaceInsertFails_RollsBackOldRows()
    {
        await InsertTestRecordAsync("Plugin.esp", "000001", "OldEntry");
        await CreateFailingInsertTriggerAsync("BadEntry");
        await using var store = await OpenStoreAsync();

        await Assert.ThrowsAsync<SqliteException>(() => store.WritePluginAsync(
            "Plugin.esp",
            [new FormIdRecord("000010", "GoodEntry"), new FormIdRecord("000011", "BadEntry")],
            UpdateMode.ReplacePluginRecords,
            TestContext.Current.CancellationToken));

        var records = await GetAllRecordsAsync();

        Assert.Single(records);
        Assert.Contains(records, record => record is { plugin: "Plugin.esp", formid: "000001", entry: "OldEntry" });
        Assert.DoesNotContain(records, record => record.entry == "GoodEntry");
    }

    [Fact]
    public async Task WritePluginAsync_SpecialCharactersInEntry_PreservesValue()
    {
        var specialEntry = "Entry 'with' quotes, semicolon; brackets [x], and slash / value";
        await using var store = await OpenStoreAsync();

        await store.WritePluginAsync(
            "Plugin.esp",
            [new FormIdRecord("000001", specialEntry)],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        var records = await GetAllRecordsAsync();

        Assert.Single(records);
        Assert.Equal(specialEntry, records[0].entry);
    }

    /// <summary>
    ///     Verifies optimization through the query-planner statistics persisted by SQLite for the Store table.
    /// </summary>
    [Fact]
    public async Task OptimizeAsync_AfterWrite_PersistsStatistics()
    {
        await using var store = await OpenStoreAsync();
        await store.WritePluginAsync(
            "Plugin.esp",
            [new FormIdRecord("000001", "Entry1"), new FormIdRecord("000002", "Entry2")],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        await store.OptimizeAsync(TestContext.Current.CancellationToken);

        await using var connection = await OpenUnpooledDatabaseConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_stat1 WHERE tbl = @tableName";
        command.Parameters.AddWithValue("@tableName", GameRelease.SkyrimSE.ToString());
        var statisticsCount = Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        Assert.True(statisticsCount > 0);
    }

    /// <summary>
    ///     Verifies disposal remains cleanup-only and never implicitly optimizes an incomplete Store session.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WithoutExplicitOptimization_DoesNotPersistStatistics()
    {
        var store = await OpenStoreAsync();
        await store.WritePluginAsync(
            "Plugin.esp",
            [new FormIdRecord("000001", "Entry1")],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        await store.DisposeAsync();

        await using var connection = await OpenUnpooledDatabaseConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table' AND name = 'sqlite_stat1'
            """;
        var statisticsTableCount = Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, statisticsTableCount);
    }

    /// <summary>
    ///     Verifies an unsupported GameRelease is rejected by the Supported GameRelease table before a connection is
    ///     opened, so no database file is created for a release that has no whitelisted table name.
    /// </summary>
    [Fact]
    public async Task OpenAsync_UnsupportedGameRelease_ThrowsBeforeOpeningConnection()
    {
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => FormIdRecordStore.OpenAsync(
            _testDbPath,
            (GameRelease)999,
            TestContext.Current.CancellationToken));

        Assert.Equal("release", exception.ParamName);
        Assert.Contains("999", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(_testDbPath));
    }

    /// <summary>
    ///     Verifies that invalid database paths fail before the Store attempts to open SQLite.
    /// </summary>
    [Fact]
    public async Task OpenAsync_WhitespaceDatabasePath_ThrowsBeforeOpeningConnection()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => FormIdRecordStore.OpenAsync(
            "   ",
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken));

        Assert.Equal("databasePath", exception.ParamName);
    }

    /// <summary>
    ///     Verifies that public Store opening prepares the selected table for every supported GameRelease.
    /// </summary>
    [Theory]
    [InlineData(GameRelease.SkyrimSE)]
    [InlineData(GameRelease.SkyrimSEGog)]
    [InlineData(GameRelease.SkyrimVR)]
    [InlineData(GameRelease.SkyrimLE)]
    [InlineData(GameRelease.Fallout4)]
    [InlineData(GameRelease.Fallout4VR)]
    [InlineData(GameRelease.Starfield)]
    [InlineData(GameRelease.Oblivion)]
    [InlineData(GameRelease.EnderalLE)]
    [InlineData(GameRelease.EnderalSE)]
    public async Task OpenAsync_SupportedGameRelease_PreparesSelectedTable(GameRelease gameRelease)
    {
        var store = await FormIdRecordStore.OpenAsync(
            _testDbPath,
            gameRelease,
            TestContext.Current.CancellationToken);
        await store.DisposeAsync();

        await using var connection = await OpenUnpooledDatabaseConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @table_name";
        command.Parameters.AddWithValue("@table_name", gameRelease.ToString());

        var tableName = Convert.ToString(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        Assert.Equal(gameRelease.ToString(), tableName);
    }

    /// <summary>
    ///     Verifies that opening returns with the selected schema, indexes, and text staging ready for immediate use.
    /// </summary>
    [Fact]
    public async Task OpenAsync_ExistingSchema_ReturnsReadyStoreWithConvergedIndexes()
    {
        var tableName = GameRelease.SkyrimSE.ToString();
        await using (var connection = await OpenUnpooledDatabaseConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                CREATE TABLE {tableName} (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    plugin TEXT,
                    formid TEXT,
                    entry TEXT
                );
                CREATE INDEX {tableName}_index ON {tableName}(formid);";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using (var store = await FormIdRecordStore.OpenAsync(
                         _testDbPath,
                         GameRelease.SkyrimSE,
                         TestContext.Current.CancellationToken))
        {
            // Writing through the text-row role straight after opening proves text staging is ready, with no setup.
            IFormIdTextRowWriter writer = store;
            await writer.WriteTextRowsAsync(
                StreamRowsAsync([new FormIdTextRow("Plugin.esp", "000001", "Entry1")]),
                UpdateMode.Append,
                TestContext.Current.CancellationToken);

            Assert.Equal(
                new FormIdStoredRecord("Plugin.esp", "000001", "Entry1"),
                Assert.Single(
                    await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken)));
        }

        await using var verificationConnection = await OpenUnpooledDatabaseConnectionAsync();
        await using var verificationCommand = verificationConnection.CreateCommand();
        verificationCommand.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = @table_name ORDER BY name";
        verificationCommand.Parameters.AddWithValue("@table_name", tableName);

        var indexes = new List<string>();
        await using (var reader = await verificationCommand.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                indexes.Add(reader.GetString(0));
            }
        }

        Assert.Equal([$"{tableName}_covering_idx", $"{tableName}_plugin_idx"], indexes);

        verificationCommand.CommandText = "PRAGMA journal_mode";
        verificationCommand.Parameters.Clear();
        var journalMode = Convert.ToString(
            await verificationCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        Assert.Equal("wal", journalMode, ignoreCase: true);
    }

    /// <summary>
    ///     Verifies that opening a database whose parent directory is missing creates that directory and returns a
    ///     ready Store, so a deleted or never-created database folder does not stop a Processing Run (ADR-0001).
    /// </summary>
    [Fact]
    public async Task OpenAsync_MissingParentDirectory_CreatesDirectoryAndReturnsReadyStore()
    {
        var missingDirectory = Path.Combine(_testFilesDirectory, "missing", "nested");
        var databasePath = Path.Combine(missingDirectory, "records.db");
        Assert.False(Directory.Exists(missingDirectory));

        await using (var store = await FormIdRecordStore.OpenAsync(
                         databasePath,
                         GameRelease.SkyrimSE,
                         TestContext.Current.CancellationToken))
        {
            Assert.True(Directory.Exists(missingDirectory));

            await store.WritePluginAsync(
                "Plugin.esp",
                [new FormIdRecord("000001", "Entry1")],
                UpdateMode.Append,
                TestContext.Current.CancellationToken);
            var records = await store.ReadRecordsAsync(
                FormIdRecordQuery.All,
                TestContext.Current.CancellationToken);

            var record = Assert.Single(records);
            Assert.Equal(new FormIdStoredRecord("Plugin.esp", "000001", "Entry1"), record);
        }

        Assert.True(File.Exists(databasePath));
    }

    /// <summary>
    ///     Verifies that reopening the same GameRelease keeps records written by an earlier Store session.
    /// </summary>
    [Fact]
    public async Task OpenAsync_ReopenedGameRelease_PreservesExistingRecords()
    {
        await using (var store = await OpenStoreAsync())
        {
            await store.WritePluginAsync(
                "Plugin.esp",
                [new FormIdRecord("000001", "Entry1")],
                UpdateMode.Append,
                TestContext.Current.CancellationToken);
        }

        await using var reopenedStore = await OpenStoreAsync();
        var records = await reopenedStore.ReadRecordsAsync(
            FormIdRecordQuery.All,
            TestContext.Current.CancellationToken);

        var record = Assert.Single(records);
        Assert.Equal(new FormIdStoredRecord("Plugin.esp", "000001", "Entry1"), record);
    }

    /// <summary>
    ///     Verifies that sequential Store openings prepare independent GameRelease tables in one database file.
    /// </summary>
    [Fact]
    public async Task OpenAsync_DifferentGameReleases_PreparesIndependentTables()
    {
        var skyrimStore = await FormIdRecordStore.OpenAsync(
            _testDbPath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);
        await skyrimStore.DisposeAsync();

        var falloutStore = await FormIdRecordStore.OpenAsync(
            _testDbPath,
            GameRelease.Fallout4,
            TestContext.Current.CancellationToken);
        await falloutStore.DisposeAsync();

        await using var connection = await OpenUnpooledDatabaseConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT name
            FROM sqlite_master
            WHERE type = 'table' AND name IN (@skyrim_table, @fallout_table)
            ORDER BY name";
        command.Parameters.AddWithValue("@skyrim_table", GameRelease.SkyrimSE.ToString());
        command.Parameters.AddWithValue("@fallout_table", GameRelease.Fallout4.ToString());

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal([GameRelease.Fallout4.ToString(), GameRelease.SkyrimSE.ToString()], tables);
    }

    /// <summary>
    ///     Verifies that cancellation before schema preparation leaves no selected GameRelease schema behind.
    /// </summary>
    [Fact]
    public async Task OpenAsync_PreCancelled_LeavesNoSelectedGameReleaseSchema()
    {
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FormIdRecordStore.OpenAsync(
            _testDbPath,
            GameRelease.SkyrimSE,
            cancellationSource.Token));

        await using var connection = await OpenUnpooledDatabaseConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table_name";
        command.Parameters.AddWithValue("@table_name", GameRelease.SkyrimSE.ToString());

        var selectedTableCount = Convert.ToInt64(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, selectedTableCount);
    }

    /// <summary>
    ///     Verifies that a schema failure after table creation rolls back the selected GameRelease schema as one unit.
    /// </summary>
    [Fact]
    public async Task OpenAsync_SchemaPreparationFails_RollsBackSelectedGameReleaseTable()
    {
        var coveringIndexName = $"{GameRelease.SkyrimSE}_covering_idx";
        await using (var connection = await OpenUnpooledDatabaseConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE TABLE {coveringIndexName} (id INTEGER PRIMARY KEY)";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(() => FormIdRecordStore.OpenAsync(
            _testDbPath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken));

        Assert.Equal(1, exception.SqliteErrorCode);
        Assert.Contains(coveringIndexName, exception.Message, StringComparison.Ordinal);

        await using (var verificationConnection = await OpenUnpooledDatabaseConnectionAsync())
        {
            await using var verificationCommand = verificationConnection.CreateCommand();
            verificationCommand.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table_name";
            verificationCommand.Parameters.AddWithValue("@table_name", GameRelease.SkyrimSE.ToString());

            var selectedTableCount = Convert.ToInt64(
                await verificationCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken));

            Assert.Equal(0, selectedTableCount);

            verificationCommand.CommandText = $"DROP TABLE {coveringIndexName}";
            verificationCommand.Parameters.Clear();
            await verificationCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var reopenedStore = await OpenStoreAsync();
        var writeResult = await reopenedStore.WritePluginAsync(
            "Plugin.esp",
            [new FormIdRecord("000001", "Entry1")],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, writeResult.RecordCount);
    }

    [Fact]
    public async Task ReadRecordsAsync_PluginQuery_MatchesPluginCaseInsensitively()
    {
        await using var store = await OpenStoreAsync();
        await store.WritePluginAsync(
            "Plugin.esp",
            [new FormIdRecord("000001", "Entry1"), new FormIdRecord("000002", "Entry2")],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);
        await store.WritePluginAsync(
            "Other.esp",
            [new FormIdRecord("000003", "OtherEntry")],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(
            new FormIdRecordQuery { PluginName = "PLUGIN.ESP" },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal("Plugin.esp", record.Plugin));
    }

    [Fact]
    public async Task ReadPluginsWithEntriesAsync_ReturnsCaseInsensitiveDistinctPlugins()
    {
        await using var store = await OpenStoreAsync();
        await store.WritePluginAsync(
            "Plugin.esp",
            [new FormIdRecord("000001", "Entry1")],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);
        await store.WritePluginAsync(
            "PLUGIN.ESP",
            [new FormIdRecord("000002", "Entry2")],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);
        await store.WritePluginAsync(
            "Other.esp",
            [new FormIdRecord("000003", "OtherEntry")],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        var plugins = await store.ReadPluginsWithEntriesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, plugins.Count);
        Assert.Contains("plugin.esp", plugins);
        Assert.Contains("Other.esp", plugins);
    }

    /// <summary>
    ///     Verifies that the text-row role in append mode keeps a Plugin's existing rows beside the streamed ones.
    /// </summary>
    [Fact]
    public async Task WriteTextRowsAsync_AppendMode_PreservesExistingRows()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldEntry");
        await using var store = await OpenStoreAsync();
        IFormIdTextRowWriter writer = store;

        await writer.WriteTextRowsAsync(
            StreamRowsAsync([new FormIdTextRow("Plugin1.esp", "000002", "NewEntry")]),
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                new FormIdStoredRecord("Plugin1.esp", "000001", "OldEntry"),
                new FormIdStoredRecord("Plugin1.esp", "000002", "NewEntry")
            ],
            records);
    }

    /// <summary>
    ///     Verifies that Update Mode replaces each mentioned Plugin case-insensitively, keeps each row's casing,
    ///     inserts new Plugins, and leaves unmentioned Plugins alone.
    /// </summary>
    [Fact]
    public async Task WriteTextRowsAsync_UpdateMode_ReplacesMentionedPluginsAndInsertsNewOnes()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldEntry1");
        await InsertTestRecordAsync("Plugin1.esp", "000002", "OldEntry2");
        await InsertTestRecordAsync("Untouched.esp", "000003", "UntouchedEntry");
        await using var store = await OpenStoreAsync();
        IFormIdTextRowWriter writer = store;

        await writer.WriteTextRowsAsync(
            StreamRowsAsync(
            [
                new FormIdTextRow("PLUGIN1.ESP", "000010", "NewEntry1"),
                new FormIdTextRow("plugin1.esp", "000011", "NewEntry2"),
                new FormIdTextRow("BrandNewPlugin.esp", "000020", "BrandNewEntry")
            ]),
            UpdateMode.ReplacePluginRecords,
            TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                new FormIdStoredRecord("Untouched.esp", "000003", "UntouchedEntry"),
                new FormIdStoredRecord("PLUGIN1.ESP", "000010", "NewEntry1"),
                new FormIdStoredRecord("plugin1.esp", "000011", "NewEntry2"),
                new FormIdStoredRecord("BrandNewPlugin.esp", "000020", "BrandNewEntry")
            ],
            records);
    }

    /// <summary>
    ///     Verifies that one Plugin's rows scattered through the stream are committed together, in stream order, before
    ///     the next Plugin's rows.
    /// </summary>
    [Fact]
    public async Task WriteTextRowsAsync_ScatteredRowsForOnePlugin_CommitsThePluginTogether()
    {
        await InsertTestRecordAsync("First.esp", "000001", "OldFirst");
        await using var store = await OpenStoreAsync();
        IFormIdTextRowWriter writer = store;

        await writer.WriteTextRowsAsync(
            StreamRowsAsync(
            [
                new FormIdTextRow("First.esp", "000010", "First1"),
                new FormIdTextRow("Second.esp", "000020", "Second1"),
                new FormIdTextRow("FIRST.ESP", "000011", "First2"),
                new FormIdTextRow("Second.esp", "000021", "Second2"),
                new FormIdTextRow("first.esp", "000012", "First3")
            ]),
            UpdateMode.ReplacePluginRecords,
            TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                new FormIdStoredRecord("First.esp", "000010", "First1"),
                new FormIdStoredRecord("FIRST.ESP", "000011", "First2"),
                new FormIdStoredRecord("first.esp", "000012", "First3"),
                new FormIdStoredRecord("Second.esp", "000020", "Second1"),
                new FormIdStoredRecord("Second.esp", "000021", "Second2")
            ],
            records);
    }

    /// <summary>
    ///     Verifies that a stream crossing the 10,000-row staging boundary commits every row.
    /// </summary>
    [Fact]
    public async Task WriteTextRowsAsync_ExceedsStagingBatchSize_CommitsEveryRow()
    {
        const int totalRows = 10100;
        await using var store = await OpenStoreAsync();
        IFormIdTextRowWriter writer = store;

        await writer.WriteTextRowsAsync(
            StreamRowsAsync(Enumerable.Range(0, totalRows)
                .Select(i => new FormIdTextRow("Plugin.esp", $"{i:X6}", $"Entry{i}"))),
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);
        Assert.Equal(totalRows, records.Count);
        Assert.Equal(new FormIdStoredRecord("Plugin.esp", "000000", "Entry0"), records[0]);
        Assert.Equal(new FormIdStoredRecord("Plugin.esp", "002773", "Entry10099"), records[^1]);
    }

    /// <summary>
    ///     Verifies that the indexed staging path commits a stream naming many distinct Plugins, one transaction per
    ///     Plugin, without losing any of them.
    /// </summary>
    [Fact]
    public async Task WriteTextRowsAsync_ManyDistinctPlugins_CommitsEveryPlugin()
    {
        const int pluginCount = 512;
        await using var store = await OpenStoreAsync();
        IFormIdTextRowWriter writer = store;

        await writer.WriteTextRowsAsync(
            StreamRowsAsync(Enumerable.Range(0, pluginCount)
                .Select(i => new FormIdTextRow($"Plugin{i:D4}.esp", $"{i:X6}", $"Entry{i}"))),
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);
        var plugins = await store.ReadPluginsWithEntriesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(pluginCount, records.Count);
        Assert.Equal(pluginCount, plugins.Count);
        Assert.Equal(new FormIdStoredRecord("Plugin0000.esp", "000000", "Entry0"), records[0]);
        Assert.Equal(new FormIdStoredRecord("Plugin0511.esp", "0001FF", "Entry511"), records[^1]);
    }

    /// <summary>
    ///     Verifies that a failed Plugin commit rolls back that Plugin, including its replacement delete, while the
    ///     other Plugins still commit and the single failure is rethrown as is.
    /// </summary>
    [Fact]
    public async Task WriteTextRowsAsync_PluginCommitFails_RollsBackThatPluginAndCommitsTheOthers()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldPlugin1");
        await InsertTestRecordAsync("Plugin2.esp", "000002", "OldPlugin2");
        await CreateFailingInsertTriggerAsync("BadEntry");
        await using var store = await OpenStoreAsync();
        IFormIdTextRowWriter writer = store;

        await Assert.ThrowsAsync<SqliteException>(() => writer.WriteTextRowsAsync(
            StreamRowsAsync(
            [
                new FormIdTextRow("Plugin1.esp", "000010", "GoodEntry"),
                new FormIdTextRow("Plugin1.esp", "000011", "BadEntry"),
                new FormIdTextRow("Plugin2.esp", "000020", "NewPlugin2")
            ]),
            UpdateMode.ReplacePluginRecords,
            TestContext.Current.CancellationToken));

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                new FormIdStoredRecord("Plugin1.esp", "000001", "OldPlugin1"),
                new FormIdStoredRecord("Plugin2.esp", "000020", "NewPlugin2")
            ],
            records);
    }

    /// <summary>
    ///     Verifies that cancellation after a staging flush commits nothing and leaves no staged rows for the next
    ///     call on the same Store.
    /// </summary>
    [Fact]
    public async Task WriteTextRowsAsync_CancelledAfterStagingFlush_ReusedStoreCommitsNoStaleRows()
    {
        const int rowsBeforeCancellation = 11000;
        using var cancellationSource = new CancellationTokenSource();
        await using var store = await OpenStoreAsync();
        IFormIdTextRowWriter writer = store;

        async IAsyncEnumerable<FormIdTextRow> CancelAfterStagingFlushAsync()
        {
            foreach (var row in Enumerable.Range(0, rowsBeforeCancellation)
                         .Select(i => new FormIdTextRow("Cancelled.esp", $"{i:X6}", $"CancelledEntry{i}")))
            {
                await Task.Yield();
                yield return row;
            }

            // 11,000 staged rows leave one full batch flushed to TEMP and 1,000 rows managed when cancellation is
            // requested. The Store observes it when it stages the row yielded next.
            await cancellationSource.CancelAsync();
            yield return new FormIdTextRow("Cancelled.esp", "FFFFFE", "AfterCancellation");
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteTextRowsAsync(
            CancelAfterStagingFlushAsync(),
            UpdateMode.Append,
            cancellationSource.Token));

        Assert.Empty(await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken));

        await writer.WriteTextRowsAsync(
            StreamRowsAsync([new FormIdTextRow("Fresh.esp", "FFFFFF", "FreshEntry")]),
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);
        Assert.Equal(new FormIdStoredRecord("Fresh.esp", "FFFFFF", "FreshEntry"), Assert.Single(records));
    }

    /// <summary>
    ///     Verifies that a stream failing partway through, after a staging flush, propagates its own exception unchanged,
    ///     commits nothing, and leaves no staged rows for the next call on the same Store.
    /// </summary>
    [Fact]
    public async Task WriteTextRowsAsync_StreamThrowsPartway_PropagatesItsExceptionAndReusedStoreCommitsNoStaleRows()
    {
        const int rowsBeforeFailure = 10500;
        var streamFailure = new IOException("The FormID text file became unreadable.");
        await using var store = await OpenStoreAsync();
        IFormIdTextRowWriter writer = store;

        async IAsyncEnumerable<FormIdTextRow> FailAfterStagingFlushAsync()
        {
            foreach (var row in Enumerable.Range(0, rowsBeforeFailure)
                         .Select(i => new FormIdTextRow("Failed.esp", $"{i:X6}", $"FailedEntry{i}")))
            {
                await Task.Yield();
                yield return row;
            }

            throw streamFailure;
        }

        var thrown = await Assert.ThrowsAsync<IOException>(() => writer.WriteTextRowsAsync(
            FailAfterStagingFlushAsync(),
            UpdateMode.Append,
            TestContext.Current.CancellationToken));
        Assert.Same(streamFailure, thrown);

        Assert.Empty(await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken));

        await writer.WriteTextRowsAsync(
            StreamRowsAsync([new FormIdTextRow("Fresh.esp", "FFFFFF", "FreshEntry")]),
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);
        Assert.Equal(new FormIdStoredRecord("Fresh.esp", "FFFFFF", "FreshEntry"), Assert.Single(records));
    }

    private Task<FormIdRecordStore> OpenStoreAsync()
    {
        return FormIdRecordStore.OpenAsync(
            _testDbPath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Opens an unpooled connection for independent persisted-database setup or verification in Store tests.
    /// </summary>
    /// <returns>An open connection that the caller must dispose.</returns>
    private async Task<SqliteConnection> OpenUnpooledDatabaseConnectionAsync()
    {
        var connection = new SqliteConnection($"Data Source={_testDbPath};Pooling=False");
        try
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task InsertTestRecordAsync(string plugin, string formId, string entry)
    {
        await using var store = await OpenStoreAsync();
        await store.WritePluginAsync(
            plugin,
            [new FormIdRecord(formId, entry)],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);
    }

    private async Task CreateFailingInsertTriggerAsync(string failingEntry)
    {
        var store = await FormIdRecordStore.OpenAsync(
            _testDbPath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);
        await store.DisposeAsync();

        await using var connection = await OpenUnpooledDatabaseConnectionAsync();
        await using var command = connection.CreateCommand();
        var escapedEntry = failingEntry.Replace("'", "''", StringComparison.Ordinal);
        command.CommandText = $@"
            CREATE TRIGGER fail_bad_entry
            BEFORE INSERT ON {GameRelease.SkyrimSE}
            WHEN NEW.entry = '{escapedEntry}'
            BEGIN
                SELECT RAISE(FAIL, 'bad entry');
            END;";
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<(string plugin, string formid, string entry)>> GetAllRecordsAsync()
    {
        var records = new List<(string plugin, string formid, string entry)>();
        await using var store = await OpenStoreAsync();
        var storedRecords = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        foreach (var record in storedRecords)
        {
            records.Add((record.Plugin!, record.FormId!, record.Entry!));
        }

        return records;
    }

    /// <summary>
    ///     Streams in-memory FormID text rows to the Store's text-row role, yielding before each row so the Store
    ///     consumes a genuinely asynchronous stream rather than a completed one.
    /// </summary>
    /// <param name="rows">The rows to stream, in order.</param>
    /// <returns>A lazy stream that yields each row once.</returns>
    private static async IAsyncEnumerable<FormIdTextRow> StreamRowsAsync(IEnumerable<FormIdTextRow> rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }
}
