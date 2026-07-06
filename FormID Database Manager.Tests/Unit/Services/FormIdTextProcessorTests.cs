#nullable enable

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

public sealed class FormIdTextProcessorTests : IDisposable
{
    private readonly DatabaseService _databaseService = new();
    private readonly FormIdTextProcessor _processor = new();
    private readonly string _testDbPath;
    private readonly string _testFilesDir;

    public FormIdTextProcessorTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
        _testFilesDir = Path.Combine(Path.GetTempPath(), $"test_files_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testFilesDir);
    }

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

        if (Directory.Exists(_testFilesDir))
        {
            try
            {
                Directory.Delete(_testFilesDir, true);
            }
            catch
            {
                /* Ignore cleanup failures from antivirus/file handles. */
            }
        }
    }

    #region Update Mode Tests

    [Fact]
    public async Task ProcessFormIdListFile_ClearsExistingEntries_InUpdateMode()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldEntry1");
        await InsertTestRecordAsync("Plugin1.esp", "000002", "OldEntry2");
        await InsertTestRecordAsync("Plugin2.esp", "000003", "OldEntry3");

        var testFile = Path.Combine(_testFilesDir, "update_mode.txt");
        await File.WriteAllLinesAsync(testFile,
            ["Plugin1.esp|000001|NewEntry1", "Plugin1.esp|000004|NewEntry4", "Plugin2.esp|000003|UpdatedEntry3"],
            TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: true);

        var records = GetAllRecords();
        Assert.Equal(3, records.Count);
        Assert.DoesNotContain(records, record => record.entry.StartsWith("OldEntry", StringComparison.Ordinal));
        Assert.Contains(records, record => record.entry == "NewEntry1");
        Assert.Contains(records, record => record.entry == "NewEntry4");
        Assert.Contains(records, record => record.entry == "UpdatedEntry3");
    }

    [Fact]
    public async Task ProcessFormIdListFile_UpdateMode_InsertsNewPluginWithoutExistingRows()
    {
        var testFile = Path.Combine(_testFilesDir, "update_mode_new_plugin.txt");
        await File.WriteAllLinesAsync(testFile,
            ["BrandNewPlugin.esp|000001|Entry1", "BrandNewPlugin.esp|000002|Entry2"],
            TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: true);

        var records = GetAllRecords();
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal("BrandNewPlugin.esp", record.plugin));
    }

    [Fact]
    public async Task ProcessFormIdListFile_UpdateMode_ClearsExistingEntriesCaseInsensitively()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldEntry1");

        var testFile = Path.Combine(_testFilesDir, "update_mode_case_insensitive.txt");
        await File.WriteAllLinesAsync(testFile, ["PLUGIN1.ESP|000010|NewEntry1"], TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: true);

        var records = GetAllRecords();
        Assert.Single(records);
        Assert.DoesNotContain(records, record => record.entry == "OldEntry1");
        Assert.Contains(records, record => record is { plugin: "PLUGIN1.ESP", entry: "NewEntry1" });
    }

    [Fact]
    public async Task ProcessFormIdListFile_UpdateModeOff_AppendsWithoutClearingExistingRows()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldEntry1");

        var testFile = Path.Combine(_testFilesDir, "update_mode_off_no_clear.txt");
        await File.WriteAllLinesAsync(testFile, ["Plugin1.esp|000002|NewEntry1"], TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: false);

        var records = GetAllRecords();
        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => record.entry == "OldEntry1");
        Assert.Contains(records, record => record.entry == "NewEntry1");
    }

    #endregion

    #region Transaction Tests

    [Fact]
    public async Task ProcessFormIdListFile_CommitError_RollsBackCurrentPluginRows()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldEntry1");
        await CreateFailingInsertTriggerAsync("BadEntry");

        var testFile = Path.Combine(_testFilesDir, "transaction_test.txt");
        await File.WriteAllLinesAsync(testFile,
            ["Plugin1.esp|000010|GoodEntry", "Plugin1.esp|000011|BadEntry"],
            TestContext.Current.CancellationToken);

        await using var store = await OpenStoreAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<SqliteException>(() => _processor.ProcessFormIdListFile(
            testFile,
            store,
            updateMode: true,
            TestContext.Current.CancellationToken));

        var records = GetAllRecords();
        Assert.Single(records);
        Assert.Contains(records, record => record is { plugin: "Plugin1.esp", formid: "000001", entry: "OldEntry1" });
        Assert.DoesNotContain(records, record => record.entry == "GoodEntry");
    }

    #endregion

    #region Core Functionality Tests

    [Fact]
    public async Task ProcessFormIdListFile_ParsesValidFormat_Correctly()
    {
        var testFile = Path.Combine(_testFilesDir, "valid_formids.txt");
        await File.WriteAllLinesAsync(testFile,
            ["TestPlugin.esp|000001|TestWeapon", "TestPlugin.esp|000002|TestArmor", "TestPlugin2.esp|000003|TestSpell"],
            TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: false);

        var records = GetAllRecords();
        Assert.Equal(3, records.Count);
        Assert.Contains(records, record => record is { plugin: "TestPlugin.esp", formid: "000001", entry: "TestWeapon" });
        Assert.Contains(records, record => record is { plugin: "TestPlugin.esp", formid: "000002", entry: "TestArmor" });
        Assert.Contains(records, record => record is { plugin: "TestPlugin2.esp", formid: "000003", entry: "TestSpell" });
    }

    [Fact]
    public async Task ProcessFormIdListFile_HandlesDifferentLineFormats_Correctly()
    {
        var testFile = Path.Combine(_testFilesDir, "varied_format.txt");
        await File.WriteAllLinesAsync(testFile,
            [
                "Plugin1.esp|000001|Entry1",
                "  Plugin2.esp  |  000002  |  Entry2  ",
                "Plugin3.esp|000003|Entry3|ExtraData",
                "",
                "   ",
                "InvalidLine",
                "Plugin4.esp|000004"
            ],
            TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: false);

        var records = GetAllRecords();
        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => record is { plugin: "Plugin1.esp", formid: "000001", entry: "Entry1" });
        Assert.Contains(records, record => record is { plugin: "Plugin2.esp", formid: "000002", entry: "Entry2" });
    }

    [Fact]
    public async Task ProcessFormIdListFile_HandlesMultiplePlugins_InSingleFile()
    {
        var testFile = Path.Combine(_testFilesDir, "multiple_plugins.txt");
        await File.WriteAllLinesAsync(testFile,
            [
                "Plugin1.esp|000001|Entry1",
                "Plugin1.esp|000002|Entry2",
                "Plugin2.esp|000003|Entry3",
                "Plugin2.esp|000004|Entry4",
                "Plugin1.esp|000005|Entry5",
                "Plugin3.esp|000006|Entry6"
            ],
            TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: false);

        var records = GetAllRecords();
        Assert.Equal(6, records.Count);
        Assert.Equal(3, records.Count(record => record.plugin == "Plugin1.esp"));
        Assert.Equal(2, records.Count(record => record.plugin == "Plugin2.esp"));
        Assert.Single(records, record => record.plugin == "Plugin3.esp");
    }

    #endregion

    #region Progress and UI Update Tests

    [Fact]
    public async Task ProcessFormIdListFile_ReportsProgress_AtRegularIntervals()
    {
        var testFile = Path.Combine(_testFilesDir, "progress_test.txt");
        var lines = Enumerable.Range(0, 2500).Select(i => $"Plugin.esp|{i:X6}|Entry{i}");
        await File.WriteAllLinesAsync(testFile, lines, TestContext.Current.CancellationToken);

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(progressReports.Add);

        await ProcessFileAsync(testFile, updateMode: false, progress: progress);

        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, report => report.Message.Contains("Starting processing", StringComparison.Ordinal));
        Assert.Contains(progressReports, report => report.Message.Contains("Processing:", StringComparison.Ordinal) && report.Value.HasValue);
        Assert.Contains(progressReports, report => report.Message.Contains("Completed processing", StringComparison.Ordinal));

        var progressValues = progressReports.Where(report => report.Value.HasValue).Select(report => report.Value!.Value).ToList();
        for (var i = 1; i < progressValues.Count; i++)
        {
            Assert.True(progressValues[i] >= progressValues[i - 1]);
        }
    }

    [Fact]
    public async Task ProcessFormIdListFile_CalculatesTotalLines_ForAccurateProgress()
    {
        var testFile = Path.Combine(_testFilesDir, "line_count_test.txt");
        const int expectedLines = 100;
        var lines = Enumerable.Range(1, expectedLines).Select(i => $"Plugin.esp|{i:X6}|Entry{i}");
        await File.WriteAllLinesAsync(testFile, lines, TestContext.Current.CancellationToken);

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(progressReports.Add);

        await ProcessFileAsync(testFile, updateMode: false, progress: progress);

        var completionReport = progressReports.Last(report => report.Message.Contains("Completed", StringComparison.Ordinal));
        Assert.Contains($"{expectedLines:N0} total records", completionReport.Message);
        Assert.Equal(100, completionReport.Value);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task ProcessFormIdListFile_BatchesInserts_CorrectlyAtBatchSize()
    {
        const int batchSize = 10000;
        var testFile = Path.Combine(_testFilesDir, "batch_test.txt");
        var totalRecords = batchSize + 100;
        var lines = Enumerable.Range(0, totalRecords).Select(i => $"Plugin.esp|{i:X6}|Entry{i}");
        await File.WriteAllLinesAsync(testFile, lines, TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: false);

        Assert.Equal(totalRecords, GetAllRecords().Count);
    }

    [Fact]
    public async Task ProcessFormIdListFile_HandlesLargeFiles_Correctly()
    {
        var testFile = Path.Combine(_testFilesDir, "large_file_test.txt");
        const int totalRecords = 25000;
        const int pluginCount = 2;
        const int recordsPerPlugin = totalRecords / pluginCount;

        await using (var writer = new StreamWriter(testFile))
        {
            for (var pluginIndex = 0; pluginIndex < pluginCount; pluginIndex++)
            {
                for (var i = 0; i < recordsPerPlugin; i++)
                {
                    var recordIndex = pluginIndex * recordsPerPlugin + i;
                    await writer.WriteLineAsync($"Plugin{pluginIndex}.esp|{recordIndex:X6}|Entry{recordIndex}");
                }
            }
        }

        await ProcessFileAsync(testFile, updateMode: false);

        Assert.Equal(totalRecords, GetAllRecords().Count);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ProcessFormIdListFile_HandlesEmptyFile_Gracefully()
    {
        var testFile = Path.Combine(_testFilesDir, "empty.txt");
        await File.WriteAllTextAsync(testFile, string.Empty, TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: false);

        Assert.Empty(GetAllRecords());
    }

    [Fact]
    public async Task ProcessFormIdListFile_HandlesInvalidFormat_WithoutCrashing()
    {
        var testFile = Path.Combine(_testFilesDir, "invalid_format.txt");
        await File.WriteAllLinesAsync(testFile,
            ["This is not a valid format", "Neither|is|this|one|with|too|many|pipes", "OrThis|WithTooFew", "||||"],
            TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: false);

        Assert.Empty(GetAllRecords());
    }

    [Fact]
    public async Task ProcessFormIdListFile_RespectsCancellationToken()
    {
        var testFile = Path.Combine(_testFilesDir, "cancellation_test.txt");
        var lines = Enumerable.Range(0, 100000).Select(i => $"Plugin.esp|{i:X6}|Entry{i}");
        await File.WriteAllLinesAsync(testFile, lines, TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<(string Message, double? Value)>(report =>
        {
            if (report.Message.Contains("Processing:", StringComparison.Ordinal))
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProcessFileAsync(testFile, updateMode: false, cancellationToken: cts.Token, progress: progress));

        Assert.True(GetAllRecords().Count < 100000);
    }

    [Fact]
    public async Task ProcessFormIdListFile_HandlesFileNotFound_Gracefully()
    {
        var nonExistentFile = Path.Combine(_testFilesDir, "does_not_exist.txt");

        await using var store = await OpenStoreAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _processor.ProcessFormIdListFile(
            nonExistentFile,
            store,
            updateMode: false,
            TestContext.Current.CancellationToken));
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task ProcessFormIdListFile_HandlesSpecialCharacters_InFormIDs()
    {
        var testFile = Path.Combine(_testFilesDir, "special_chars.txt");
        await File.WriteAllLinesAsync(testFile,
            [
                "Plugin.esp|FF0001|Entry with spaces",
                "Plugin.esp|000002|Entry-with-dashes",
                "Plugin.esp|000003|Entry_with_underscores",
                "Plugin.esp|000004|Entry.with.dots",
                "Plugin.esp|000005|Entry'with'quotes",
                @"Plugin.esp|000006|Entry\with\backslashes",
                "Plugin.esp|000007|Entry/with/forward/slashes",
                "Plugin.esp|000008|Entry(with)parentheses",
                "Plugin.esp|000009|Entry[with]brackets",
                "Plugin.esp|00000A|Entry{with}braces"
            ],
            TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: false);

        var records = GetAllRecords();
        Assert.Equal(10, records.Count);
        Assert.Contains(records, record => record.entry == "Entry with spaces");
        Assert.Contains(records, record => record.entry == "Entry-with-dashes");
        Assert.Contains(records, record => record.entry == "Entry_with_underscores");
        Assert.Contains(records, record => record.entry == "Entry'with'quotes");
    }

    [Fact]
    public async Task ProcessFormIdListFile_TracksProcessedPlugins_Correctly()
    {
        var testFile = Path.Combine(_testFilesDir, "plugin_tracking.txt");
        await File.WriteAllLinesAsync(testFile,
            [
                "Plugin1.esp|000001|Entry1",
                "Plugin1.esp|000002|Entry2",
                "Plugin2.esp|000003|Entry3",
                "Plugin1.esp|000004|Entry4",
                "Plugin3.esp|000005|Entry5",
                "Plugin2.esp|000006|Entry6"
            ],
            TestContext.Current.CancellationToken);

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(progressReports.Add);

        await ProcessFileAsync(testFile, updateMode: false, progress: progress);

        Assert.Contains(progressReports,
            report => report.Message.Contains("Completed processing 3 plugins", StringComparison.Ordinal) &&
                      report.Message.Contains("total records", StringComparison.Ordinal));
    }

    #endregion

    #region Case-Insensitive Plugin Tests

    [Fact]
    public async Task ProcessFormIdListFile_TreatsDifferentCasePluginNames_AsSamePlugin()
    {
        var testFile = Path.Combine(_testFilesDir, "case_insensitive.txt");
        await File.WriteAllLinesAsync(testFile,
            ["Plugin1.esp|000001|Entry1", "plugin1.esp|000002|Entry2", "PLUGIN1.ESP|000003|Entry3"],
            TestContext.Current.CancellationToken);

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(progressReports.Add);

        await ProcessFileAsync(testFile, updateMode: false, progress: progress);

        Assert.Equal(3, GetAllRecords().Count);
        Assert.Contains(progressReports, report => report.Message.Contains("Completed processing 1 plugins", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessFormIdListFile_CaseInsensitive_DoesNotDuplicateClearPluginEntries()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldEntry1");

        var testFile = Path.Combine(_testFilesDir, "case_clear.txt");
        await File.WriteAllLinesAsync(testFile,
            ["Plugin1.esp|000001|NewEntry1", "PLUGIN1.ESP|000002|NewEntry2"],
            TestContext.Current.CancellationToken);

        await ProcessFileAsync(testFile, updateMode: true);

        var records = GetAllRecords();
        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => record.entry == "NewEntry1");
        Assert.Contains(records, record => record.entry == "NewEntry2");
        Assert.DoesNotContain(records, record => record.entry == "OldEntry1");
    }

    #endregion

    #region Helper Methods

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            handler(value);
        }
    }

    private Task ProcessFileAsync(
        string testFile,
        bool updateMode,
        IProgress<(string Message, double? Value)>? progress = null)
    {
        return ProcessFileAsync(testFile, updateMode, TestContext.Current.CancellationToken, progress);
    }

    private async Task ProcessFileAsync(
        string testFile,
        bool updateMode,
        CancellationToken cancellationToken,
        IProgress<(string Message, double? Value)>? progress = null)
    {
        await using var store = await OpenStoreAsync(cancellationToken);
        await _processor.ProcessFormIdListFile(testFile, store, updateMode, cancellationToken, progress);
    }

    private Task<FormIdRecordStore> OpenStoreAsync(CancellationToken cancellationToken)
    {
        return FormIdRecordStore.OpenAsync(_testDbPath, GameRelease.SkyrimSE, cancellationToken);
    }

    private List<(string plugin, string formid, string entry)> GetAllRecords()
    {
        var records = new List<(string plugin, string formid, string entry)>();
        using var connection = new SqliteConnection(DatabaseService.GetOptimizedConnectionString(_testDbPath));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT plugin, formid, entry FROM {GameRelease.SkyrimSE} ORDER BY id";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return records;
    }

    private async Task InsertTestRecordAsync(string plugin, string formId, string entry)
    {
        await _databaseService.InitializeDatabase(_testDbPath, GameRelease.SkyrimSE, TestContext.Current.CancellationToken);

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
        await _databaseService.InitializeDatabase(_testDbPath, GameRelease.SkyrimSE, TestContext.Current.CancellationToken);

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

    #endregion
}
