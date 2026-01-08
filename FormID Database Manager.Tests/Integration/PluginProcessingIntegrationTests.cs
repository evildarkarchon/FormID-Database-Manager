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

    [Fact(Skip = "Causes test host crash")]
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
        await File.WriteAllLinesAsync(formIdListPath, formIdContent);

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

    [ExpectsGameEnvironmentFailureFact]
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
        await File.WriteAllLinesAsync(formIdListPath, formIdContent);

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

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugins_CompleteWorkflow_FailsWithoutGameInstallation()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "test.db");
        _tempFiles.Add(dbPath);

        var dataPath = Path.Combine(_testDirectory, "Data");
        CreateTestPlugin(dataPath, "TestPlugin1.esp");
        CreateTestPlugin(dataPath, "TestPlugin2.esp");

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            SelectedPlugins =
            [
                new() { Name = "TestPlugin1.esp", IsSelected = true },
                new() { Name = "TestPlugin2.esp", IsSelected = true }
            ],
            UpdateMode = false,
            DryRun = false
        };

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(report => progressReports.Add(report));

        // Act & Assert - GameEnvironment will fail without real game
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _processingService.ProcessPlugins(parameters, progress));
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugins_HandlesMultipleGameTypes_FailsWithoutGames()
    {
        // Arrange
        var gameReleases = new[] { GameRelease.SkyrimSE, GameRelease.Fallout4, GameRelease.Starfield };

        foreach (var gameRelease in gameReleases)
        {
            var dbPath = Path.Combine(_testDirectory, $"{gameRelease}.db");
            _tempFiles.Add(dbPath);

            var dataPath = Path.Combine(_testDirectory, "Data");
            CreateTestPlugin(dataPath, $"{gameRelease}_Test.esp");

            var parameters = new ProcessingParameters
            {
                GameDirectory = _testDirectory,
                DatabasePath = dbPath,
                GameRelease = gameRelease,
                SelectedPlugins =
                [
                    new() { Name = $"{gameRelease}_Test.esp", IsSelected = true }
                ],
                UpdateMode = false,
                DryRun = false
            };

            // Act & Assert - GameEnvironment will fail without real games
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await _processingService.ProcessPlugins(parameters));
        }
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugins_UpdateMode_FailsWithoutGameInstallation()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "update_test.db");
        _tempFiles.Add(dbPath);

        var dataPath = Path.Combine(_testDirectory, "Data");
        CreateTestPlugin(dataPath, "UpdateTest.esp");

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            SelectedPlugins = [new() { Name = "UpdateTest.esp", IsSelected = true }],
            UpdateMode = false,
            DryRun = false
        };

        // Act & Assert - GameEnvironment will fail without real game
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _processingService.ProcessPlugins(parameters));
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

        await File.WriteAllTextAsync(formIdListPath, "Test content");

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

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugins_HandlesPluginErrors_ContinuesProcessing()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "error_test.db");
        _tempFiles.Add(dbPath);

        var dataPath = Path.Combine(_testDirectory, "Data");
        CreateTestPlugin(dataPath, "GoodPlugin.esp");
        CreateCorruptedPlugin(dataPath, "BadPlugin.esp");
        CreateTestPlugin(dataPath, "AnotherGoodPlugin.esp");

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            SelectedPlugins =
            [
                new() { Name = "GoodPlugin.esp", IsSelected = true },
                new() { Name = "BadPlugin.esp", IsSelected = true },
                new() { Name = "AnotherGoodPlugin.esp", IsSelected = true }
            ],
            UpdateMode = false,
            DryRun = false
        };

        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(report => progressReports.Add(report));

        // Act
        await _processingService.ProcessPlugins(parameters, progress);

        // Assert
        Assert.Contains(progressReports, r => r.Message.Contains("Error processing plugin BadPlugin.esp"));
        Assert.Contains(progressReports,
            r => r.Message.Contains("Processing completed with") && r.Message.Contains("failed"));
        Assert.NotEmpty(_viewModel.ErrorMessages);
        Assert.Contains(_viewModel.ErrorMessages, msg => msg.Contains("Failed to process plugin BadPlugin.esp"));
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

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugins_CancellationBeforeStart_ThrowsImmediately()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "cancel_test.db");
        _tempFiles.Add(dbPath);

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            SelectedPlugins = [new() { Name = "Test.esp", IsSelected = true }],
            UpdateMode = false,
            DryRun = false
        };

        // Act - Cancel before starting
        _processingService.CancelProcessing();

        // Assert - Should fail due to GameEnvironment or cancellation
        await Assert.ThrowsAnyAsync<Exception>(() => _processingService.ProcessPlugins(parameters));
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugins_MultipleCancellations_HandledCorrectly()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "multi_cancel.db");
        _tempFiles.Add(dbPath);

        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            SelectedPlugins = [],
            UpdateMode = false,
            DryRun = false
        };

        // Act - Cancel multiple times
        _processingService.CancelProcessing(); // Cancel before start
        _processingService.CancelProcessing(); // Cancel again

        var task = _processingService.ProcessPlugins(parameters);
        _processingService.CancelProcessing(); // Cancel during

        // Assert - Should handle gracefully
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
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
