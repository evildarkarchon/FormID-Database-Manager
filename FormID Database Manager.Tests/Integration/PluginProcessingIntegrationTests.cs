using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Integration;

/// <summary>
///     Integration tests for the complete plugin processing workflow,
///     testing the interaction between PluginProcessingService and its dependencies.
/// </summary>
[Collection("Integration Tests")]
public class PluginProcessingIntegrationTests : IDisposable
{
    private readonly DatabaseService _databaseService;
    private readonly PluginProcessingService _processingService;
    private readonly List<string> _tempFiles;
    private readonly string _testDirectory;
    private readonly MainWindowViewModel _viewModel;
    private readonly SynchronousThreadDispatcher _dispatcher;

    public PluginProcessingIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"plugin_integration_{Guid.NewGuid()}");
        _tempFiles = [];

        // Use SynchronousThreadDispatcher to avoid deadlocks in non-[AvaloniaFact] tests.
        // The AvaloniaThreadDispatcher tries to post to the UI thread which isn't being pumped
        // in regular xUnit tests, causing deadlocks.
        _dispatcher = new SynchronousThreadDispatcher();
        _viewModel = new MainWindowViewModel(_dispatcher);
        _databaseService = new DatabaseService();
        _processingService = new PluginProcessingService(_databaseService, _viewModel, _dispatcher);

        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(Path.Combine(_testDirectory, "Data"));
    }

    public void Dispose()
    {
        _processingService?.Dispose();

        foreach (var file in _tempFiles.Where(File.Exists))
        {
            try
            {
                File.Delete(file);
            }
            catch { }
        }

        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch { }
        }
    }

    #region FormID List Processing Tests

    [Fact]
    public async Task ProcessPlugins_FormIdListProcessing_Integration()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "formid_test.db");
        var formIdListPath = Path.Combine(_testDirectory, "formids.txt");
        _tempFiles.Add(dbPath);
        _tempFiles.Add(formIdListPath);

        // Create FormID list file
        var formIdContent = new[]
        {
            "TestPlugin.esp|000001|TestWeapon", "TestPlugin.esp|000002|TestArmor",
            "AnotherPlugin.esp|000003|TestSpell"
        };
        File.WriteAllLines(formIdListPath, formIdContent);

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            FormIdListPath = formIdListPath,
            UpdateMode = false,
            DryRun = false
        };

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(report => progressReports.Add(report));

        // Act
        await _processingService.ProcessPlugins(parameters, progress);

        // Assert
        Assert.Contains(progressReports, r => r.Message.Contains("Processing completed successfully"));

        // Verify database contains FormID list data
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", connection);
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(3, count);
    }

    #endregion

    #region Progress Reporting Tests

    [Fact]
    public async Task ProcessPlugins_ReportsDetailedProgress_InDryRun()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "progress_test.db");

        var dataPath = Path.Combine(_testDirectory, "Data");
        var pluginNames = new[] { "Plugin1.esp", "Plugin2.esp", "Plugin3.esp" };
        foreach (var name in pluginNames)
        {
            CreateTestPlugin(dataPath, name);
        }

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            SelectedPlugins = pluginNames.Select(n => new PluginListItem { Name = n, IsSelected = true }).ToList(),
            UpdateMode = false,
            DryRun = true // Use dry run to avoid GameEnvironment issues
        };

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(report => progressReports.Add(report));

        // Act
        await _processingService.ProcessPlugins(parameters, progress);

        // Assert - In dry run mode
        Assert.Contains(progressReports, r => r.Message.Contains("Would process Plugin1.esp"));
        Assert.Contains(progressReports, r => r.Message.Contains("Would process Plugin2.esp"));
        Assert.Contains(progressReports, r => r.Message.Contains("Would process Plugin3.esp"));
        Assert.Equal(3, progressReports.Count);
    }

    #endregion

    #region Database Optimization Tests

    [Fact]
    public async Task ProcessPlugins_CreatesDatabase_WithFormIdList()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "optimize_test.db");
        var formIdListPath = Path.Combine(_testDirectory, "formids.txt");
        _tempFiles.Add(dbPath);
        _tempFiles.Add(formIdListPath);

        // Create FormID list file instead of plugins to avoid GameEnvironment
        var formIdContent = new[] { "TestPlugin.esp|000001|TestItem" };
        File.WriteAllLines(formIdListPath, formIdContent);

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            FormIdListPath = formIdListPath,
            UpdateMode = false,
            DryRun = false
        };

        // Act
        await _processingService.ProcessPlugins(parameters);

        // Assert - Database should exist and be optimized
        Assert.True(File.Exists(dbPath));
        var fileInfo = new FileInfo(dbPath);
        Assert.True(fileInfo.Length > 0);

        // Verify database is in good state
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var cmd = new SqliteCommand("PRAGMA integrity_check", connection);
        var result = cmd.ExecuteScalar() as string;
        Assert.Equal("ok", result);
    }

    #endregion

    #region Complete Workflow Tests

    [Fact]
    public async Task ProcessPlugins_FormIdListWorkflow_CompletesSuccessfully()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "workflow_test.db");
        var formIdListPath = Path.Combine(_testDirectory, "workflow_formids.txt");
        _tempFiles.Add(dbPath);
        _tempFiles.Add(formIdListPath);

        File.WriteAllLines(formIdListPath,
        [
            "TestPlugin1.esp|000001|WorkflowEntry1",
            "TestPlugin1.esp|000002|WorkflowEntry2",
            "TestPlugin2.esp|000003|WorkflowEntry3"
        ]);

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            FormIdListPath = formIdListPath,
            UpdateMode = false,
            DryRun = false
        };

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(report => progressReports.Add(report));

        // Act
        await _processingService.ProcessPlugins(parameters, progress);

        // Assert
        Assert.Contains(progressReports,
            r => r.Message.Contains("Completed processing 2 plugins") && r.Message.Contains("3 total records"));
        Assert.Contains(progressReports, r => r.Message.Contains("Processing completed successfully"));

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var countCmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", connection);
        var count = Convert.ToInt32(countCmd.ExecuteScalar());
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task ProcessPlugins_FormIdListSupportsMultipleGameReleases()
    {
        // Arrange
        var gameReleases = new[] { GameRelease.SkyrimSE, GameRelease.Fallout4, GameRelease.Starfield };

        foreach (var gameRelease in gameReleases)
        {
            var dbPath = Path.Combine(_testDirectory, $"{gameRelease}.db");
            var formIdListPath = Path.Combine(_testDirectory, $"{gameRelease}.txt");
            _tempFiles.Add(dbPath);
            _tempFiles.Add(formIdListPath);

            File.WriteAllLines(formIdListPath,
            [
                $"{gameRelease}_Test.esp|0000AA|{gameRelease} Entry"
            ]);

            var parameters = new ProcessingParameters
            {
                GameDirectory = _testDirectory,
                DatabasePath = dbPath,
                GameRelease = gameRelease,
                FormIdListPath = formIdListPath,
                UpdateMode = false,
                DryRun = false
            };

            // Act
            await _processingService.ProcessPlugins(parameters);

            // Assert
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var countCmd = new SqliteCommand($"SELECT COUNT(*) FROM {gameRelease}", connection);
            var count = Convert.ToInt32(countCmd.ExecuteScalar());
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task ProcessPlugins_UpdateMode_ReplacesPluginEntriesFromFormIdList()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "update_test.db");
        var initialPath = Path.Combine(_testDirectory, "update_initial.txt");
        var updatedPath = Path.Combine(_testDirectory, "update_new.txt");
        _tempFiles.Add(dbPath);
        _tempFiles.Add(initialPath);
        _tempFiles.Add(updatedPath);

        File.WriteAllLines(initialPath,
        [
            "UpdateTest.esp|000001|InitialEntry1",
            "UpdateTest.esp|000002|InitialEntry2"
        ]);

        File.WriteAllLines(updatedPath,
        [
            "UpdateTest.esp|000010|UpdatedEntry"
        ]);

        var initialParameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            FormIdListPath = initialPath,
            UpdateMode = false,
            DryRun = false
        };

        var updateParameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            FormIdListPath = updatedPath,
            UpdateMode = true,
            DryRun = false
        };

        // Act
        await _processingService.ProcessPlugins(initialParameters);
        await _processingService.ProcessPlugins(updateParameters);

        // Assert
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var pluginCountCmd = new SqliteCommand(
            $"SELECT COUNT(*) FROM {GameRelease.SkyrimSE} WHERE plugin = @plugin", connection);
        pluginCountCmd.Parameters.AddWithValue("@plugin", "UpdateTest.esp");
        var pluginCount = Convert.ToInt32(pluginCountCmd.ExecuteScalar());
        Assert.Equal(1, pluginCount);

        using var oldRecordCmd = new SqliteCommand(
            $"SELECT COUNT(*) FROM {GameRelease.SkyrimSE} WHERE plugin = @plugin AND formid = @formid", connection);
        oldRecordCmd.Parameters.AddWithValue("@plugin", "UpdateTest.esp");
        oldRecordCmd.Parameters.AddWithValue("@formid", "000001");
        var oldRecordCount = Convert.ToInt32(oldRecordCmd.ExecuteScalar());
        Assert.Equal(0, oldRecordCount);

        using var newRecordCmd = new SqliteCommand(
            $"SELECT COUNT(*) FROM {GameRelease.SkyrimSE} WHERE plugin = @plugin AND formid = @formid", connection);
        newRecordCmd.Parameters.AddWithValue("@plugin", "UpdateTest.esp");
        newRecordCmd.Parameters.AddWithValue("@formid", "000010");
        var newRecordCount = Convert.ToInt32(newRecordCmd.ExecuteScalar());
        Assert.Equal(1, newRecordCount);
    }

    #endregion

    #region Dry Run Tests

    [Fact]
    public async Task ProcessPlugins_DryRun_DoesNotModifyDatabase()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "dryrun_test.db");
        _tempFiles.Add(dbPath);

        var dataPath = Path.Combine(_testDirectory, "Data");
        CreateTestPlugin(dataPath, "DryRunTest.esp");

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            SelectedPlugins = [new() { Name = "DryRunTest.esp", IsSelected = true }],
            UpdateMode = false,
            DryRun = true // Dry run mode
        };

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(report => progressReports.Add(report));

        // Act
        await _processingService.ProcessPlugins(parameters, progress);

        // Assert - No delay needed since we use SynchronousProgress
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, r => r.Message.Contains("Would process"));
        Assert.False(File.Exists(dbPath)); // Database should not be created
    }

    [Fact]
    public async Task ProcessPlugins_DryRunWithFormIdList_ReportsCorrectly()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "dryrun_formid.db");
        var formIdListPath = Path.Combine(_testDirectory, "formids.txt");
        _tempFiles.Add(formIdListPath);

        File.WriteAllText(formIdListPath, "Test content");

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            FormIdListPath = formIdListPath,
            DryRun = true
        };

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(report => progressReports.Add(report));

        // Act
        await _processingService.ProcessPlugins(parameters, progress);

        // Assert - No delay needed since we use SynchronousProgress
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, r => r.Message.Contains("Would process FormID list file"));
        Assert.False(File.Exists(dbPath));
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ProcessPlugins_HandlesMalformedFormIdLines_ContinuesProcessing()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "malformed_lines_test.db");
        var formIdListPath = Path.Combine(_testDirectory, "malformed_lines.txt");
        _tempFiles.Add(dbPath);
        _tempFiles.Add(formIdListPath);

        File.WriteAllLines(formIdListPath,
        [
            "GoodPlugin.esp|000001|ValidEntry1",
            "MalformedLineWithoutPipes",
            "AnotherGoodPlugin.esp|000002|ValidEntry2",
            "Too|Many|Pipes|In|This|Line",
            "MissingEntryField|000003"
        ]);

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            FormIdListPath = formIdListPath,
            UpdateMode = false,
            DryRun = false
        };

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(report => progressReports.Add(report));

        // Act
        await _processingService.ProcessPlugins(parameters, progress);

        // Assert
        Assert.Contains(progressReports,
            r => r.Message.Contains("Completed processing 2 plugins") && r.Message.Contains("2 total records"));
        Assert.Contains(progressReports,
            r => r.Message.Contains("Processing completed successfully"));

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var countCmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", connection);
        var count = Convert.ToInt32(countCmd.ExecuteScalar());
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ProcessPlugins_HandlesInvalidDatabase_ThrowsException()
    {
        // Arrange
        var invalidDbPath = Path.Combine(_testDirectory, "subdir", "nonexistent", "test.db");

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = invalidDbPath,
            GameRelease = GameRelease.SkyrimSE,
            SelectedPlugins = [new() { Name = "Test.esp", IsSelected = true }],
            UpdateMode = false,
            DryRun = false
        };

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _processingService.ProcessPlugins(parameters));
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ProcessPlugins_CancellationBeforeStart_DoesNotAffectNextRun()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "cancel_test.db");
        var formIdListPath = Path.Combine(_testDirectory, "cancel_test.txt");
        _tempFiles.Add(dbPath);
        _tempFiles.Add(formIdListPath);

        File.WriteAllLines(formIdListPath,
        [
            "CancelTest.esp|000001|CancelEntry"
        ]);

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            FormIdListPath = formIdListPath,
            UpdateMode = false,
            DryRun = false
        };

        // Act - Cancel before starting
        _processingService.CancelProcessing();
        await _processingService.ProcessPlugins(parameters);

        // Assert
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var countCmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", connection);
        var count = Convert.ToInt32(countCmd.ExecuteScalar());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ProcessPlugins_MultipleCancellations_DoNotCorruptSubsequentRuns()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "multi_cancel.db");
        var initialPath = Path.Combine(_testDirectory, "multi_cancel_initial.txt");
        var updatedPath = Path.Combine(_testDirectory, "multi_cancel_updated.txt");
        _tempFiles.Add(dbPath);
        _tempFiles.Add(initialPath);
        _tempFiles.Add(updatedPath);

        File.WriteAllLines(initialPath,
        [
            "MultiCancel.esp|000001|Initial"
        ]);

        File.WriteAllLines(updatedPath,
        [
            "MultiCancel.esp|000002|Updated"
        ]);

        var initialParameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            FormIdListPath = initialPath,
            UpdateMode = false,
            DryRun = false
        };

        var updatedParameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            FormIdListPath = updatedPath,
            UpdateMode = true,
            DryRun = false
        };

        // Act - Cancel multiple times
        _processingService.CancelProcessing(); // Cancel before start
        _processingService.CancelProcessing(); // Cancel again
        await _processingService.ProcessPlugins(initialParameters);
        _processingService.CancelProcessing(); // Cancel between runs
        _processingService.CancelProcessing(); // Cancel again between runs
        await _processingService.ProcessPlugins(updatedParameters);

        // Assert - service still processes correctly after repeated cancels
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var countCmd = new SqliteCommand(
            $"SELECT COUNT(*) FROM {GameRelease.SkyrimSE} WHERE plugin = @plugin", connection);
        countCmd.Parameters.AddWithValue("@plugin", "MultiCancel.esp");
        var count = Convert.ToInt32(countCmd.ExecuteScalar());
        Assert.Equal(1, count);

        using var formIdCmd = new SqliteCommand(
            $"SELECT COUNT(*) FROM {GameRelease.SkyrimSE} WHERE plugin = @plugin AND formid = @formid", connection);
        formIdCmd.Parameters.AddWithValue("@plugin", "MultiCancel.esp");
        formIdCmd.Parameters.AddWithValue("@formid", "000002");
        var updatedCount = Convert.ToInt32(formIdCmd.ExecuteScalar());
        Assert.Equal(1, updatedCount);
    }

    #endregion

    #region Helper Methods

    private void CreateTestPlugin(string directory, string fileName)
    {
        var filePath = Path.Combine(directory, fileName);
        // Create minimal ESP/ESM file header
        var header = new byte[]
        {
            0x54, 0x45, 0x53, 0x34, // "TES4"
            0x2B, 0x00, 0x00, 0x00, // Size
            0x00, 0x00, 0x00, 0x00, // Flags
            0x00, 0x00, 0x00, 0x00, // FormID
            0x00, 0x00, 0x00, 0x00, // Timestamp
            0x00, 0x00, 0x00, 0x00 // Version info
        };
        File.WriteAllBytes(filePath, header);
    }

    private void CreateCorruptedPlugin(string directory, string fileName)
    {
        var filePath = Path.Combine(directory, fileName);
        // Create corrupted file
        var corruptedData = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00 };
        File.WriteAllBytes(filePath, corruptedData);
    }

    #endregion
}
