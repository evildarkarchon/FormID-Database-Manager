using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class FormIdRecordStoreTests : IDisposable
{
    private readonly string _testDbPath = Path.Combine(Path.GetTempPath(), $"record_store_{Guid.NewGuid():N}.db");
    private readonly DatabaseService _databaseService = new();

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
    public async Task CommitStagedTextRecordsAsync_AppendMode_PreservesInterleavedPluginRows()
    {
        await using var store = await OpenStoreAsync();

        await store.StageTextRecordAsync("Plugin1.esp", "000001", "Entry1", TestContext.Current.CancellationToken);
        await store.StageTextRecordAsync("Plugin2.esp", "000002", "Entry2", TestContext.Current.CancellationToken);
        await store.StageTextRecordAsync("Plugin1.esp", "000003", "Entry3", TestContext.Current.CancellationToken);

        await store.CommitStagedTextRecordsAsync(
            replaceExistingPluginRows: false,
            TestContext.Current.CancellationToken);

        var records = await GetAllRecordsAsync();

        Assert.Equal(3, records.Count);
        Assert.Contains(records, record => record is { plugin: "Plugin1.esp", formid: "000001", entry: "Entry1" });
        Assert.Contains(records, record => record is { plugin: "Plugin2.esp", formid: "000002", entry: "Entry2" });
        Assert.Contains(records, record => record is { plugin: "Plugin1.esp", formid: "000003", entry: "Entry3" });
    }

    [Fact]
    public async Task CommitStagedTextRecordsAsync_UpdateMode_ClearsEachUniquePluginOnceCaseInsensitively()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldEntry");
        await using var store = await OpenStoreAsync();

        await store.StageTextRecordAsync("Plugin1.esp", "000010", "NewEntry1", TestContext.Current.CancellationToken);
        await store.StageTextRecordAsync("PLUGIN1.ESP", "000011", "NewEntry2", TestContext.Current.CancellationToken);

        await store.CommitStagedTextRecordsAsync(
            replaceExistingPluginRows: true,
            TestContext.Current.CancellationToken);

        var records = await GetAllRecordsAsync();

        Assert.Equal(2, records.Count);
        Assert.DoesNotContain(records, record => record.entry == "OldEntry");
        Assert.Contains(records, record => record is { plugin: "Plugin1.esp", formid: "000010", entry: "NewEntry1" });
        Assert.Contains(records, record => record is { plugin: "PLUGIN1.ESP", formid: "000011", entry: "NewEntry2" });
    }

    [Fact]
    public async Task CommitStagedTextRecordsAsync_PluginWriteFails_ContinuesWithNextPlugin()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldPlugin1");
        await InsertTestRecordAsync("Plugin2.esp", "000002", "OldPlugin2");
        await CreateFailingInsertTriggerAsync("BadEntry");
        await using var store = await OpenStoreAsync();

        await store.StageTextRecordAsync("Plugin1.esp", "000010", "GoodEntry", TestContext.Current.CancellationToken);
        await store.StageTextRecordAsync("Plugin1.esp", "000011", "BadEntry", TestContext.Current.CancellationToken);
        await store.StageTextRecordAsync("Plugin2.esp", "000020", "NewPlugin2", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<SqliteException>(() => store.CommitStagedTextRecordsAsync(
            replaceExistingPluginRows: true,
            TestContext.Current.CancellationToken));

        var records = await GetAllRecordsAsync();

        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => record is { plugin: "Plugin1.esp", formid: "000001", entry: "OldPlugin1" });
        Assert.DoesNotContain(records, record => record.entry == "GoodEntry");
        Assert.DoesNotContain(records, record => record.entry == "BadEntry");
        Assert.DoesNotContain(records, record => record.entry == "OldPlugin2");
        Assert.Contains(records, record => record is { plugin: "Plugin2.esp", formid: "000020", entry: "NewPlugin2" });
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

    [Fact]
    public async Task OpenAsync_UnsupportedGameRelease_UsesSafeTableNameWhitelist()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => FormIdRecordStore.OpenAsync(
            _testDbPath,
            (GameRelease)999,
            TestContext.Current.CancellationToken));
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

    [Fact]
    public async Task ImportFormIdTextFileAsync_UpdateMode_ReplacesPluginsAndReportsResult()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldEntry");
        var testFile = Path.Combine(Path.GetTempPath(), $"record_store_import_{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllLinesAsync(testFile,
                ["Plugin1.esp|000010|NewEntry1", "PLUGIN1.ESP|000011|NewEntry2"],
                TestContext.Current.CancellationToken);
            await using var store = await OpenStoreAsync();
            var progressReports = new List<FormIdStoreProgress>();
            var progress = new SynchronousProgress<FormIdStoreProgress>(progressReports.Add);

            var result = await store.ImportFormIdTextFileAsync(
                testFile,
                UpdateMode.ReplacePluginRecords,
                progress,
                TestContext.Current.CancellationToken);

            var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

            Assert.Equal(new FormIdTextFileImportResult(1, 2), result);
            Assert.Equal(2, records.Count);
            Assert.DoesNotContain(records, record => record.Entry == "OldEntry");
            Assert.Contains(records, record => record is { Plugin: "Plugin1.esp", FormId: "000010", Entry: "NewEntry1" });
            Assert.Contains(records, record => record is { Plugin: "PLUGIN1.ESP", FormId: "000011", Entry: "NewEntry2" });
            Assert.Contains(progressReports, report => report.Message.Contains("Completed processing 1 plugins", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(testFile))
            {
                File.Delete(testFile);
            }
        }
    }

    private Task<FormIdRecordStore> OpenStoreAsync()
    {
        return FormIdRecordStore.OpenAsync(
            _databaseService,
            _testDbPath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);
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
        await _databaseService.InitializeDatabase(
            _testDbPath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection(DatabaseService.GetOptimizedConnectionString(_testDbPath));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
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

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            handler(value);
        }
    }
}
