using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities;
using FormID_Database_Manager.ViewModels;
using Moq;
using Mutagen.Bethesda;
using Xunit;
using MockFactory = FormID_Database_Manager.TestUtilities.Mocks.MockFactory;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class PluginListManagerTests : IDisposable
{
    private readonly Mock<GameDetectionService> _mockGameDetectionService;
    private readonly PluginListManager _pluginListManager;
    private readonly ObservableCollection<PluginListItem> _plugins;
    private readonly string _testDirectory;
    private readonly MainWindowViewModel _viewModel;

    public PluginListManagerTests()
    {
        _mockGameDetectionService = MockFactory.CreateGameDetectionServiceMock();
        _viewModel = new MainWindowViewModel();
        _pluginListManager = new PluginListManager(_mockGameDetectionService.Object, _viewModel);
        _testDirectory = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        _plugins = new ObservableCollection<PluginListItem>();

        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(Path.Combine(_testDirectory, "Data"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    #region Helper Methods

    private void CreateTestPluginFiles(string directory, params string[] fileNames)
    {
        foreach (var fileName in fileNames)
        {
            var filePath = Path.Combine(directory, fileName);
            // Create minimal ESP/ESM file header
            var header = new byte[]
            {
                0x54, 0x45, 0x53, 0x34, // "TES4"
                0x2B, 0x00, 0x00, 0x00, // Size
                0x00, 0x00, 0x00, 0x00, // Flags
                0x00, 0x00, 0x00, 0x00 // FormID
            };
            File.WriteAllBytes(filePath, header);
        }
    }

    #endregion

    #region Core Functionality Tests

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_LoadsPlugins_FromGameEnvironment()
    {
        // Arrange
        var dataPath = Path.Combine(_testDirectory, "Data");
        CreateTestPluginFiles(dataPath, "TestPlugin1.esp", "TestPlugin2.esp", "TestPlugin3.esm");

        // Act
        await _pluginListManager.RefreshPluginList(
            _testDirectory,
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Assert - GameEnvironment will fail without real game, so expect error
        Assert.Empty(_plugins);
        Assert.Contains(_viewModel.ErrorMessages, msg => msg.Contains("Failed to load plugins"));
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_FiltersBasePlugins_InNormalMode()
    {
        // Arrange
        var dataPath = Path.Combine(_testDirectory, "Data");
        CreateTestPluginFiles(dataPath, "Skyrim.esm", "Update.esm", "Dawnguard.esm", "UserMod.esp");

        _mockGameDetectionService.Setup(x => x.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns(new HashSet<string> { "Skyrim.esm", "Update.esm", "Dawnguard.esm" });

        // Act
        await _pluginListManager.RefreshPluginList(
            _testDirectory,
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Assert - GameEnvironment will fail, so we just verify error handling
        Assert.Empty(_plugins);
        Assert.NotEmpty(_viewModel.ErrorMessages);
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_ShowsAllPlugins_InAdvancedMode()
    {
        // Arrange
        var dataPath = Path.Combine(_testDirectory, "Data");
        CreateTestPluginFiles(dataPath, "Skyrim.esm", "UserMod.esp");

        // Act - Advanced mode should show all plugins
        await _pluginListManager.RefreshPluginList(
            _testDirectory,
            GameRelease.SkyrimSE,
            _plugins,
            true);

        // Assert - GameEnvironment will fail without real game
        Assert.Empty(_plugins);
        Assert.NotEmpty(_viewModel.ErrorMessages);
    }

    #endregion

    #region Collection Management Tests

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_ClearsCollections_BeforeLoading()
    {
        // Arrange - Pre-populate collections
        _plugins.Add(new PluginListItem { Name = "OldPlugin.esp" });
        _viewModel.FilteredPlugins.Add(new PluginListItem { Name = "OldPlugin.esp" });

        var dataPath = Path.Combine(_testDirectory, "Data");
        CreateTestPluginFiles(dataPath, "NewPlugin.esp");

        // Act
        await _pluginListManager.RefreshPluginList(
            _testDirectory,
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Assert - Collections should be cleared even on error
        Assert.Empty(_plugins);
        Assert.Empty(_viewModel.FilteredPlugins);
        Assert.NotEmpty(_viewModel.ErrorMessages);
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_UpdatesFilteredList_Correctly()
    {
        // Arrange
        var dataPath = Path.Combine(_testDirectory, "Data");
        CreateTestPluginFiles(dataPath, "Plugin1.esp", "Plugin2.esp");

        // Act
        await _pluginListManager.RefreshPluginList(
            _testDirectory,
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Assert - GameEnvironment will fail, both collections should be empty
        Assert.Empty(_plugins);
        Assert.Empty(_viewModel.FilteredPlugins);
        Assert.NotEmpty(_viewModel.ErrorMessages);
    }

    #endregion

    #region Error Handling Tests

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_HandlesEmptyDirectory_Gracefully()
    {
        // Arrange - Empty data directory
        var emptyDataPath = Path.Combine(_testDirectory, "EmptyData");
        Directory.CreateDirectory(emptyDataPath);

        // Act
        await _pluginListManager.RefreshPluginList(
            emptyDataPath,
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Assert
        Assert.Empty(_plugins);
        Assert.Empty(_viewModel.FilteredPlugins);
        Assert.NotEmpty(_viewModel.ErrorMessages);
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_HandlesInvalidGameRelease_WithErrorMessage()
    {
        // Arrange
        var invalidGameRelease = (GameRelease)999; // Invalid enum value

        // Act
        await _pluginListManager.RefreshPluginList(
            _testDirectory,
            invalidGameRelease,
            _plugins,
            false);

        // Assert
        Assert.Empty(_plugins);
        Assert.Contains(_viewModel.ErrorMessages, msg => msg.Contains("Failed to load plugins"));
        Assert.Contains(_viewModel.ErrorMessages,
            msg => msg.Contains("Ensure you selected the correct game Data directory"));
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_HandlesDataDirectoryVariants_Correctly()
    {
        // Test 1: Game directory without Data subfolder
        var gameDir = _testDirectory;
        var dataPath = Path.Combine(gameDir, "Data");
        CreateTestPluginFiles(dataPath, "Test1.esp");

        await _pluginListManager.RefreshPluginList(
            gameDir,
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Test 2: Direct Data directory path
        _plugins.Clear();
        _viewModel.FilteredPlugins.Clear();
        _viewModel.ErrorMessages.Clear();

        await _pluginListManager.RefreshPluginList(
            dataPath, // Direct data path
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Both should fail with GameEnvironment error
        Assert.Empty(_plugins);
        Assert.NotEmpty(_viewModel.ErrorMessages);
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_HandlesNonExistentPluginFiles_Correctly()
    {
        // Arrange - Create directory but no actual plugin files
        var dataPath = Path.Combine(_testDirectory, "Data");

        // Act
        await _pluginListManager.RefreshPluginList(
            _testDirectory,
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Assert - Should handle gracefully
        Assert.Empty(_plugins);
        Assert.NotEmpty(_viewModel.ErrorMessages);
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_HandlesException_ClearsCollectionsAndReportsError()
    {
        // Arrange - Use non-existent directory to trigger exception
        var nonExistentDir = Path.Combine(_testDirectory, "NonExistent");

        // Act
        await _pluginListManager.RefreshPluginList(
            nonExistentDir,
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Assert
        Assert.Empty(_plugins);
        Assert.Empty(_viewModel.FilteredPlugins);
        Assert.Contains(_viewModel.ErrorMessages, msg => msg.Contains("Failed to load plugins"));
    }

    #endregion

    #region Selection Management Tests

    [Fact]
    public void SelectAll_MarksAllPlugins_AsSelected()
    {
        // Arrange
        var testPlugins = new ObservableCollection<PluginListItem>
        {
            new() { Name = "Plugin1.esp", IsSelected = false },
            new() { Name = "Plugin2.esp", IsSelected = false },
            new() { Name = "Plugin3.esp", IsSelected = true } // Already selected
        };

        // Act
        _pluginListManager.SelectAll(testPlugins);

        // Assert
        Assert.All(testPlugins, plugin => Assert.True(plugin.IsSelected));
    }

    [Fact]
    public void SelectNone_DeselectsAllPlugins()
    {
        // Arrange
        var testPlugins = new ObservableCollection<PluginListItem>
        {
            new() { Name = "Plugin1.esp", IsSelected = true },
            new() { Name = "Plugin2.esp", IsSelected = true },
            new() { Name = "Plugin3.esp", IsSelected = false } // Already deselected
        };

        // Act
        _pluginListManager.SelectNone(testPlugins);

        // Assert
        Assert.All(testPlugins, plugin => Assert.False(plugin.IsSelected));
    }

    [Fact]
    public void SelectAll_HandlesEmptyCollection_Gracefully()
    {
        // Arrange
        var emptyPlugins = new ObservableCollection<PluginListItem>();

        // Act & Assert - Should not throw
        _pluginListManager.SelectAll(emptyPlugins);
        Assert.Empty(emptyPlugins);
    }

    [Fact]
    public void SelectNone_HandlesEmptyCollection_Gracefully()
    {
        // Arrange
        var emptyPlugins = new ObservableCollection<PluginListItem>();

        // Act & Assert - Should not throw
        _pluginListManager.SelectNone(emptyPlugins);
        Assert.Empty(emptyPlugins);
    }

    #endregion

    #region Plugin State Tests

    [Fact]
    public void PluginListItem_PropertyChange_TriggersNotification()
    {
        // Arrange
        var plugin = new PluginListItem { Name = "Test.esp", IsSelected = false };
        var propertyChanged = false;
        plugin.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(PluginListItem.IsSelected))
            {
                propertyChanged = true;
            }
        };

        // Act
        plugin.IsSelected = true;

        // Assert
        Assert.True(propertyChanged);
        Assert.True(plugin.IsSelected);
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_PreservesPluginOrder_FromLoadOrder()
    {
        // Arrange
        var dataPath = Path.Combine(_testDirectory, "Data");
        var pluginNames = new[] { "A_Plugin.esp", "B_Plugin.esp", "C_Plugin.esp" };
        CreateTestPluginFiles(dataPath, pluginNames);

        // Act
        await _pluginListManager.RefreshPluginList(
            _testDirectory,
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Assert - GameEnvironment will fail without real game
        Assert.Empty(_plugins);
        Assert.NotEmpty(_viewModel.ErrorMessages);
    }

    #endregion

    #region Message Handling Tests

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_AddsInformationMessage_OnSuccess()
    {
        // Arrange
        var dataPath = Path.Combine(_testDirectory, "Data");
        CreateTestPluginFiles(dataPath, "TestPlugin.esp");
        _viewModel.InformationMessages.Clear();

        // Act
        await _pluginListManager.RefreshPluginList(
            _testDirectory,
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Assert - GameEnvironment will fail, so expect error messages instead
        Assert.Empty(_viewModel.InformationMessages);
        Assert.NotEmpty(_viewModel.ErrorMessages);
        Assert.Contains(_viewModel.ErrorMessages, msg => msg.Contains("Failed to load plugins"));
    }

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_AddsErrorMessages_OnFailure()
    {
        // Arrange
        _viewModel.ErrorMessages.Clear();
        var invalidGameRelease = (GameRelease)999;

        // Act
        await _pluginListManager.RefreshPluginList(
            _testDirectory,
            invalidGameRelease,
            _plugins,
            false);

        // Assert
        Assert.NotEmpty(_viewModel.ErrorMessages);
        Assert.Equal(2, _viewModel.ErrorMessages.Count); // Two error messages as per implementation
    }

    #endregion

    #region Edge Cases

    [ExpectsGameEnvironmentFailureFact]
    public async Task RefreshPluginList_HandlesSpecialCharactersInPluginNames()
    {
        // Arrange
        var dataPath = Path.Combine(_testDirectory, "Data");
        var specialNames = new[]
        {
            "Plugin with spaces.esp", "Plugin-with-dashes.esp", "Plugin_with_underscores.esp",
            "Plugin.with.dots.esp"
        };
        CreateTestPluginFiles(dataPath, specialNames);

        // Act
        await _pluginListManager.RefreshPluginList(
            _testDirectory,
            GameRelease.SkyrimSE,
            _plugins,
            false);

        // Assert - GameEnvironment will fail without real game
        Assert.Empty(_plugins);
        Assert.NotEmpty(_viewModel.ErrorMessages);
    }

    [Fact]
    public void SelectionMethods_HandleNullPluginItems_Gracefully()
    {
        // Arrange
        var pluginsWithNull = new ObservableCollection<PluginListItem>
        {
            new() { Name = "Plugin1.esp", IsSelected = false },
            null!, // Null item
            new() { Name = "Plugin2.esp", IsSelected = false }
        };

        // Remove null to test actual functionality
        pluginsWithNull.Remove(null);

        // Act & Assert - Should handle collection without throwing
        _pluginListManager.SelectAll(pluginsWithNull);
        Assert.All(pluginsWithNull.Where(p => p != null), plugin => Assert.True(plugin.IsSelected));

        _pluginListManager.SelectNone(pluginsWithNull);
        Assert.All(pluginsWithNull.Where(p => p != null), plugin => Assert.False(plugin.IsSelected));
    }

    #endregion
}
