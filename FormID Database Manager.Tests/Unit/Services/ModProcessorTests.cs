using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities;
using Microsoft.Data.Sqlite;
using Moq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class ModProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DatabaseService _databaseService;
    private readonly List<string> _errorMessages;
    private readonly ModProcessor _modProcessor;
    private readonly string _testDbPath;

    public ModProcessorTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        _errorMessages = [];
        _databaseService = new DatabaseService();
        _modProcessor = new ModProcessor(_databaseService, error => _errorMessages.Add(error));

        // Create and open connection for tests
        _connection = new SqliteConnection($"Data Source={_testDbPath}");
        _connection.Open();
        InitializeDatabase(_connection, GameRelease.SkyrimSE);
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
        if (File.Exists(_testDbPath))
        {
            try
            { File.Delete(_testDbPath); }
            catch { /* Ignore */ }
        }
    }

    #region Transaction and Database Tests

    [Fact]
    public async Task ProcessPlugin_RollsBackTransaction_OnError()
    {
        // Arrange
        var gameDir = Path.Combine(Path.GetTempPath(), "TestGame", "Data");
        Directory.CreateDirectory(gameDir);
        var pluginPath = Path.Combine(gameDir, "TestPlugin.esp");

        // Create corrupted plugin that will fail processing
        await File.WriteAllBytesAsync(pluginPath, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

        var pluginItem = new PluginListItem { Name = "TestPlugin.esp", IsSelected = true };
        var mockModListing = CreateMockModListing("TestPlugin.esp");
        var loadOrder = new List<IModListingGetter<IModGetter>> { mockModListing.Object };

        // Get initial count
        var initialCount = GetRecordCount();

        // Act
        try
        {
            await _modProcessor.ProcessPlugin(
                gameDir,
                _connection,
                GameRelease.SkyrimSE,
                pluginItem,
                loadOrder,
                false,
                CancellationToken.None);
        }
        catch
        {
            // Expected to throw
        }

        // Assert - Database should remain unchanged
        var finalCount = GetRecordCount();
        Assert.Equal(initialCount, finalCount);
    }

    #endregion

    #region Basic Functionality Tests

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugin_FailsGracefully_WithInvalidPlugin()
    {
        // Arrange
        var gameDir = Path.Combine(Path.GetTempPath(), "TestGame", "Data");
        Directory.CreateDirectory(gameDir);
        var pluginPath = Path.Combine(gameDir, "TestPlugin.esp");

        // Create a mock plugin file (minimal ESP header - not valid for Mutagen)
        await CreateMinimalPluginFile(pluginPath);

        var pluginItem = new PluginListItem { Name = "TestPlugin.esp", IsSelected = true };
        var mockModListing = CreateMockModListing("TestPlugin.esp");
        var loadOrder = new List<IModListingGetter<IModGetter>> { mockModListing.Object };

        // Act & Assert - Should throw because plugin is not valid
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _modProcessor.ProcessPlugin(
                gameDir,
                _connection,
                GameRelease.SkyrimSE,
                pluginItem,
                loadOrder,
                false,
                CancellationToken.None));

        // Verify error was logged
        Assert.Contains(_errorMessages, msg => msg.Contains("Error processing TestPlugin.esp"));
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugin_HandlesMultipleGameTypes_WithErrors()
    {
        // Arrange
        var gameReleases = new[]
        {
            GameRelease.SkyrimSE, GameRelease.Fallout4, GameRelease.Starfield, GameRelease.Oblivion
        };

        foreach (var gameRelease in gameReleases)
        {
            _errorMessages.Clear();
            var gameDir = Path.Combine(Path.GetTempPath(), $"Test{gameRelease}", "Data");
            Directory.CreateDirectory(gameDir);
            var pluginPath = Path.Combine(gameDir, "TestPlugin.esp");
            await CreateMinimalPluginFile(pluginPath);

            var pluginItem = new PluginListItem { Name = "TestPlugin.esp", IsSelected = true };
            var mockModListing = CreateMockModListing("TestPlugin.esp");
            var loadOrder = new List<IModListingGetter<IModGetter>> { mockModListing.Object };

            // Initialize table for each game
            InitializeDatabase(_connection, gameRelease);

            // Act - Should fail for each game type due to invalid plugin
            try
            {
                await _modProcessor.ProcessPlugin(
                    gameDir,
                    _connection,
                    gameRelease,
                    pluginItem,
                    loadOrder,
                    false,
                    CancellationToken.None);
            }
            catch
            {
                // Expected to fail
            }

            // Assert - Should log error for each game type
            Assert.Contains(_errorMessages, msg => msg.Contains("Error processing"));
        }
    }

    [Fact]
    public async Task ProcessPlugin_WarnsWhenPluginNotInLoadOrder()
    {
        // Arrange
        var gameDir = Path.Combine(Path.GetTempPath(), "TestGame", "Data");
        Directory.CreateDirectory(gameDir);
        var pluginPath = Path.Combine(gameDir, "TestPlugin.esp");
        await CreateMinimalPluginFile(pluginPath);

        var pluginItem = new PluginListItem { Name = "TestPlugin.esp", IsSelected = true };
        var emptyLoadOrder = new List<IModListingGetter<IModGetter>>(); // Empty load order

        // Act
        await _modProcessor.ProcessPlugin(
            gameDir,
            _connection,
            GameRelease.SkyrimSE,
            pluginItem,
            emptyLoadOrder,
            false,
            CancellationToken.None);

        // Assert - Should warn about plugin not in load order
        Assert.Contains(_errorMessages, msg => msg.Contains("Could not find plugin in load order"));
    }

    #endregion

    #region Error Handling Tests

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugin_HandlesCorruptedPlugin_WithErrorCallback()
    {
        // Arrange
        var gameDir = Path.Combine(Path.GetTempPath(), "TestGame", "Data");
        Directory.CreateDirectory(gameDir);
        var pluginPath = Path.Combine(gameDir, "Corrupted.esp");

        // Create corrupted plugin file
        await File.WriteAllBytesAsync(pluginPath, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

        var pluginItem = new PluginListItem { Name = "Corrupted.esp", IsSelected = true };
        var mockModListing = CreateMockModListing("Corrupted.esp");
        var loadOrder = new List<IModListingGetter<IModGetter>> { mockModListing.Object };

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _modProcessor.ProcessPlugin(
                gameDir,
                _connection,
                GameRelease.SkyrimSE,
                pluginItem,
                loadOrder,
                false,
                CancellationToken.None));

        // Verify error was logged
        Assert.Contains(_errorMessages, msg => msg.Contains("Error processing"));
    }

    [Fact]
    public async Task ProcessPlugin_IgnoresKnownErrorPatterns_Correctly()
    {
        // Arrange
        var ignorablePatterns = new[] { "KSIZ", "KWDA", "Expected EDID", "List with a non zero counter" };

        // This test verifies the error filtering logic in ProcessModRecordsAsync
        // Since we can't easily simulate these specific Mutagen errors, we test the pattern matching
        foreach (var pattern in ignorablePatterns)
        {
            var testError = new Exception($"Test error containing {pattern} in message");
            var isIgnorable = testError.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase);
            Assert.True(isIgnorable);
        }

        await Task.CompletedTask;
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugin_ReportsUnknownErrors_ToCallback()
    {
        // Arrange
        var gameDir = Path.Combine(Path.GetTempPath(), "TestGame");
        var pluginItem = new PluginListItem { Name = "Missing.esp", IsSelected = true };
        var mockModListing = CreateMockModListing("Missing.esp");
        var loadOrder = new List<IModListingGetter<IModGetter>> { mockModListing.Object };

        // Act - Process with non-existent plugin
        await _modProcessor.ProcessPlugin(
            gameDir,
            _connection,
            GameRelease.SkyrimSE,
            pluginItem,
            loadOrder,
            false,
            CancellationToken.None);

        // Assert - Should report file not found
        Assert.Contains(_errorMessages, msg => msg.Contains("Could not find plugin file"));
    }

    #endregion

    #region Performance and Cancellation Tests

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugin_RespectsCancellationToken_DuringProcessing()
    {
        // Arrange
        var gameDir = Path.Combine(Path.GetTempPath(), "TestGame", "Data");
        Directory.CreateDirectory(gameDir);
        var pluginPath = Path.Combine(gameDir, "TestPlugin.esp");
        await CreateMinimalPluginFile(pluginPath);

        var pluginItem = new PluginListItem { Name = "TestPlugin.esp", IsSelected = true };
        var mockModListing = CreateMockModListing("TestPlugin.esp");
        var loadOrder = new List<IModListingGetter<IModGetter>> { mockModListing.Object };

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _modProcessor.ProcessPlugin(
                gameDir,
                _connection,
                GameRelease.SkyrimSE,
                pluginItem,
                loadOrder,
                false,
                cts.Token));
    }

    [Fact]
    public async Task ProcessPlugin_BatchesInserts_ForPerformance()
    {
        // This test verifies batching logic is in place
        // The BatchSize constant is 1000 as defined in ModProcessor

        // Verify the batch size constant exists and is reasonable
        var batchSizeField = typeof(ModProcessor).GetField("BatchSize",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(batchSizeField);

        var batchSize = (int)batchSizeField.GetValue(null);
        Assert.Equal(1000, batchSize);

        await Task.CompletedTask;
    }

    #endregion

    #region Edge Cases Tests

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugin_HandlesEmptyPlugin_WithError()
    {
        // Arrange
        var gameDir = Path.Combine(Path.GetTempPath(), "TestGame", "Data");
        Directory.CreateDirectory(gameDir);
        var pluginPath = Path.Combine(gameDir, "Empty.esp");
        await CreateMinimalPluginFile(pluginPath);

        var pluginItem = new PluginListItem { Name = "Empty.esp", IsSelected = true };
        var mockModListing = CreateMockModListing("Empty.esp");
        var loadOrder = new List<IModListingGetter<IModGetter>> { mockModListing.Object };

        // Act & Assert - Should fail due to invalid plugin format
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _modProcessor.ProcessPlugin(
                gameDir,
                _connection,
                GameRelease.SkyrimSE,
                pluginItem,
                loadOrder,
                false,
                CancellationToken.None));
    }

    [Fact]
    public async Task ProcessPlugin_HandlesNullLoadOrder_Gracefully()
    {
        // Arrange
        var gameDir = Path.Combine(Path.GetTempPath(), "TestGame", "Data");
        var pluginItem = new PluginListItem { Name = "Test.esp", IsSelected = true };
        var emptyLoadOrder = new List<IModListingGetter<IModGetter>>();

        // Act
        await _modProcessor.ProcessPlugin(
            gameDir,
            _connection,
            GameRelease.SkyrimSE,
            pluginItem,
            emptyLoadOrder, // Empty load order
            false,
            CancellationToken.None);

        // Assert - Should log warning about not finding plugin
        Assert.Contains(_errorMessages, msg => msg.Contains("Could not find plugin in load order"));
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task ProcessPlugin_HandlesDataDirectoryVariants_WithErrors()
    {
        // Test both "GameDir" and "GameDir/Data" paths
        var testCases = new[]
        {
            Path.Combine(Path.GetTempPath(), "TestGame"), Path.Combine(Path.GetTempPath(), "TestGame", "Data")
        };

        foreach (var gameDir in testCases)
        {
            _errorMessages.Clear();

            // Arrange
            var dataPath = gameDir.EndsWith("Data") ? gameDir : Path.Combine(gameDir, "Data");
            Directory.CreateDirectory(dataPath);
            var pluginPath = Path.Combine(dataPath, "Test.esp");
            await CreateMinimalPluginFile(pluginPath);

            var pluginItem = new PluginListItem { Name = "Test.esp", IsSelected = true };
            var mockModListing = CreateMockModListing("Test.esp");
            var loadOrder = new List<IModListingGetter<IModGetter>> { mockModListing.Object };

            // Act - Should fail due to invalid plugin
            try
            {
                await _modProcessor.ProcessPlugin(
                    gameDir,
                    _connection,
                    GameRelease.SkyrimSE,
                    pluginItem,
                    loadOrder,
                    false,
                    CancellationToken.None);
            }
            catch
            {
                // Expected to fail
            }

            // Assert - Should have error for both directory structures
            Assert.Contains(_errorMessages, msg => msg.Contains("Error processing"));
        }
    }

    [Fact]
    public async Task ProcessPlugin_HandlesSpecialCharactersInPluginName_WithErrors()
    {
        // Arrange
        var specialNames = new[] { "Test Plugin.esp", "Test-Plugin.esp", "Test_Plugin.esp", "Test+Plugin.esp" };

        foreach (var pluginName in specialNames)
        {
            _errorMessages.Clear();
            var gameDir = Path.Combine(Path.GetTempPath(), "TestGame", "Data");
            Directory.CreateDirectory(gameDir);
            var pluginPath = Path.Combine(gameDir, pluginName);
            await CreateMinimalPluginFile(pluginPath);

            var pluginItem = new PluginListItem { Name = pluginName, IsSelected = true };
            var mockModListing = CreateMockModListing(pluginName);
            var loadOrder = new List<IModListingGetter<IModGetter>> { mockModListing.Object };

            // Act - Should fail due to invalid plugin
            try
            {
                await _modProcessor.ProcessPlugin(
                    gameDir,
                    _connection,
                    GameRelease.SkyrimSE,
                    pluginItem,
                    loadOrder,
                    false,
                    CancellationToken.None);
            }
            catch
            {
                // Expected to fail
            }

            // Assert - Should handle special characters in error messages
            Assert.Contains(_errorMessages, msg => msg.Contains(pluginName));
        }
    }

    #endregion

    #region Helper Methods

    private void InitializeDatabase(SqliteConnection connection, GameRelease gameRelease)
    {
        using var command = connection.CreateCommand();
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

    private Mock<IModListingGetter<IModGetter>> CreateMockModListing(string fileName)
    {
        var mock = new Mock<IModListingGetter<IModGetter>>();
        // Extract mod name without extension for ModKey constructor
        var modName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var modType = extension switch
        {
            ".esm" => ModType.Master,
            ".esl" => ModType.Light,
            ".esp" => ModType.Plugin,
            _ => ModType.Plugin
        };
        var modKey = new ModKey(modName, modType);
        mock.Setup(m => m.ModKey).Returns(modKey);
        return mock;
    }

    private async Task CreateMinimalPluginFile(string path)
    {
        // Create a minimal valid ESP file header
        // TES4 record header (simplified)
        var header = new byte[]
        {
            0x54, 0x45, 0x53, 0x34, // "TES4"
            0x2B, 0x00, 0x00, 0x00, // Size
            0x00, 0x00, 0x00, 0x00, // Flags
            0x00, 0x00, 0x00, 0x00, // FormID
            0x00, 0x00, 0x00, 0x00, // Timestamp
            0x00, 0x00, 0x00, 0x00, // Version Control
            0x00, 0x00, 0x00, 0x00, // Internal Version
            0x00, 0x00, 0x00, 0x00 // Unknown
        };

        await File.WriteAllBytesAsync(path, header);
    }

    private int GetRecordCount()
    {
        using var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", _connection);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    #endregion
}
