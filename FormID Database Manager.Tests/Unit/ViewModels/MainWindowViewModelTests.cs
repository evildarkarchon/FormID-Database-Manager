#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.ViewModels;

public class MainWindowViewModelTests
{
    private readonly MainWindowViewModel _viewModel = new(new SynchronousThreadDispatcher());

    #region Property Change Notification Tests

    [Fact]
    public void GameDirectory_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (_, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.GameDirectory = @"C:\Games\Skyrim";

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.GameDirectory), propertyName);
        Assert.Equal(@"C:\Games\Skyrim", _viewModel.GameDirectory);
    }

    [Fact]
    public void DatabasePath_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (_, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.DatabasePath = @"C:\Database\formids.db";

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.DatabasePath), propertyName);
        Assert.Equal(@"C:\Database\formids.db", _viewModel.DatabasePath);
    }

    [Fact]
    public void FormIdListPath_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (_, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.FormIdListPath = @"C:\Lists\formids.txt";

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.FormIdListPath), propertyName);
        Assert.Equal(@"C:\Lists\formids.txt", _viewModel.FormIdListPath);
    }

    [Fact]
    public void SelectedGame_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        _viewModel.SelectedGame = GameRelease.SkyrimSE;

        // Assert
        Assert.Contains(nameof(MainWindowViewModel.SelectedGame), notifiedProperties);
        Assert.Contains(nameof(MainWindowViewModel.IsGameSelected), notifiedProperties);
        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
    }

    [Fact]
    public void SelectedGame_IsNullByDefault()
    {
        // Assert
        Assert.Null(_viewModel.SelectedGame);
    }

    [Fact]
    public void IsGameSelected_ReturnsFalse_WhenSelectedGameIsNull()
    {
        // Assert
        Assert.False(_viewModel.IsGameSelected);
    }

    [Fact]
    public void IsGameSelected_ReturnsTrue_WhenSelectedGameHasValue()
    {
        // Arrange
        _viewModel.SelectedGame = GameRelease.Fallout4;

        // Assert
        Assert.True(_viewModel.IsGameSelected);
    }

    [Fact]
    public void AvailableGames_ContainsAll10SupportedReleases()
    {
        // Assert
        Assert.Equal(10, _viewModel.AvailableGames.Count);
        Assert.Contains(GameRelease.Fallout4, _viewModel.AvailableGames);
        Assert.Contains(GameRelease.SkyrimSE, _viewModel.AvailableGames);
        Assert.Contains(GameRelease.SkyrimLE, _viewModel.AvailableGames);
        Assert.Contains(GameRelease.SkyrimVR, _viewModel.AvailableGames);
        Assert.Contains(GameRelease.SkyrimSEGog, _viewModel.AvailableGames);
        Assert.Contains(GameRelease.EnderalSE, _viewModel.AvailableGames);
        Assert.Contains(GameRelease.EnderalLE, _viewModel.AvailableGames);
        Assert.Contains(GameRelease.Fallout4VR, _viewModel.AvailableGames);
        Assert.Contains(GameRelease.Oblivion, _viewModel.AvailableGames);
        Assert.Contains(GameRelease.Starfield, _viewModel.AvailableGames);
    }

    [Fact]
    public void AvailableGames_HasFallout4First()
    {
        // Assert
        Assert.Equal(GameRelease.Fallout4, _viewModel.AvailableGames[0]);
    }

    [Fact]
    public void DetectedDirectories_InitializesAsEmptyCollection()
    {
        // Assert
        Assert.NotNull(_viewModel.DetectedDirectories);
        Assert.Empty(_viewModel.DetectedDirectories);
    }

    [Fact]
    public void HasMultipleDirectories_ReturnsFalse_WhenEmpty()
    {
        // Assert
        Assert.False(_viewModel.HasMultipleDirectories);
    }

    [Fact]
    public void HasMultipleDirectories_ReturnsFalse_WhenSingleEntry()
    {
        // Arrange
        _viewModel.DetectedDirectories.Add(@"C:\Games\Fallout4");

        // Assert
        Assert.False(_viewModel.HasMultipleDirectories);
    }

    [Fact]
    public void HasMultipleDirectories_ReturnsTrue_WhenMultipleEntries()
    {
        // Arrange
        _viewModel.DetectedDirectories.Add(@"C:\Games\Fallout4");
        _viewModel.DetectedDirectories.Add(@"D:\Games\Fallout4");

        // Assert
        Assert.True(_viewModel.HasMultipleDirectories);
    }

    [Fact]
    public void HasMultipleDirectories_RaisesPropertyChanged_WhenDirectoriesChange()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        _viewModel.DetectedDirectories.Add(@"C:\Games\Fallout4");
        _viewModel.DetectedDirectories.Add(@"D:\Games\Fallout4");

        // Assert
        Assert.Contains(nameof(MainWindowViewModel.HasMultipleDirectories), notifiedProperties);
    }

    [Fact]
    public void IsProcessing_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        _viewModel.IsProcessing = true;

        // Assert
        Assert.Contains(nameof(MainWindowViewModel.IsProcessing), notifiedProperties);
        Assert.True(_viewModel.IsProcessing);
    }

    [Fact]
    public void ProgressValue_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (_, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.ProgressValue = 75.5;

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.ProgressValue), propertyName);
        Assert.Equal(75.5, _viewModel.ProgressValue);
    }

    [Fact]
    public void ProgressStatus_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (_, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.ProgressStatus = "Processing plugins...";

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.ProgressStatus), propertyName);
        Assert.Equal("Processing plugins...", _viewModel.ProgressStatus);
    }

    [Fact]
    public void AdvancedMode_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (_, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.AdvancedMode = true;

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.AdvancedMode), propertyName);
        Assert.True(_viewModel.AdvancedMode);
    }

    [Fact]
    public void UpdateMode_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (_, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.UpdateMode = true;

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.UpdateMode), propertyName);
        Assert.True(_viewModel.UpdateMode);
    }

    [Fact]
    public void ProcessButtonText_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (_, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.ProcessButtonText = "Cancel Processing";

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.ProcessButtonText), propertyName);
        Assert.Equal("Cancel Processing", _viewModel.ProcessButtonText);
    }

    [Fact]
    public void Properties_DoNotRaisePropertyChanged_WhenSetToSameValue()
    {
        // Arrange
        _viewModel.GameDirectory = "TestPath";
        var eventRaised = false;
        _viewModel.PropertyChanged += (_, _) => eventRaised = true;

        // Act
        _viewModel.GameDirectory = "TestPath"; // Same value

        // Assert
        Assert.False(eventRaised);
    }

    #endregion

    #region Filtering Tests

    [Fact]
    public void PluginFilter_UpdatesFilteredPlugins_WhenChanged()
    {
        // Arrange
        _viewModel.Plugins.Add(new PluginListItem { Name = "Plugin1.esp" });
        _viewModel.Plugins.Add(new PluginListItem { Name = "Plugin2.esp" });
        _viewModel.Plugins.Add(new PluginListItem { Name = "TestMod.esp" });

        // Act
        _viewModel.PluginFilter = "Plugin";

        // Assert
        Assert.Equal(2, _viewModel.FilteredPlugins.Count);
        Assert.All(_viewModel.FilteredPlugins, p => Assert.Contains("Plugin", p.Name));
    }

    [Fact]
    public void ApplyFilter_FiltersCaseInsensitive()
    {
        // Arrange
        _viewModel.Plugins.Add(new PluginListItem { Name = "PLUGIN.esp" });
        _viewModel.Plugins.Add(new PluginListItem { Name = "plugin.esp" });
        _viewModel.Plugins.Add(new PluginListItem { Name = "PlUgIn.esp" });

        // Act
        _viewModel.PluginFilter = "plugin";

        // Assert
        Assert.Equal(3, _viewModel.FilteredPlugins.Count);
    }

    [Fact]
    public void ApplyFilter_ShowsAllPlugins_WhenFilterIsEmpty()
    {
        // Arrange
        _viewModel.Plugins.Add(new PluginListItem { Name = "Plugin1.esp" });
        _viewModel.Plugins.Add(new PluginListItem { Name = "Plugin2.esp" });
        _viewModel.PluginFilter = "Test";

        // Act
        _viewModel.PluginFilter = "";

        // Assert
        Assert.Equal(_viewModel.Plugins.Count, _viewModel.FilteredPlugins.Count);
    }

    [Fact]
    public void ApplyFilter_ShowsAllPlugins_WhenFilterIsWhitespace()
    {
        // Arrange
        _viewModel.Plugins.Add(new PluginListItem { Name = "Plugin1.esp" });
        _viewModel.Plugins.Add(new PluginListItem { Name = "Plugin2.esp" });

        // Act
        _viewModel.PluginFilter = "   ";

        // Assert
        Assert.Equal(_viewModel.Plugins.Count, _viewModel.FilteredPlugins.Count);
    }

    [Fact]
    public void Plugins_TriggersFilterUpdate_WhenSet()
    {
        // Arrange
        var newPlugins = new ObservableCollection<PluginListItem>
        {
            new() { Name = "NewPlugin1.esp" }, new() { Name = "NewPlugin2.esp" }
        };

        // Act
        _viewModel.Plugins = newPlugins;

        // Assert
        Assert.Equal(2, _viewModel.FilteredPlugins.Count);
        Assert.Equal(newPlugins.Count, _viewModel.FilteredPlugins.Count);
    }

    [Fact]
    public void GetSelectedPlugins_ReturnsOnlySelectedPluginSnapshot()
    {
        // Arrange
        var selectedPlugin = new PluginListItem { Name = "Selected.esp", IsSelected = true };
        var unselectedPlugin = new PluginListItem { Name = "Unselected.esp", IsSelected = false };
        _viewModel.Plugins.Add(selectedPlugin);
        _viewModel.Plugins.Add(unselectedPlugin);

        // Act
        var selectedPlugins = _viewModel.GetSelectedPlugins();
        selectedPlugin.IsSelected = false;

        // Assert
        Assert.Single(selectedPlugins);
        Assert.Same(selectedPlugin, selectedPlugins[0]);
    }

    #endregion

    #region Plugin Item Validation Tests

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void PluginListItem_NameValidation_ReturnsErrorForEmptyOrWhitespaceNames(string pluginName)
    {
        // Arrange
        var plugin = new PluginListItem { Name = pluginName };
        var validation = Assert.IsAssignableFrom<IDataErrorInfo>(plugin);

        // Act
        var error = validation[nameof(PluginListItem.Name)];

        // Assert
        Assert.Equal("Name cannot be empty", error);
    }

    [Fact]
    public void PluginListItem_ValidNameHasNoValidationErrorAndRemainsSelectable()
    {
        // Arrange
        var plugin = new PluginListItem { Name = "ValidPlugin.esp" };
        var validation = Assert.IsAssignableFrom<IDataErrorInfo>(plugin);

        // Act
        plugin.IsSelected = true;

        // Assert
        Assert.Equal(string.Empty, validation[nameof(PluginListItem.Name)]);
        Assert.Equal("ValidPlugin.esp", plugin.Name);
        Assert.True(plugin.IsSelected);
    }

    #endregion

    #region Message Management Tests

    [Fact]
    public void AddErrorMessage_AddsToCollection()
    {
        // Arrange
        var message = "Test error message";

        // Act
        _viewModel.AddErrorMessage(message);

        // Assert
        Assert.Single(_viewModel.ErrorMessages);
        Assert.Contains(message, _viewModel.ErrorMessages);
    }

    [Fact]
    public void AddErrorMessage_MaintainsMaxMessages()
    {
        // Arrange
        const int maxMessages = 5;

        // Act
        for (var i = 0; i < 10; i++)
        {
            _viewModel.AddErrorMessage($"Error {i}", maxMessages);
        }

        // Assert
        Assert.Equal(maxMessages, _viewModel.ErrorMessages.Count);
        Assert.Equal("Error 5", _viewModel.ErrorMessages.First());
        Assert.Equal("Error 9", _viewModel.ErrorMessages.Last());
    }

    [Fact]
    public void AddErrorMessage_UsesDefaultMaxMessages()
    {
        // Act
        for (var i = 0; i < 12; i++)
        {
            _viewModel.AddErrorMessage($"Error {i}");
        }

        // Assert
        Assert.Equal(10, _viewModel.ErrorMessages.Count);
        Assert.Equal("Error 2", _viewModel.ErrorMessages.First());
        Assert.Equal("Error 11", _viewModel.ErrorMessages.Last());
    }

    [Fact]
    public void AddInformationMessage_AddsToCollection()
    {
        // Arrange
        var message = "Test information message";

        // Act
        _viewModel.AddInformationMessage(message);

        // Assert
        Assert.Single(_viewModel.InformationMessages);
        Assert.Contains(message, _viewModel.InformationMessages);
    }

    [Fact]
    public void AddInformationMessage_MaintainsMaxMessages()
    {
        // Arrange
        const int maxMessages = 5;

        // Act
        for (var i = 0; i < 10; i++)
        {
            _viewModel.AddInformationMessage($"Info {i}", maxMessages);
        }

        // Assert
        Assert.Equal(maxMessages, _viewModel.InformationMessages.Count);
        Assert.Equal("Info 5", _viewModel.InformationMessages.First());
        Assert.Equal("Info 9", _viewModel.InformationMessages.Last());
    }

    [Fact]
    public void AddInformationMessage_UsesDefaultMaxMessages()
    {
        // Act
        for (var i = 0; i < 12; i++)
        {
            _viewModel.AddInformationMessage($"Info {i}");
        }

        // Assert
        Assert.Equal(10, _viewModel.InformationMessages.Count);
        Assert.Equal("Info 2", _viewModel.InformationMessages.First());
        Assert.Equal("Info 11", _viewModel.InformationMessages.Last());
    }

    [Fact]
    public void AddWarningMessage_AddsToCollection()
    {
        // Arrange
        var message = "Test warning message";

        // Act
        _viewModel.AddWarningMessage(message);

        // Assert
        Assert.Single(_viewModel.WarningMessages);
        Assert.Contains(message, _viewModel.WarningMessages);
    }

    [Fact]
    public void AddWarningMessage_MaintainsMaxMessages()
    {
        // Arrange
        const int maxMessages = 5;

        // Act
        for (var i = 0; i < 10; i++)
        {
            _viewModel.AddWarningMessage($"Warning {i}", maxMessages);
        }

        // Assert
        Assert.Equal(maxMessages, _viewModel.WarningMessages.Count);
        Assert.Equal("Warning 5", _viewModel.WarningMessages.First());
        Assert.Equal("Warning 9", _viewModel.WarningMessages.Last());
    }

    [Fact]
    public async Task AddMessages_WorksFromBackgroundThread()
    {
        // Arrange
        var errorAdded = false;
        var infoAdded = false;
        var warningAdded = false;

        // Act - Add messages from background thread
        await Task.Run(() =>
        {
            _viewModel.AddErrorMessage("Background error");
            errorAdded = true;
            _viewModel.AddInformationMessage("Background info");
            infoAdded = true;
            _viewModel.AddWarningMessage("Background warning");
            warningAdded = true;
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(errorAdded);
        Assert.True(infoAdded);
        Assert.True(warningAdded);
        Assert.Contains("Background error", _viewModel.ErrorMessages);
        Assert.Contains("Background info", _viewModel.InformationMessages);
        Assert.Contains("Background warning", _viewModel.WarningMessages);
    }

    #endregion

    #region Progress Management Tests

    [Fact]
    public void ResetProgress_ClearsAllProgressState()
    {
        // Arrange
        _viewModel.ProgressValue = 50;
        _viewModel.ProgressStatus = "Processing...";
        _viewModel.IsProcessing = true;
        _viewModel.ErrorMessages.Add("Error");
        _viewModel.InformationMessages.Add("Info");
        _viewModel.WarningMessages.Add("Warning");

        // Act
        _viewModel.ResetProgress();

        // Assert
        Assert.Equal(0, _viewModel.ProgressValue);
        Assert.Equal(string.Empty, _viewModel.ProgressStatus);
        Assert.False(_viewModel.IsProcessing);
        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_viewModel.InformationMessages);
        Assert.Empty(_viewModel.WarningMessages);
    }

    [Fact]
    public void UpdateProgress_UpdatesStatusAndValue()
    {
        // Act
        _viewModel.UpdateProgress("Processing item 5/10", 50);

        // Assert
        Assert.Equal("Processing item 5/10", _viewModel.ProgressStatus);
        Assert.Equal(50, _viewModel.ProgressValue);
    }

    [Fact]
    public void UpdateProgress_UpdatesOnlyStatus_WhenValueIsNull()
    {
        // Arrange
        _viewModel.ProgressValue = 75;

        // Act
        _viewModel.UpdateProgress("Still processing...");

        // Assert
        Assert.Equal("Still processing...", _viewModel.ProgressStatus);
        Assert.Equal(75, _viewModel.ProgressValue); // Unchanged
    }

    [Fact]
    public async Task UpdateProgress_WorksFromBackgroundThread()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>();

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.ProgressStatus) or
                nameof(MainWindowViewModel.ProgressValue))
            {
                if (_viewModel.ProgressStatus == "Background update" &&
                    Math.Abs(_viewModel.ProgressValue - 25) < 0.0001)
                {
                    tcs.TrySetResult(true);
                }
            }
        };

        // Act
        await Task.Run(() =>
        {
            _viewModel.UpdateProgress("Background update", 25);
        }, TestContext.Current.CancellationToken);

        // Wait for property changed event with timeout
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(5000, TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(tcs.Task, completedTask); // Ensure we didn't timeout
        Assert.Equal("Background update", _viewModel.ProgressStatus);
        Assert.Equal(25, _viewModel.ProgressValue);
    }

    [Fact]
    public async Task ResetProgress_WorksFromBackgroundThread()
    {
        // Arrange
        _viewModel.ProgressValue = 100;
        _viewModel.IsProcessing = true;
        var reset = false;

        // Act
        await Task.Run(() =>
        {
            _viewModel.ResetProgress();
            reset = true;
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(reset);
        Assert.Equal(0, _viewModel.ProgressValue);
        Assert.False(_viewModel.IsProcessing);
    }

    #endregion

    #region Collection Management Tests

    [Fact]
    public void Plugins_InitializesAsEmptyCollection()
    {
        // Assert
        Assert.NotNull(_viewModel.Plugins);
        Assert.Empty(_viewModel.Plugins);
    }

    [Fact]
    public void FilteredPlugins_InitializesAsEmptyCollection()
    {
        // Assert
        Assert.NotNull(_viewModel.FilteredPlugins);
        Assert.Empty(_viewModel.FilteredPlugins);
    }

    [Fact]
    public void ErrorMessages_InitializesAsEmptyCollection()
    {
        // Assert
        Assert.NotNull(_viewModel.ErrorMessages);
        Assert.Empty(_viewModel.ErrorMessages);
    }

    [Fact]
    public void InformationMessages_InitializesAsEmptyCollection()
    {
        // Assert
        Assert.NotNull(_viewModel.InformationMessages);
        Assert.Empty(_viewModel.InformationMessages);
    }

    [Fact]
    public void WarningMessages_InitializesAsEmptyCollection()
    {
        // Assert
        Assert.NotNull(_viewModel.WarningMessages);
        Assert.Empty(_viewModel.WarningMessages);
    }

    [Fact]
    public void MessageVisibilityProperties_ReturnFalseByDefault()
    {
        // Assert
        Assert.False(GetBooleanProperty(_viewModel, "HasErrorMessages"));
        Assert.False(GetBooleanProperty(_viewModel, "HasInformationMessages"));
        Assert.False(GetBooleanProperty(_viewModel, "HasWarningMessages"));
    }

    [Fact]
    public void AddErrorMessage_UpdatesHasErrorMessages()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        _viewModel.AddErrorMessage("Binding support error");

        // Assert
        Assert.True(GetBooleanProperty(_viewModel, "HasErrorMessages"));
        Assert.Contains("HasErrorMessages", notifiedProperties);
    }

    [Fact]
    public void AddInformationMessage_UpdatesHasInformationMessages()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        _viewModel.AddInformationMessage("Binding support information");

        // Assert
        Assert.True(GetBooleanProperty(_viewModel, "HasInformationMessages"));
        Assert.Contains("HasInformationMessages", notifiedProperties);
    }

    [Fact]
    public void AddWarningMessage_UpdatesHasWarningMessages()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        _viewModel.AddWarningMessage("Binding support warning");

        // Assert
        Assert.True(GetBooleanProperty(_viewModel, "HasWarningMessages"));
        Assert.Contains("HasWarningMessages", notifiedProperties);
    }

    [Fact]
    public void ErrorMessages_CollectionChangesNotifyHasErrorMessagesForAddAndClear()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        _viewModel.ErrorMessages.Add("Binding support error");

        // Assert
        Assert.True(_viewModel.HasErrorMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasErrorMessages), notifiedProperties);

        // Act
        notifiedProperties.Clear();
        _viewModel.ErrorMessages.Clear();

        // Assert
        Assert.False(_viewModel.HasErrorMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasErrorMessages), notifiedProperties);
    }

    [Fact]
    public void InformationMessages_CollectionChangesNotifyHasInformationMessagesForAddAndClear()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        _viewModel.InformationMessages.Add("Binding support information");

        // Assert
        Assert.True(_viewModel.HasInformationMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasInformationMessages), notifiedProperties);

        // Act
        notifiedProperties.Clear();
        _viewModel.InformationMessages.Clear();

        // Assert
        Assert.False(_viewModel.HasInformationMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasInformationMessages), notifiedProperties);
    }

    [Fact]
    public void WarningMessages_CollectionChangesNotifyHasWarningMessagesForAddAndClear()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        _viewModel.WarningMessages.Add("Binding support warning");

        // Assert
        Assert.True(_viewModel.HasWarningMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasWarningMessages), notifiedProperties);

        // Act
        notifiedProperties.Clear();
        _viewModel.WarningMessages.Clear();

        // Assert
        Assert.False(_viewModel.HasWarningMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasWarningMessages), notifiedProperties);
    }

    [Fact]
    public void ErrorMessages_ReplacedCollectionContinuesNotifyingHasErrorMessages()
    {
        // Arrange
        var replacement = new ObservableCollection<string>();
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.ErrorMessages = replacement;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        replacement.Add("Replacement error");

        // Assert
        Assert.True(_viewModel.HasErrorMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasErrorMessages), notifiedProperties);

        // Act
        notifiedProperties.Clear();
        replacement.Clear();

        // Assert
        Assert.False(_viewModel.HasErrorMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasErrorMessages), notifiedProperties);
    }

    [Fact]
    public void InformationMessages_ReplacedCollectionContinuesNotifyingHasInformationMessages()
    {
        // Arrange
        var replacement = new ObservableCollection<string>();
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.InformationMessages = replacement;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        replacement.Add("Replacement information");

        // Assert
        Assert.True(_viewModel.HasInformationMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasInformationMessages), notifiedProperties);

        // Act
        notifiedProperties.Clear();
        replacement.Clear();

        // Assert
        Assert.False(_viewModel.HasInformationMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasInformationMessages), notifiedProperties);
    }

    [Fact]
    public void WarningMessages_ReplacedCollectionContinuesNotifyingHasWarningMessages()
    {
        // Arrange
        var replacement = new ObservableCollection<string>();
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.WarningMessages = replacement;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        replacement.Add("Replacement warning");

        // Assert
        Assert.True(_viewModel.HasWarningMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasWarningMessages), notifiedProperties);

        // Act
        notifiedProperties.Clear();
        replacement.Clear();

        // Assert
        Assert.False(_viewModel.HasWarningMessages);
        Assert.Contains(nameof(MainWindowViewModel.HasWarningMessages), notifiedProperties);
    }

    [Fact]
    public void Collections_CanBeModified()
    {
        // Act
        _viewModel.Plugins.Add(new PluginListItem { Name = "Test.esp" });
        _viewModel.ErrorMessages.Add("Error");
        _viewModel.InformationMessages.Add("Info");
        _viewModel.WarningMessages.Add("Warning");

        // Assert
        Assert.Single(_viewModel.Plugins);
        Assert.Single(_viewModel.ErrorMessages);
        Assert.Single(_viewModel.InformationMessages);
        Assert.Single(_viewModel.WarningMessages);
    }

    #endregion

    #region Debounce Tests

    [Fact]
    public async Task PluginFilter_WithDebounce_DelaysFilterApplication()
    {
        // Arrange - Use SynchronousThreadDispatcher for deterministic debounce behavior
        var dispatcher = new SynchronousThreadDispatcher();
        var debouncedVm = new MainWindowViewModel(dispatcher, 200);
        debouncedVm.Plugins.Add(new PluginListItem { Name = "Plugin1.esp" });
        debouncedVm.Plugins.Add(new PluginListItem { Name = "TestMod.esp" });

        // Act - Set filter (should not apply immediately due to debounce)
        debouncedVm.PluginFilter = "Plugin";

        // Assert - Immediately after setting, all plugins still visible (debounce not yet fired)
        Assert.Equal(2, debouncedVm.FilteredPlugins.Count);

        // After debounce period, filter should be applied
        await Task.Delay(350, TestContext.Current.CancellationToken);
        Assert.Single(debouncedVm.FilteredPlugins);
    }

    [Fact]
    public async Task PluginFilter_WithDebounceAndUnavailableDispatcher_PostsBeforeUpdatingFilteredPlugins()
    {
        // Arrange
        var dispatcher = new RecordingThreadDispatcher(hasAccess: true);
        using var debouncedVm = new MainWindowViewModel(dispatcher, 50);
        debouncedVm.Plugins.Add(new PluginListItem { Name = "Plugin1.esp" });
        debouncedVm.Plugins.Add(new PluginListItem { Name = "TestMod.esp" });
        dispatcher.HasAccess = false;

        // Act
        debouncedVm.PluginFilter = "Plugin";
        await Task.Delay(150, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(dispatcher.PostCount > 0);
        Assert.Equal(2, debouncedVm.FilteredPlugins.Count);

        dispatcher.DrainPostedActions(hasAccessDuringDrain: true);

        Assert.Single(debouncedVm.FilteredPlugins);
        Assert.Equal("Plugin1.esp", debouncedVm.FilteredPlugins[0].Name);
    }

    [Fact]
    public async Task PluginFilter_WithDebounce_PreservesPluginInstancesAndSelectionAcrossHideShow()
    {
        // Arrange
        var dispatcher = new SynchronousThreadDispatcher();
        using var debouncedVm = new MainWindowViewModel(dispatcher, 25);
        var selectedPlugin = new PluginListItem { Name = "SelectedPlugin.esp", IsSelected = true };
        var otherPlugin = new PluginListItem { Name = "OtherPlugin.esp" };
        debouncedVm.Plugins.Add(selectedPlugin);
        debouncedVm.Plugins.Add(otherPlugin);

        // Act
        debouncedVm.PluginFilter = "Other";
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(selectedPlugin, debouncedVm.FilteredPlugins);
        Assert.Contains(otherPlugin, debouncedVm.FilteredPlugins);

        // Act
        debouncedVm.PluginFilter = "Selected";
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        var visiblePlugin = Assert.Single(debouncedVm.FilteredPlugins);
        Assert.Same(selectedPlugin, visiblePlugin);
        Assert.True(visiblePlugin.IsSelected);
    }

    [Fact]
    public void PluginFilter_WithZeroDebounce_AppliesImmediately()
    {
        // Arrange - Zero debounce (default for existing tests)
        var vm = new MainWindowViewModel(null, 0);
        vm.Plugins.Add(new PluginListItem { Name = "Plugin1.esp" });
        vm.Plugins.Add(new PluginListItem { Name = "TestMod.esp" });

        // Act
        vm.PluginFilter = "Plugin";

        // Assert - Filter should apply immediately with zero debounce
        Assert.Single(vm.FilteredPlugins);
    }

    #endregion

    #region Filter Suspension Tests

    [Fact]
    public void SuspendFilter_PreventsApplyFilterDuringBulkAdd()
    {
        // Arrange
        // Act - Suspend, add many plugins, resume
        _viewModel.SuspendFilter();
        for (var i = 0; i < 100; i++)
        {
            _viewModel.Plugins.Add(new PluginListItem { Name = $"Plugin{i}.esp" });
        }

        Assert.Empty(_viewModel.FilteredPlugins);

        _viewModel.ResumeFilter();

        // Assert - All plugins should be in filtered list
        Assert.Equal(100, _viewModel.FilteredPlugins.Count);
    }

    [Fact]
    public void ResumeFilter_AppliesCurrentFilter()
    {
        // Arrange
        _viewModel.PluginFilter = "Test";
        _viewModel.SuspendFilter();
        _viewModel.Plugins.Add(new PluginListItem { Name = "TestPlugin.esp" });
        _viewModel.Plugins.Add(new PluginListItem { Name = "OtherPlugin.esp" });

        // Act
        _viewModel.ResumeFilter();

        // Assert - Filter "Test" should be applied
        Assert.Single(_viewModel.FilteredPlugins);
        Assert.Equal("TestPlugin.esp", _viewModel.FilteredPlugins[0].Name);
    }

    #endregion

    #region Edge Cases and Stress Tests

    [Fact]
    public void PropertyChanged_HandlesNullPropertyName()
    {
        // Arrange
        var eventRaised = false;
        _viewModel.PropertyChanged += (_, _) =>
        {
            eventRaised = true;
            // Should not throw on null property name
        };

        // Act - Force PropertyChanged with reflection
        var method = typeof(MainWindowViewModel).GetMethod("OnPropertyChanged",
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            [typeof(string)],
            modifiers: null);
        method?.Invoke(_viewModel, [null]);

        // Assert
        Assert.True(eventRaised);
    }

    [Fact]
    public void ApplyFilter_HandlesLargePluginList()
    {
        // Arrange - Add 1000 plugins
        for (var i = 0; i < 1000; i++)
        {
            _viewModel.Plugins.Add(new PluginListItem { Name = $"Plugin{i}.esp" });
        }

        // Act
        _viewModel.PluginFilter = "Plugin99";

        // Assert
        Assert.Equal(11, _viewModel.FilteredPlugins.Count); // Plugin99, Plugin990-999
    }

    [Fact]
    public void Messages_HandleRapidAdditions()
    {
        // Act - Rapidly add many messages
        const int messageCount = 100;
        for (var i = 0; i < messageCount; i++)
        {
            _viewModel.AddErrorMessage($"Error {i}", 50);
            _viewModel.AddInformationMessage($"Info {i}", 50);
            _viewModel.AddWarningMessage($"Warning {i}", 50);
        }

        // Assert
        Assert.Equal(50, _viewModel.ErrorMessages.Count);
        Assert.Equal(50, _viewModel.InformationMessages.Count);
        Assert.Equal(50, _viewModel.WarningMessages.Count);
        Assert.Equal("Error 50", _viewModel.ErrorMessages.First());
        Assert.Equal("Error 99", _viewModel.ErrorMessages.Last());
        Assert.Equal("Warning 50", _viewModel.WarningMessages.First());
        Assert.Equal("Warning 99", _viewModel.WarningMessages.Last());
    }

    [Fact]
    public async Task AddErrorMessage_IsThreadSafeUnderConcurrentCalls()
    {
        // Arrange
        const int threadCount = 8;
        const int perThreadMessages = 200;
        const int defaultMaxMessages = 10;
        var errors = new ConcurrentQueue<Exception>();

        // Act
        var tasks = Enumerable.Range(0, threadCount)
            .Select(threadId => Task.Run(() =>
            {
                for (var i = 0; i < perThreadMessages; i++)
                {
                    try
                    {
                        _viewModel.AddErrorMessage($"T{threadId}:{i}");
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                    }
                }
            }, TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);
        // Assert
        Assert.Empty(errors);
        Assert.Equal(defaultMaxMessages, _viewModel.ErrorMessages.Count);
    }

    [Fact]
    public async Task Plugins_Add_IsThreadSafeUnderConcurrentCalls()
    {
        // Arrange
        const int threadCount = 8;
        const int perThreadPlugins = 100;
        var errors = new ConcurrentQueue<Exception>();

        // Act
        var tasks = Enumerable.Range(0, threadCount)
            .Select(threadId => Task.Run(() =>
            {
                for (var i = 0; i < perThreadPlugins; i++)
                {
                    try
                    {
                        _viewModel.Plugins.Add(new PluginListItem { Name = $"T{threadId}_{i}.esp" });
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                    }
                }
            }, TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);
        // Assert
        Assert.Empty(errors);
        Assert.Equal(threadCount * perThreadPlugins, _viewModel.Plugins.Count);
        Assert.Equal(_viewModel.Plugins.Count, _viewModel.FilteredPlugins.Count);
    }

    [Fact]
    public void Filter_HandlesSpecialCharacters()
    {
        // Arrange
        _viewModel.Plugins.Add(new PluginListItem { Name = "Plugin[Special].esp" });
        _viewModel.Plugins.Add(new PluginListItem { Name = "Plugin(Test).esp" });
        _viewModel.Plugins.Add(new PluginListItem { Name = "Plugin.Test.esp" });

        // Act
        _viewModel.PluginFilter = "[Special]";

        // Assert
        Assert.Single(_viewModel.FilteredPlugins);
        Assert.Equal("Plugin[Special].esp", _viewModel.FilteredPlugins.First().Name);
    }

    [Fact]
    public void SetProperty_ReturnsFalse_WhenValueUnchanged()
    {
        // Arrange
        _viewModel.GameDirectory = "TestPath";

        // Act - Test that setting the same value doesn't raise PropertyChanged
        var eventRaised = false;
        _viewModel.PropertyChanged += (_, _) => eventRaised = true;
        _viewModel.GameDirectory = "TestPath"; // Same value

        // Assert
        Assert.False(eventRaised);
        Assert.Equal("TestPath", _viewModel.GameDirectory);
    }

    #endregion

    #region IsProgressVisible and IsScanning Tests

    [Fact]
    public void IsProgressVisible_True_WhenIsProcessingTrue()
    {
        // Arrange & Act
        _viewModel.IsProcessing = true;

        // Assert
        Assert.True(_viewModel.IsProgressVisible);
    }

    [Fact]
    public void IsProgressVisible_True_WhenIsScanningTrue()
    {
        // Arrange & Act
        _viewModel.IsScanning = true;

        // Assert
        Assert.True(_viewModel.IsProgressVisible);
    }

    [Fact]
    public void IsProgressVisible_False_WhenBothFalse()
    {
        // Arrange - defaults are false, but be explicit
        _viewModel.IsProcessing = false;
        _viewModel.IsScanning = false;

        // Assert
        Assert.False(_viewModel.IsProgressVisible);
    }

    [Fact]
    public void IsScanning_PropertyChanged_NotifiesIsProgressVisible()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        _viewModel.IsScanning = true;

        // Assert
        Assert.Contains(nameof(MainWindowViewModel.IsProgressVisible), notifiedProperties);
    }

    [Fact]
    public void IsProcessing_PropertyChanged_NotifiesIsProgressVisible()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        // Act
        _viewModel.IsProcessing = true;

        // Assert
        Assert.Contains(nameof(MainWindowViewModel.IsProgressVisible), notifiedProperties);
    }

    #endregion


    private sealed class RecordingThreadDispatcher(bool hasAccess) : IThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _postedActions = new();
        private int _postCount;

        public bool HasAccess { get; set; } = hasAccess;

        public int PostCount => Volatile.Read(ref _postCount);

        public Task InvokeAsync(Action action)
        {
            if (CheckAccess())
            {
                action();
            }
            else
            {
                Post(action);
            }

            return Task.CompletedTask;
        }

        public void Post(Action action)
        {
            Interlocked.Increment(ref _postCount);
            _postedActions.Enqueue(action);
        }

        public bool CheckAccess()
        {
            return HasAccess;
        }

        public void DrainPostedActions(bool hasAccessDuringDrain)
        {
            var actionsToDrain = _postedActions.Count;
            var previousHasAccess = HasAccess;
            HasAccess = hasAccessDuringDrain;

            try
            {
                for (var i = 0; i < actionsToDrain && _postedActions.TryDequeue(out var action); i++)
                {
                    action();
                }
            }
            finally
            {
                HasAccess = previousHasAccess;
            }
        }
    }

    private static bool GetBooleanProperty(MainWindowViewModel viewModel, string propertyName)
    {
        var property = typeof(MainWindowViewModel).GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(typeof(bool), property.PropertyType);
        return (bool)property.GetValue(viewModel)!;
    }
}
