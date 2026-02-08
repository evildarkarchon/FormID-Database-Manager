#nullable enable

using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.ViewModels;

public class MainWindowViewModelTests
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindowViewModelTests()
    {
        _viewModel = new MainWindowViewModel();
    }

    #region Property Change Notification Tests

    [AvaloniaFact]
    public void GameDirectory_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (sender, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.GameDirectory = @"C:\Games\Skyrim";

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.GameDirectory), propertyName);
        Assert.Equal(@"C:\Games\Skyrim", _viewModel.GameDirectory);
    }

    [AvaloniaFact]
    public void DatabasePath_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (sender, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.DatabasePath = @"C:\Database\formids.db";

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.DatabasePath), propertyName);
        Assert.Equal(@"C:\Database\formids.db", _viewModel.DatabasePath);
    }

    [AvaloniaFact]
    public void FormIdListPath_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (sender, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.FormIdListPath = @"C:\Lists\formids.txt";

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.FormIdListPath), propertyName);
        Assert.Equal(@"C:\Lists\formids.txt", _viewModel.FormIdListPath);
    }

    [AvaloniaFact]
    public void DetectedGame_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (sender, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.DetectedGame = "Skyrim SE";

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.DetectedGame), propertyName);
        Assert.Equal("Skyrim SE", _viewModel.DetectedGame);
    }

    [AvaloniaFact]
    public void IsProcessing_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (sender, args) =>
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

    [AvaloniaFact]
    public void ProgressValue_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (sender, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.ProgressValue = 75.5;

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.ProgressValue), propertyName);
        Assert.Equal(75.5, _viewModel.ProgressValue);
    }

    [AvaloniaFact]
    public void ProgressStatus_RaisesPropertyChanged_WhenSet()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (sender, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.ProgressStatus = "Processing plugins...";

        // Assert
        Assert.Equal(nameof(MainWindowViewModel.ProgressStatus), propertyName);
        Assert.Equal("Processing plugins...", _viewModel.ProgressStatus);
    }

    [AvaloniaFact]
    public void Properties_DoNotRaisePropertyChanged_WhenSetToSameValue()
    {
        // Arrange
        _viewModel.GameDirectory = "TestPath";
        var eventRaised = false;
        _viewModel.PropertyChanged += (sender, args) => eventRaised = true;

        // Act
        _viewModel.GameDirectory = "TestPath"; // Same value

        // Assert
        Assert.False(eventRaised);
    }

    #endregion

    #region Filtering Tests

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    #endregion

    #region Message Management Tests

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact(Skip = "Dispatcher synchronization issues in headless mode")]
    public async Task AddMessages_WorksFromBackgroundThread()
    {
        // Arrange
        var errorAdded = false;
        var infoAdded = false;

        // Act - Add messages from background thread
        await Task.Run(() =>
        {
            _viewModel.AddErrorMessage("Background error");
            errorAdded = true;
            _viewModel.AddInformationMessage("Background info");
            infoAdded = true;
        });

        // Wait for dispatcher to process
        await Task.Delay(100);

        // Assert
        Assert.True(errorAdded);
        Assert.True(infoAdded);
        Assert.Contains("Background error", _viewModel.ErrorMessages);
        Assert.Contains("Background info", _viewModel.InformationMessages);
    }

    #endregion

    #region Progress Management Tests

    [AvaloniaFact]
    public void ResetProgress_ClearsAllProgressState()
    {
        // Arrange
        _viewModel.ProgressValue = 50;
        _viewModel.ProgressStatus = "Processing...";
        _viewModel.IsProcessing = true;
        _viewModel.ErrorMessages.Add("Error");
        _viewModel.InformationMessages.Add("Info");

        // Act
        _viewModel.ResetProgress();

        // Assert
        Assert.Equal(0, _viewModel.ProgressValue);
        Assert.Equal(string.Empty, _viewModel.ProgressStatus);
        Assert.False(_viewModel.IsProcessing);
        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_viewModel.InformationMessages);
    }

    [AvaloniaFact]
    public void UpdateProgress_UpdatesStatusAndValue()
    {
        // Act
        _viewModel.UpdateProgress("Processing item 5/10", 50);

        // Assert
        Assert.Equal("Processing item 5/10", _viewModel.ProgressStatus);
        Assert.Equal(50, _viewModel.ProgressValue);
    }

    [AvaloniaFact]
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

    [AvaloniaFact(Skip = "Dispatcher synchronization issues in headless mode")]
    public async Task UpdateProgress_WorksFromBackgroundThread()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>();
        string? capturedStatus = null;
        double? capturedValue = null;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.ProgressStatus))
            {
                capturedStatus = _viewModel.ProgressStatus;
                capturedValue = _viewModel.ProgressValue;
                tcs.TrySetResult(true);
            }
        };

        // Act
        await Task.Run(() =>
        {
            _viewModel.UpdateProgress("Background update", 25);
        });

        // Wait for property changed event with timeout
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(5000));

        // Assert
        Assert.Same(tcs.Task, completedTask); // Ensure we didn't timeout
        Assert.Equal("Background update", capturedStatus);
        Assert.Equal(25, capturedValue);
    }

    [AvaloniaFact(Skip = "Dispatcher synchronization issues in headless mode")]
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
        });

        // Wait for dispatcher
        await Task.Delay(100);

        // Assert
        Assert.True(reset);
        Assert.Equal(0, _viewModel.ProgressValue);
        Assert.False(_viewModel.IsProcessing);
    }

    #endregion

    #region Collection Management Tests

    [AvaloniaFact]
    public void Plugins_InitializesAsEmptyCollection()
    {
        // Assert
        Assert.NotNull(_viewModel.Plugins);
        Assert.Empty(_viewModel.Plugins);
    }

    [AvaloniaFact]
    public void FilteredPlugins_InitializesAsEmptyCollection()
    {
        // Assert
        Assert.NotNull(_viewModel.FilteredPlugins);
        Assert.Empty(_viewModel.FilteredPlugins);
    }

    [AvaloniaFact]
    public void ErrorMessages_InitializesAsEmptyCollection()
    {
        // Assert
        Assert.NotNull(_viewModel.ErrorMessages);
        Assert.Empty(_viewModel.ErrorMessages);
    }

    [AvaloniaFact]
    public void InformationMessages_InitializesAsEmptyCollection()
    {
        // Assert
        Assert.NotNull(_viewModel.InformationMessages);
        Assert.Empty(_viewModel.InformationMessages);
    }

    [AvaloniaFact]
    public void Collections_CanBeModified()
    {
        // Act
        _viewModel.Plugins.Add(new PluginListItem { Name = "Test.esp" });
        _viewModel.ErrorMessages.Add("Error");
        _viewModel.InformationMessages.Add("Info");

        // Assert
        Assert.Single(_viewModel.Plugins);
        Assert.Single(_viewModel.ErrorMessages);
        Assert.Single(_viewModel.InformationMessages);
    }

    #endregion

    #region Debounce Tests

    [Fact]
    public async Task PluginFilter_WithDebounce_DelaysFilterApplication()
    {
        // Arrange - Use SynchronousThreadDispatcher to avoid Avalonia headless issues
        var dispatcher = new FormID_Database_Manager.TestUtilities.Mocks.SynchronousThreadDispatcher();
        var debouncedVm = new MainWindowViewModel(dispatcher, 200);
        debouncedVm.Plugins.Add(new PluginListItem { Name = "Plugin1.esp" });
        debouncedVm.Plugins.Add(new PluginListItem { Name = "TestMod.esp" });

        // Act - Set filter (should not apply immediately due to debounce)
        debouncedVm.PluginFilter = "Plugin";

        // Assert - Immediately after setting, all plugins still visible (debounce not yet fired)
        Assert.Equal(2, debouncedVm.FilteredPlugins.Count);

        // After debounce period, filter should be applied
        await Task.Delay(350);
        Assert.Single(debouncedVm.FilteredPlugins);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void SuspendFilter_PreventsApplyFilterDuringBulkAdd()
    {
        // Arrange
        var filterCallCount = 0;
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.FilteredPlugins))
            {
                filterCallCount++;
            }
        };

        // Act - Suspend, add many plugins, resume
        _viewModel.SuspendFilter();
        for (var i = 0; i < 100; i++)
        {
            _viewModel.Plugins.Add(new PluginListItem { Name = $"Plugin{i}.esp" });
        }
        _viewModel.ResumeFilter();

        // Assert - All plugins should be in filtered list
        Assert.Equal(100, _viewModel.FilteredPlugins.Count);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void PropertyChanged_HandlesNullPropertyName()
    {
        // Arrange
        var eventRaised = false;
        _viewModel.PropertyChanged += (sender, args) =>
        {
            eventRaised = true;
            // Should not throw on null property name
        };

        // Act - Force PropertyChanged with reflection
        var method = typeof(MainWindowViewModel).GetMethod("OnPropertyChanged",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method?.Invoke(_viewModel, new object?[] { null });

        // Assert
        Assert.True(eventRaised);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void Messages_HandleRapidAdditions()
    {
        // Act - Rapidly add many messages
        const int messageCount = 100;
        for (var i = 0; i < messageCount; i++)
        {
            _viewModel.AddErrorMessage($"Error {i}", 50);
            _viewModel.AddInformationMessage($"Info {i}", 50);
        }

        // Assert
        Assert.Equal(50, _viewModel.ErrorMessages.Count);
        Assert.Equal(50, _viewModel.InformationMessages.Count);
        Assert.Equal("Error 50", _viewModel.ErrorMessages.First());
        Assert.Equal("Error 99", _viewModel.ErrorMessages.Last());
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void SetProperty_ReturnsFalse_WhenValueUnchanged()
    {
        // Arrange
        _viewModel.GameDirectory = "TestPath";

        // Act - Test that setting the same value doesn't raise PropertyChanged
        var eventRaised = false;
        _viewModel.PropertyChanged += (sender, args) => eventRaised = true;
        _viewModel.GameDirectory = "TestPath"; // Same value

        // Assert
        Assert.False(eventRaised);
        Assert.Equal("TestPath", _viewModel.GameDirectory);
    }

    #endregion

    #region IsProgressVisible and IsScanning Tests

    [AvaloniaFact]
    public void IsProgressVisible_True_WhenIsProcessingTrue()
    {
        // Arrange & Act
        _viewModel.IsProcessing = true;

        // Assert
        Assert.True(_viewModel.IsProgressVisible);
    }

    [AvaloniaFact]
    public void IsProgressVisible_True_WhenIsScanningTrue()
    {
        // Arrange & Act
        _viewModel.IsScanning = true;

        // Assert
        Assert.True(_viewModel.IsProgressVisible);
    }

    [AvaloniaFact]
    public void IsProgressVisible_False_WhenBothFalse()
    {
        // Arrange - defaults are false, but be explicit
        _viewModel.IsProcessing = false;
        _viewModel.IsScanning = false;

        // Assert
        Assert.False(_viewModel.IsProgressVisible);
    }

    [AvaloniaFact]
    public void IsScanning_PropertyChanged_NotifiesIsProgressVisible()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (sender, args) =>
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

    [AvaloniaFact]
    public void IsProcessing_PropertyChanged_NotifiesIsProgressVisible()
    {
        // Arrange
        var notifiedProperties = new System.Collections.Generic.List<string>();
        _viewModel.PropertyChanged += (sender, args) =>
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
}
