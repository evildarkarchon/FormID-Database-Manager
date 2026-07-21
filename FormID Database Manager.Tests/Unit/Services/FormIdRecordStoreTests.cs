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

public sealed class FormIdRecordStoreTests : IDisposable
{
    private readonly string _testDbPath = Path.Combine(Path.GetTempPath(), $"record_store_{Guid.NewGuid():N}.db");
    private readonly DatabaseService _databaseService = new();
    private readonly string _testFilesDirectory;

    /// <summary>
    ///     Creates isolated database and FormID text file paths for each test.
    /// </summary>
    public FormIdRecordStoreTests()
    {
        _testFilesDirectory = Path.Combine(Path.GetTempPath(), $"record_store_files_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testFilesDirectory);
    }

    /// <summary>
    ///     Removes the isolated database and FormID text files created by the test.
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

    /// <summary>
    ///     Verifies that valid, interleaved rows for multiple Plugins are parsed and persisted through the store.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_ValidRowsForMultiplePlugins_StoresEveryRecord()
    {
        var testFile = await WriteFormIdTextFileAsync(
            "valid_multiple_plugins.txt",
            [
                "Plugin1.esp|000001|Entry1",
                "Plugin1.esp|000002|Entry2",
                "Plugin2.esp|000003|Entry3",
                "Plugin2.esp|000004|Entry4",
                "Plugin1.esp|000005|Entry5",
                "Plugin3.esp|000006|Entry6"
            ]);
        await using var store = await OpenStoreAsync();

        var result = await store.ImportFormIdTextFileAsync(
            testFile,
            UpdateMode.Append,
            cancellationToken: TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(3, 6), result);
        Assert.Equal(6, records.Count);
        Assert.Equal(3, records.Count(record => record.Plugin == "Plugin1.esp"));
        Assert.Equal(2, records.Count(record => record.Plugin == "Plugin2.esp"));
        Assert.Single(records, record => record.Plugin == "Plugin3.esp");
        Assert.Contains(records, record => record is { Plugin: "Plugin1.esp", FormId: "000001", Entry: "Entry1" });
        Assert.Contains(records, record => record is { Plugin: "Plugin3.esp", FormId: "000006", Entry: "Entry6" });
    }

    /// <summary>
    ///     Verifies that whitespace is trimmed and malformed rows are skipped without hiding valid rows.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_MalformedAndWhitespaceRows_ImportsOnlyValidTrimmedRows()
    {
        var testFile = await WriteFormIdTextFileAsync(
            "malformed_rows.txt",
            [
                "Plugin1.esp|000001|Entry1",
                "  Plugin2.esp  |  000002  |  Entry2  ",
                "Plugin3.esp|000003|Entry3|ExtraData",
                "",
                "   ",
                "InvalidLine",
                "Plugin4.esp|000004",
                "||||"
            ]);
        await using var store = await OpenStoreAsync();

        var result = await store.ImportFormIdTextFileAsync(
            testFile,
            UpdateMode.Append,
            cancellationToken: TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(2, 2), result);
        Assert.Equal(
            [
                new FormIdStoredRecord("Plugin1.esp", "000001", "Entry1"),
                new FormIdStoredRecord("Plugin2.esp", "000002", "Entry2")
            ],
            records);
    }

    /// <summary>
    ///     Verifies that an empty FormID text file completes with zero imported rows.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_EmptyFile_ReturnsZeroCounts()
    {
        var testFile = await WriteFormIdTextFileAsync("empty.txt", []);
        await using var store = await OpenStoreAsync();

        var result = await store.ImportFormIdTextFileAsync(
            testFile,
            UpdateMode.Append,
            cancellationToken: TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(0, 0), result);
        Assert.Empty(records);
    }

    /// <summary>
    ///     Verifies that importing a missing FormID text file surfaces the file-system error.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var missingFile = Path.Combine(_testFilesDirectory, "missing.txt");
        await using var store = await OpenStoreAsync();

        await Assert.ThrowsAsync<FileNotFoundException>(() => store.ImportFormIdTextFileAsync(
            missingFile,
            UpdateMode.Append,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Verifies that supported punctuation in FormID text Entries round-trips unchanged.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_SpecialCharacters_PreservesEntries()
    {
        string[] expectedEntries =
        [
            "Entry with spaces",
            "Entry-with-dashes",
            "Entry_with_underscores",
            "Entry.with.dots",
            "Entry'with'quotes",
            @"Entry\with\backslashes",
            "Entry/with/forward/slashes",
            "Entry(with)parentheses",
            "Entry[with]brackets",
            "Entry{with}braces"
        ];
        var lines = expectedEntries
            .Select((entry, index) => $"Plugin.esp|{index + 1:X6}|{entry}")
            .ToArray();
        var testFile = await WriteFormIdTextFileAsync("special_characters.txt", lines);
        await using var store = await OpenStoreAsync();

        var result = await store.ImportFormIdTextFileAsync(
            testFile,
            UpdateMode.Append,
            cancellationToken: TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(1, expectedEntries.Length), result);
        Assert.Equal(expectedEntries, records.Select(record => record.Entry).ToArray());
    }

    /// <summary>
    ///     Verifies start, interval, and completion progress across multiple progress-report intervals.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_CrossesProgressIntervals_ReportsMonotonicProgressAndFinalCounts()
    {
        const int totalRecords = 2500;
        var lines = Enumerable.Range(0, totalRecords).Select(i => $"Plugin.esp|{i:X6}|Entry{i}");
        var testFile = await WriteFormIdTextFileAsync("progress.txt", lines);
        var progressReports = new List<FormIdStoreProgress>();
        var progress = new SynchronousProgress<FormIdStoreProgress>(progressReports.Add);
        await using var store = await OpenStoreAsync();

        var result = await store.ImportFormIdTextFileAsync(
            testFile,
            UpdateMode.Append,
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(1, totalRecords), result);
        Assert.Contains(
            progressReports,
            report => report is { Message: "Starting processing...", Value: 0 });
        Assert.Contains(
            progressReports,
            report => report.Message.Contains("Processing:", StringComparison.Ordinal) && report.Value.HasValue);

        var completionReport = progressReports.Last();
        Assert.Contains($"{totalRecords:N0} total records", completionReport.Message, StringComparison.Ordinal);
        Assert.Equal(100, completionReport.Value);

        var progressValues = progressReports
            .Where(report => report.Value.HasValue)
            .Select(report => report.Value!.Value)
            .ToArray();
        Assert.All(progressValues, value => Assert.InRange(value, 0, 100));
        for (var i = 1; i < progressValues.Length; i++)
        {
            Assert.True(progressValues[i] >= progressValues[i - 1]);
        }
    }

    /// <summary>
    ///     Verifies that imports exceeding the 10,000-row managed batch use file-backed SQLite staging and commit all rows.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_ExceedsStagingBatchSize_UsesFileBackedStagingAndCommitsAllRecords()
    {
        const int totalRecords = 10100;
        const long fileBackedTemporaryStorageMode = 1;
        var lines = Enumerable.Range(0, totalRecords).Select(i => $"Plugin.esp|{i:X6}|Entry{i}");
        var testFile = await WriteFormIdTextFileAsync("batch_boundary.txt", lines);
        var databaseService = new TemporaryStorageInspectingDatabaseService();
        await using var store = await FormIdRecordStore.OpenAsync(
            databaseService,
            _testDbPath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);

        var result = await store.ImportFormIdTextFileAsync(
            testFile,
            UpdateMode.Append,
            cancellationToken: TestContext.Current.CancellationToken);

        var temporaryStorageMode = await databaseService.ReadTemporaryStorageModeAsync(
            TestContext.Current.CancellationToken);
        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        Assert.Equal(fileBackedTemporaryStorageMode, temporaryStorageMode);
        Assert.Equal(new FormIdTextFileImportResult(1, totalRecords), result);
        Assert.Equal(totalRecords, records.Count);
    }

    /// <summary>
    ///     Verifies that importing many distinct Plugins does not compare every staged row for every Plugin commit.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_ManyDistinctPlugins_KeepsStagingLookupWorkSubquadratic()
    {
        const int pluginCount = 512;
        // A full scan needs roughly one comparison per Plugin-row pair; this bound leaves indexed work ample headroom.
        const long maximumNoCaseComparisons = pluginCount * pluginCount / 4;
        var lines = Enumerable.Range(0, pluginCount)
            .Select(i => $"Plugin{i:D4}.esp|{i:X6}|Entry{i}");
        var testFile = await WriteFormIdTextFileAsync("many_distinct_plugins.txt", lines);
        var databaseService = new NoCaseComparisonCountingDatabaseService();
        await using var store = await FormIdRecordStore.OpenAsync(
            databaseService,
            _testDbPath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);
        databaseService.ResetComparisonCount();

        var result = await store.ImportFormIdTextFileAsync(
            testFile,
            UpdateMode.Append,
            cancellationToken: TestContext.Current.CancellationToken);
        var comparisonCount = databaseService.ComparisonCount;
        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(pluginCount, pluginCount), result);
        Assert.Equal(pluginCount, records.Count);
        Assert.InRange(comparisonCount, 1, maximumNoCaseComparisons);
    }

    /// <summary>
    ///     Verifies that cancellation during parsing stops the import before target rows are committed.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_CancelledDuringParsing_ThrowsAndCommitsNoRecords()
    {
        var lines = Enumerable.Range(0, 2500).Select(i => $"Plugin.esp|{i:X6}|Entry{i}");
        var testFile = await WriteFormIdTextFileAsync("cancelled.txt", lines);
        using var cancellationSource = new CancellationTokenSource();
        var progress = new SynchronousProgress<FormIdStoreProgress>(report =>
        {
            if (report.Message.Contains("Processing:", StringComparison.Ordinal))
            {
                cancellationSource.Cancel();
            }
        });
        await using var store = await OpenStoreAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ImportFormIdTextFileAsync(
            testFile,
            UpdateMode.Append,
            progress,
            cancellationSource.Token));

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);
        Assert.Empty(records);
    }

