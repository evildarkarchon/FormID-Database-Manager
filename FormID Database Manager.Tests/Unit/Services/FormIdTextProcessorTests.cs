using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class FormIdTextProcessorTests : IDisposable
{
    private readonly DatabaseService _databaseService;
    private readonly FormIdTextProcessor _processor;
    private readonly string _testDbPath;
    private readonly SQLiteConnection _connection;
    private readonly string _testFilesDir;

    public FormIdTextProcessorTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        _testFilesDir = Path.Combine(Path.GetTempPath(), $"test_files_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testFilesDir);

        _databaseService = new DatabaseService();
        _processor = new FormIdTextProcessor(_databaseService);

        // Create and open connection for tests
        _connection = new SQLiteConnection($"Data Source={_testDbPath};Version=3;");
        _connection.Open();
        InitializeDatabase(_connection, GameRelease.SkyrimSE);
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
        if (Directory.Exists(_testFilesDir))
        {
            Directory.Delete(_testFilesDir, true);
        }
    }

    #region Core Functionality Tests

    [Fact]
    public async Task ProcessFormIdListFile_ParsesValidFormat_Correctly()
    {
        // Arrange
        var testFile = Path.Combine(_testFilesDir, "valid_formids.txt");
        var content = new[]
        {
            "TestPlugin.esp|000001|TestWeapon",
            "TestPlugin.esp|000002|TestArmor",
            "TestPlugin2.esp|000003|TestSpell"
        };
        await File.WriteAllLinesAsync(testFile, content);

        // Act
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            CancellationToken.None);

        // Assert
        var records = GetAllRecords();
        Assert.Equal(3, records.Count);
        Assert.Contains(records, r => r.plugin == "TestPlugin.esp" && r.formid == "000001" && r.entry == "TestWeapon");
        Assert.Contains(records, r => r.plugin == "TestPlugin.esp" && r.formid == "000002" && r.entry == "TestArmor");
        Assert.Contains(records, r => r.plugin == "TestPlugin2.esp" && r.formid == "000003" && r.entry == "TestSpell");
    }

    [Fact]
    public async Task ProcessFormIdListFile_HandlesDifferentLineFormats_Correctly()
    {
        // Arrange - Test with various whitespace and formatting
        var testFile = Path.Combine(_testFilesDir, "varied_format.txt");
        var content = new[]
        {
            "Plugin1.esp|000001|Entry1",
            "  Plugin2.esp  |  000002  |  Entry2  ", // Extra spaces
            "Plugin3.esp|000003|Entry3|ExtraData", // Extra pipe (should be ignored)
            "",  // Empty line
            "   ",  // Whitespace only
            "InvalidLine", // No pipes
            "Plugin4.esp|000004", // Missing entry
        };
        await File.WriteAllLinesAsync(testFile, content);

        // Act
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            CancellationToken.None);

        // Assert
        var records = GetAllRecords();
        Assert.Equal(2, records.Count); // Only valid lines should be processed
        Assert.Contains(records, r => r.plugin == "Plugin1.esp" && r.formid == "000001" && r.entry == "Entry1");
        Assert.Contains(records, r => r.plugin == "Plugin2.esp" && r.formid == "000002" && r.entry == "Entry2");
    }

    [Fact]
    public async Task ProcessFormIdListFile_HandlesMultiplePlugins_InSingleFile()
    {
        // Arrange
        var testFile = Path.Combine(_testFilesDir, "multiple_plugins.txt");
        var content = new[]
        {
            "Plugin1.esp|000001|Entry1",
            "Plugin1.esp|000002|Entry2",
            "Plugin2.esp|000003|Entry3",
            "Plugin2.esp|000004|Entry4",
            "Plugin1.esp|000005|Entry5", // Back to Plugin1
            "Plugin3.esp|000006|Entry6"
        };
        await File.WriteAllLinesAsync(testFile, content);

        // Act
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            CancellationToken.None);

        // Assert
        var records = GetAllRecords();
        Assert.Equal(6, records.Count);

        var plugin1Records = records.Where(r => r.plugin == "Plugin1.esp").ToList();
        Assert.Equal(3, plugin1Records.Count);

        var plugin2Records = records.Where(r => r.plugin == "Plugin2.esp").ToList();
        Assert.Equal(2, plugin2Records.Count);

        var plugin3Records = records.Where(r => r.plugin == "Plugin3.esp").ToList();
        Assert.Single(plugin3Records);
    }

    #endregion

    #region Progress and UI Update Tests

    [Fact]
    public async Task ProcessFormIdListFile_ReportsProgress_AtRegularIntervals()
    {
        // Arrange
        var testFile = Path.Combine(_testFilesDir, "progress_test.txt");
        var lines = new List<string>();
        for (int i = 0; i < 2500; i++) // More than UiUpdateInterval (1000)
        {
            lines.Add($"Plugin.esp|{i:X6}|Entry{i}");
        }
        await File.WriteAllLinesAsync(testFile, lines);

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new Progress<(string Message, double? Value)>(report => progressReports.Add(report));

        // Act
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            CancellationToken.None,
            progress);

        // Assert
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, r => r.Message.Contains("Starting processing"));
        Assert.Contains(progressReports, r => r.Message.Contains("Processing:") && r.Value.HasValue);
        Assert.Contains(progressReports, r => r.Message.Contains("Completed processing"));

        // Verify progress values are increasing
        var progressValues = progressReports.Where(r => r.Value.HasValue).Select(r => r.Value!.Value).ToList();
        for (int i = 1; i < progressValues.Count; i++)
        {
            Assert.True(progressValues[i] >= progressValues[i - 1]);
        }
    }

    [Fact]
    public async Task ProcessFormIdListFile_CalculatesTotalLines_ForAccurateProgress()
    {
        // Arrange
        var testFile = Path.Combine(_testFilesDir, "line_count_test.txt");
        var expectedLines = 100;
        var lines = Enumerable.Range(1, expectedLines).Select(i => $"Plugin.esp|{i:X6}|Entry{i}");
        await File.WriteAllLinesAsync(testFile, lines);

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new Progress<(string Message, double? Value)>(report => progressReports.Add(report));

        // Act
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            CancellationToken.None,
            progress);

        // Assert
        var completionReport = progressReports.Last(r => r.Message.Contains("Completed"));
        Assert.Contains($"{expectedLines:N0} total records", completionReport.Message);
        Assert.Equal(100, completionReport.Value);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task ProcessFormIdListFile_BatchesInserts_CorrectlyAtBatchSize()
    {
        // Arrange
        const int batchSize = 10000; // As defined in FormIdTextProcessor
        var testFile = Path.Combine(_testFilesDir, "batch_test.txt");
        var totalRecords = batchSize + 100; // Slightly more than one batch

        var lines = new List<string>();
        for (int i = 0; i < totalRecords; i++)
        {
            lines.Add($"Plugin.esp|{i:X6}|Entry{i}");
        }
        await File.WriteAllLinesAsync(testFile, lines);

        // Act
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            CancellationToken.None);

        // Assert
        var records = GetAllRecords();
        Assert.Equal(totalRecords, records.Count);
    }

    [Fact]
    public async Task ProcessFormIdListFile_HandlesLargeFiles_Efficiently()
    {
        // Arrange
        var testFile = Path.Combine(_testFilesDir, "large_file_test.txt");
        var totalRecords = 25000; // Large number to test efficiency

        using (var writer = new StreamWriter(testFile))
        {
            for (int i = 0; i < totalRecords; i++)
            {
                await writer.WriteLineAsync($"Plugin{i % 5}.esp|{i:X6}|Entry{i}");
            }
        }

        var startTime = DateTime.UtcNow;

        // Act
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            CancellationToken.None);

        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        var records = GetAllRecords();
        Assert.Equal(totalRecords, records.Count);
        Assert.True(elapsed.TotalSeconds < 60, $"Processing took too long: {elapsed.TotalSeconds} seconds"); // Increased threshold for CI environments
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ProcessFormIdListFile_HandlesEmptyFile_Gracefully()
    {
        // Arrange
        var testFile = Path.Combine(_testFilesDir, "empty.txt");
        await File.WriteAllTextAsync(testFile, string.Empty);

        // Act & Assert - Should not throw
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            CancellationToken.None);

        var records = GetAllRecords();
        Assert.Empty(records);
    }

    [Fact]
    public async Task ProcessFormIdListFile_HandlesInvalidFormat_WithoutCrashing()
    {
        // Arrange
        var testFile = Path.Combine(_testFilesDir, "invalid_format.txt");
        var content = new[]
        {
            "This is not a valid format",
            "Neither|is|this|one|with|too|many|pipes",
            "OrThis|WithTooFew",
            null!, // Null line
            "||||", // Empty fields
        };
        await File.WriteAllLinesAsync(testFile, content.Where(c => c != null));

        // Act & Assert - Should process without throwing
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            CancellationToken.None);

        var records = GetAllRecords();
        Assert.Empty(records); // No valid records to process
    }

    [Fact]
    public async Task ProcessFormIdListFile_RespectsCancellationToken()
    {
        // Arrange
        // Increased record count to ensure cancellation has time to trigger
        // (optimized code now processes 10k records in <50ms)
        var testFile = Path.Combine(_testFilesDir, "cancellation_test.txt");
        var lines = new List<string>();
        for (int i = 0; i < 100000; i++) // Increased from 10,000 to 100,000
        {
            lines.Add($"Plugin.esp|{i:X6}|Entry{i}");
        }
        await File.WriteAllLinesAsync(testFile, lines);

        var cts = new CancellationTokenSource();
        var processTask = _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            cts.Token);

        // Act
        cts.CancelAfter(50); // Cancel after 50ms

        // Assert - TaskCanceledException inherits from OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processTask);

        // Verify partial processing
        var records = GetAllRecords();
        Assert.True(records.Count < 100000); // Should not have processed all records
    }

    [Fact]
    public async Task ProcessFormIdListFile_HandlesFileNotFound_Gracefully()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testFilesDir, "does_not_exist.txt");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _processor.ProcessFormIdListFile(
                nonExistentFile,
                _connection,
                GameRelease.SkyrimSE,
                false,
                CancellationToken.None));
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task ProcessFormIdListFile_HandlesSpecialCharacters_InFormIDs()
    {
        // Arrange
        var testFile = Path.Combine(_testFilesDir, "special_chars.txt");
        var content = new[]
        {
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
        };
        await File.WriteAllLinesAsync(testFile, content);

        // Act
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            CancellationToken.None);

        // Assert
        var records = GetAllRecords();
        Assert.Equal(10, records.Count);

        // Verify special characters are preserved
        Assert.Contains(records, r => r.entry == "Entry with spaces");
        Assert.Contains(records, r => r.entry == "Entry-with-dashes");
        Assert.Contains(records, r => r.entry == "Entry_with_underscores");
        Assert.Contains(records, r => r.entry == "Entry'with'quotes");
    }

    [Fact]
    public async Task ProcessFormIdListFile_TracksProcessedPlugins_Correctly()
    {
        // Arrange
        var testFile = Path.Combine(_testFilesDir, "plugin_tracking.txt");
        var content = new[]
        {
            "Plugin1.esp|000001|Entry1",
            "Plugin1.esp|000002|Entry2",
            "Plugin2.esp|000003|Entry3",
            "Plugin1.esp|000004|Entry4", // Same plugin again
            "Plugin3.esp|000005|Entry5",
            "Plugin2.esp|000006|Entry6"  // Same plugin again
        };
        await File.WriteAllLinesAsync(testFile, content);

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new Progress<(string Message, double? Value)>(report => progressReports.Add(report));

        // Act
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            false,
            CancellationToken.None,
            progress);

        // Give time for all progress reports to be captured
        await Task.Delay(100);

        // Assert
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, r => r.Message.Contains("Completed processing 3 plugins") && r.Message.Contains("total records")); // Should track 3 unique plugins
    }

    #endregion

    #region Update Mode Tests

    [Fact]
    public async Task ProcessFormIdListFile_ClearsExistingEntries_InUpdateMode()
    {
        // Arrange - Pre-populate database
        await InsertTestRecord("Plugin1.esp", "000001", "OldEntry1");
        await InsertTestRecord("Plugin1.esp", "000002", "OldEntry2");
        await InsertTestRecord("Plugin2.esp", "000003", "OldEntry3");

        var testFile = Path.Combine(_testFilesDir, "update_mode.txt");
        var content = new[]
        {
            "Plugin1.esp|000001|NewEntry1",
            "Plugin1.esp|000004|NewEntry4", // New record
            "Plugin2.esp|000003|UpdatedEntry3"
        };
        await File.WriteAllLinesAsync(testFile, content);

        // Act
        await _processor.ProcessFormIdListFile(
            testFile,
            _connection,
            GameRelease.SkyrimSE,
            true, // Update mode
            CancellationToken.None);

        // Assert
        var records = GetAllRecords();
        Assert.Equal(3, records.Count);

        // Old Plugin1 entries should be replaced
        Assert.DoesNotContain(records, r => r.entry == "OldEntry1");
        Assert.DoesNotContain(records, r => r.entry == "OldEntry2");
        Assert.Contains(records, r => r.entry == "NewEntry1");
        Assert.Contains(records, r => r.entry == "NewEntry4");

        // Plugin2 should be updated
        Assert.DoesNotContain(records, r => r.entry == "OldEntry3");
        Assert.Contains(records, r => r.entry == "UpdatedEntry3");
    }

    #endregion

    #region Transaction Tests

    [Fact]
    public async Task ProcessFormIdListFile_RollsBackOnError_MaintainsDataIntegrity()
    {
        // Arrange
        var testFile = Path.Combine(_testFilesDir, "transaction_test.txt");
        var content = new[]
        {
            "Plugin1.esp|000001|Entry1",
            "Plugin1.esp|000002|Entry2"
        };
        await File.WriteAllLinesAsync(testFile, content);

        // Close connection to simulate database error
        _connection.Close();

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _processor.ProcessFormIdListFile(
                testFile,
                _connection,
                GameRelease.SkyrimSE,
                false,
                CancellationToken.None));

        // Re-open and verify no partial data
        _connection.Open();
        var records = GetAllRecords();
        Assert.Empty(records);
    }

    #endregion

    #region Helper Methods

    private void InitializeDatabase(SQLiteConnection connection, GameRelease gameRelease)
    {
        using var command = new SQLiteCommand(connection);
        command.CommandText = $@"
            CREATE TABLE IF NOT EXISTS {gameRelease} (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plugin TEXT NOT NULL,
                formid TEXT NOT NULL,
                entry TEXT NOT NULL
            )";
        command.ExecuteNonQuery();

        // Create indices
        command.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{gameRelease}_plugin ON {gameRelease}(plugin)";
        command.ExecuteNonQuery();
        command.CommandText = $"CREATE INDEX IF NOT EXISTS idx_{gameRelease}_formid ON {gameRelease}(formid)";
        command.ExecuteNonQuery();
    }

    private List<(string plugin, string formid, string entry)> GetAllRecords()
    {
        var records = new List<(string plugin, string formid, string entry)>();
        using var cmd = new SQLiteCommand($"SELECT plugin, formid, entry FROM {GameRelease.SkyrimSE}", _connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            records.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)
            ));
        }
        return records;
    }

    private async Task InsertTestRecord(string plugin, string formId, string entry)
    {
        using var cmd = new SQLiteCommand(
            $"INSERT INTO {GameRelease.SkyrimSE} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)",
            _connection);
        cmd.Parameters.AddWithValue("@plugin", plugin);
        cmd.Parameters.AddWithValue("@formid", formId);
        cmd.Parameters.AddWithValue("@entry", entry);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion
}
