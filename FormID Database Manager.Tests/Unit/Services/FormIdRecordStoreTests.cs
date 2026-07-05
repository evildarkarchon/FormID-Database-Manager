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
    public async Task WritePluginRecordsAsync_AppendMode_AppendsRowsForPlugin()
    {
        await using var store = await OpenStoreAsync();

        await store.WritePluginRecordsAsync(
            "Plugin.esp",
            [new FormIdRecord("000001", "Entry1"), new FormIdRecord("000002", "Entry2")],
            replaceExistingPluginRows: false,
            TestContext.Current.CancellationToken);

        var records = await GetAllRecordsAsync();

        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => record is { plugin: "Plugin.esp", formid: "000001", entry: "Entry1" });
        Assert.Contains(records, record => record is { plugin: "Plugin.esp", formid: "000002", entry: "Entry2" });
    }

    [Fact]
    public async Task WritePluginRecordsAsync_ReplaceMode_ReplacesOnePluginCaseInsensitively()
    {
        await InsertTestRecordAsync("Plugin.esp", "000001", "OldEntry");
        await InsertTestRecordAsync("Other.esp", "000002", "OtherEntry");
        await using var store = await OpenStoreAsync();

        await store.WritePluginRecordsAsync(
            "PLUGIN.ESP",
            [new FormIdRecord("000010", "NewEntry")],
            replaceExistingPluginRows: true,
            TestContext.Current.CancellationToken);

        var records = await GetAllRecordsAsync();

        Assert.Equal(2, records.Count);
        Assert.DoesNotContain(records, record => record.entry == "OldEntry");
        Assert.Contains(records, record => record is { plugin: "PLUGIN.ESP", formid: "000010", entry: "NewEntry" });
        Assert.Contains(records, record => record is { plugin: "Other.esp", formid: "000002", entry: "OtherEntry" });
    }

    [Fact]
    public async Task WritePluginRecordsAsync_ReplaceInsertFails_RollsBackOldRows()
    {
        await InsertTestRecordAsync("Plugin.esp", "000001", "OldEntry");
        await CreateFailingInsertTriggerAsync("BadEntry");
        await using var store = await OpenStoreAsync();

        await Assert.ThrowsAsync<SqliteException>(() => store.WritePluginRecordsAsync(
            "Plugin.esp",
            [new FormIdRecord("000010", "GoodEntry"), new FormIdRecord("000011", "BadEntry")],
            replaceExistingPluginRows: true,
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
    public async Task WritePluginRecordsAsync_SpecialCharactersInEntry_PreservesValue()
    {
        var specialEntry = "Entry 'with' quotes, semicolon; brackets [x], and slash / value";
        await using var store = await OpenStoreAsync();

        await store.WritePluginRecordsAsync(
            "Plugin.esp",
            [new FormIdRecord("000001", specialEntry)],
            replaceExistingPluginRows: false,
            TestContext.Current.CancellationToken);

        var records = await GetAllRecordsAsync();

        Assert.Single(records);
        Assert.Equal(specialEntry, records[0].entry);
    }

    [Fact]
    public async Task OpenAsync_UnsupportedGameRelease_UsesSafeTableNameWhitelist()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => FormIdRecordStore.OpenAsync(
            _databaseService,
            _testDbPath,
            (GameRelease)999,
            TestContext.Current.CancellationToken));
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
        await _databaseService.InitializeDatabase(
            _testDbPath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection(DatabaseService.GetOptimizedConnectionString(_testDbPath));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {GameRelease.SkyrimSE} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";
        command.Parameters.AddWithValue("@plugin", plugin);
        command.Parameters.AddWithValue("@formid", formId);
        command.Parameters.AddWithValue("@entry", entry);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
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
        await using var connection = new SqliteConnection(DatabaseService.GetOptimizedConnectionString(_testDbPath));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT plugin, formid, entry FROM {GameRelease.SkyrimSE} ORDER BY id";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return records;
    }
}