    /// <summary>
    ///     Verifies that append imports preserve existing rows for the same Plugin.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_AppendMode_PreservesExistingRows()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldEntry");
        var testFile = await WriteFormIdTextFileAsync(
            "append.txt",
            ["Plugin1.esp|000002|NewEntry"]);
        await using var store = await OpenStoreAsync();

        var result = await store.ImportFormIdTextFileAsync(
            testFile,
            UpdateMode.Append,
            cancellationToken: TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(1, 1), result);
        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => record is { Plugin: "Plugin1.esp", FormId: "000001", Entry: "OldEntry" });
        Assert.Contains(records, record => record is { Plugin: "Plugin1.esp", FormId: "000002", Entry: "NewEntry" });
    }

    /// <summary>
    ///     Verifies case-insensitive replacement, source casing, and new-Plugin insertion in one Update Mode import.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_UpdateMode_ReplacesExistingPluginAndInsertsNewPlugin()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldEntry1");
        await InsertTestRecordAsync("Plugin1.esp", "000002", "OldEntry2");
        var testFile = await WriteFormIdTextFileAsync(
            "update_mode.txt",
            [
                "PLUGIN1.ESP|000010|NewEntry1",
                "plugin1.esp|000011|NewEntry2",
                "BrandNewPlugin.esp|000020|BrandNewEntry"
            ]);
        await using var store = await OpenStoreAsync();

        var result = await store.ImportFormIdTextFileAsync(
            testFile,
            UpdateMode.ReplacePluginRecords,
            cancellationToken: TestContext.Current.CancellationToken);

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(2, 3), result);
        Assert.Equal(3, records.Count);
        Assert.DoesNotContain(records, record => record.Entry is "OldEntry1" or "OldEntry2");
        Assert.Contains(records, record => record is { Plugin: "PLUGIN1.ESP", FormId: "000010", Entry: "NewEntry1" });
        Assert.Contains(records, record => record is { Plugin: "plugin1.esp", FormId: "000011", Entry: "NewEntry2" });
        Assert.Contains(
            records,
            record => record is { Plugin: "BrandNewPlugin.esp", FormId: "000020", Entry: "BrandNewEntry" });
    }

    /// <summary>
    ///     Verifies that a failed Plugin import rolls back its rows while a later Plugin can still commit.
    /// </summary>
    [Fact]
    public async Task ImportFormIdTextFileAsync_PluginCommitFails_RollsBackPluginAndContinues()
    {
        await InsertTestRecordAsync("Plugin1.esp", "000001", "OldPlugin1");
        await InsertTestRecordAsync("Plugin2.esp", "000002", "OldPlugin2");
        await CreateFailingInsertTriggerAsync("BadEntry");
        var testFile = await WriteFormIdTextFileAsync(
            "rollback.txt",
            [
                "Plugin1.esp|000010|GoodEntry",
                "Plugin1.esp|000011|BadEntry",
                "Plugin2.esp|000020|NewPlugin2"
            ]);
        await using var store = await OpenStoreAsync();

        await Assert.ThrowsAsync<SqliteException>(() => store.ImportFormIdTextFileAsync(
            testFile,
            UpdateMode.ReplacePluginRecords,
            cancellationToken: TestContext.Current.CancellationToken));

        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        Assert.Equal(2, records.Count);
        Assert.Contains(
            records,
            record => record is { Plugin: "Plugin1.esp", FormId: "000001", Entry: "OldPlugin1" });
        Assert.DoesNotContain(records, record => record.Entry is "GoodEntry" or "BadEntry" or "OldPlugin2");
        Assert.Contains(
            records,
            record => record is { Plugin: "Plugin2.esp", FormId: "000020", Entry: "NewPlugin2" });
    }

    private Task<FormIdRecordStore> OpenStoreAsync()
    {
        return FormIdRecordStore.OpenAsync(
            _databaseService,
            _testDbPath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Writes a FormID text file into this test's isolated directory.
    /// </summary>
    /// <param name="fileName">The file name within the isolated directory.</param>
    /// <param name="lines">The FormID text rows to write.</param>
    /// <returns>The absolute path to the written file.</returns>
    private async Task<string> WriteFormIdTextFileAsync(string fileName, IEnumerable<string> lines)
    {
        var path = Path.Combine(_testFilesDirectory, fileName);
        await File.WriteAllLinesAsync(path, lines, TestContext.Current.CancellationToken);
        return path;
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

    private sealed class NoCaseComparisonCountingDatabaseService : DatabaseService
    {
        private long _comparisonCount;

        /// <summary>
        ///     Gets the number of case-insensitive comparisons performed since the last reset.
        /// </summary>
        public long ComparisonCount => Interlocked.Read(ref _comparisonCount);

        /// <inheritdoc />
        public override async Task ConfigureConnection(
            SqliteConnection conn,
            CancellationToken cancellationToken = default)
        {
            await base.ConfigureConnection(conn, cancellationToken);
            conn.CreateCollation("NOCASE", (left, right) =>
            {
                Interlocked.Increment(ref _comparisonCount);
                return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        ///     Starts a new comparison-counting interval for a FormID Record Store operation.
        /// </summary>
        public void ResetComparisonCount()
        {
            Interlocked.Exchange(ref _comparisonCount, 0);
        }
    }

    private sealed class TemporaryStorageInspectingDatabaseService : DatabaseService
    {
        private SqliteConnection _configuredConnection = null!;

        /// <inheritdoc />
        public override async Task ConfigureConnection(
            SqliteConnection conn,
            CancellationToken cancellationToken = default)
        {
            await base.ConfigureConnection(conn, cancellationToken);
            _configuredConnection = conn;
        }

        /// <summary>
        ///     Reads SQLite's temporary-storage mode from the run-scoped FormID Record Store connection.
        /// </summary>
        /// <param name="cancellationToken">Token to monitor for cancellation.</param>
        /// <returns>The numeric SQLite <c>temp_store</c> mode.</returns>
        public async Task<long> ReadTemporaryStorageModeAsync(CancellationToken cancellationToken)
        {
            if (_configuredConnection is null)
            {
                throw new InvalidOperationException("No FormID Record Store connection has been configured.");
            }

            await using var command = _configuredConnection.CreateCommand();
            command.CommandText = "PRAGMA temp_store";
            var mode = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(mode);
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            handler(value);
        }
    }
}
