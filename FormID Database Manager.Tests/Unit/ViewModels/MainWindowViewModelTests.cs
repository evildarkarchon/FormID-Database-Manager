#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    /// <summary>
    /// Verifies that a changed projected directory raises its public presentation notification.
    /// </summary>
    [Fact]
    public void ApplyGameContextProjection_ChangedDirectory_RaisesPropertyChanged()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (_, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.ApplyGameContextProjection(
            GameRelease.SkyrimSE,
            @"C:\Games\Skyrim",
            [@"C:\Games\Skyrim"],
            AdvancedMode.Off);

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

    /// <summary>
    /// Verifies that a changed projected GameRelease raises release and derived-state notifications.
    /// </summary>
    [Fact]
    public void ApplyGameContextProjection_ChangedGameRelease_RaisesDependentNotifications()
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
        _viewModel.ApplyGameContextProjection(
            GameRelease.SkyrimSE,
            null,
            [],
            AdvancedMode.Off);

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
        _viewModel.ApplyGameContextProjection(
            GameRelease.Fallout4,
            null,
            [],
            AdvancedMode.Off);

        // Assert
        Assert.True(_viewModel.IsGameSelected);
    }

    /// <summary>
    /// Verifies that a complete Game Context projection is queued as one dispatcher action and observed coherently.
    /// </summary>
    [Fact]
    public void ApplyGameContextProjection_OffDispatcher_QueuesOneCoherentUpdate()
    {
        var dispatcher = new RecordingThreadDispatcher(hasAccess: true);
        var viewModel = new MainWindowViewModel(dispatcher);
        viewModel.ApplyGameContextProjection(
            GameRelease.Fallout4,
            @"C:\OldFallout",
            [@"C:\OldFallout"],
            AdvancedMode.Off);
        var observedSnapshots = new List<(GameRelease? Game, string Directory, string Directories, bool Mode)>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.SelectedGame)
                or nameof(MainWindowViewModel.GameDirectory)
                or nameof(MainWindowViewModel.DetectedDirectories)
                or nameof(MainWindowViewModel.AdvancedMode)
                or nameof(MainWindowViewModel.IsGameSelected)
                or nameof(MainWindowViewModel.HasMultipleDirectories))
            {
                observedSnapshots.Add((
                    viewModel.SelectedGame,
                    viewModel.GameDirectory,
                    string.Join('|', viewModel.DetectedDirectories),
                    viewModel.AdvancedMode));
            }
        };
        dispatcher.HasAccess = false;

        viewModel.ApplyGameContextProjection(
            GameRelease.SkyrimSE,
            null,
            [@"D:\Skyrim", @"C:\Skyrim"],
            AdvancedMode.On);

        Assert.Equal(1, dispatcher.PostCount);
        Assert.Equal(GameRelease.Fallout4, viewModel.SelectedGame);
        Assert.Equal(@"C:\OldFallout", viewModel.GameDirectory);
        Assert.Equal([@"C:\OldFallout"], viewModel.DetectedDirectories);
        Assert.False(viewModel.AdvancedMode);

        dispatcher.DrainPostedActions(hasAccessDuringDrain: true);

        Assert.Equal(GameRelease.SkyrimSE, viewModel.SelectedGame);
        Assert.Equal(string.Empty, viewModel.GameDirectory);
        Assert.Equal([@"D:\Skyrim", @"C:\Skyrim"], viewModel.DetectedDirectories);
        Assert.True(viewModel.AdvancedMode);
        Assert.True(viewModel.IsGameSelected);
        Assert.True(viewModel.HasMultipleDirectories);
        Assert.NotEmpty(observedSnapshots);
        Assert.All(observedSnapshots, snapshot => Assert.Equal(
            (GameRelease.SkyrimSE, string.Empty, @"D:\Skyrim|C:\Skyrim", true),
            snapshot));
    }

    /// <summary>
    ///     Verifies the game dropdown is exactly the Supported GameRelease table projected in table order. Which
    ///     releases appear, and in what order, is the table's responsibility now, and its own tests pin both.
    /// </summary>
    [Fact]
    public void AvailableGames_ProjectsTheSupportedGameReleaseTable()
    {
        Assert.Equal(
            SupportedGameReleases.All.Select(supported => supported.Release),
            _viewModel.AvailableGames);
    }

    [Fact]
    public void DetectedDirectories_InitializesAsEmptyCollection()
    {
        // Assert
        Assert.NotNull(_viewModel.DetectedDirectories);
        Assert.Empty(_viewModel.DetectedDirectories);
    }

    /// <summary>
    /// Verifies that available directories retain one ordered, read-only presentation collection across projections.
    /// </summary>
    [Fact]
    public void DetectedDirectories_CompleteProjections_PreserveStableReadOnlyOrderedMembership()
    {
        var property = typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.DetectedDirectories));
        var originalProjection = _viewModel.DetectedDirectories;

        _viewModel.ApplyGameContextProjection(
            GameRelease.SkyrimSE,
            @"D:\Skyrim",
            [@"D:\Skyrim", @"C:\Skyrim"],
            AdvancedMode.Off);

        Assert.NotNull(property);
        Assert.Equal(typeof(ReadOnlyObservableCollection<string>), property.PropertyType);
        Assert.Null(property.SetMethod);
        Assert.Same(originalProjection, _viewModel.DetectedDirectories);
        Assert.Equal([@"D:\Skyrim", @"C:\Skyrim"], _viewModel.DetectedDirectories);

        _viewModel.ApplyGameContextProjection(
            GameRelease.Fallout4,
            @"E:\Fallout4",
            [@"E:\Fallout4", @"B:\Fallout4"],
            AdvancedMode.On);

        Assert.Same(originalProjection, _viewModel.DetectedDirectories);
        Assert.Equal([@"E:\Fallout4", @"B:\Fallout4"], _viewModel.DetectedDirectories);
        var collection = Assert.IsAssignableFrom<ICollection<string>>(_viewModel.DetectedDirectories);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(@"Z:\Injected"));
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
        ProjectGameContext(@"C:\Games\Fallout4");

        // Assert
        Assert.False(_viewModel.HasMultipleDirectories);
    }

    [Fact]
    public void HasMultipleDirectories_ReturnsTrue_WhenMultipleEntries()
    {
        // Arrange
        ProjectGameContext(@"C:\Games\Fallout4", @"D:\Games\Fallout4");

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
        ProjectGameContext(@"C:\Games\Fallout4", @"D:\Games\Fallout4");

        // Assert
        Assert.Contains(nameof(MainWindowViewModel.HasMultipleDirectories), notifiedProperties);
    }

    /// <summary>
    /// Verifies that a changed projected Advanced Mode raises its public presentation notification.
    /// </summary>
    [Fact]
    public void ApplyGameContextProjection_ChangedAdvancedMode_RaisesPropertyChanged()
    {
        // Arrange
        var propertyName = string.Empty;
        _viewModel.PropertyChanged += (_, args) => propertyName = args.PropertyName;

        // Act
        _viewModel.ApplyGameContextProjection(
            null,
            null,
            [],
            AdvancedMode.On);

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
    public void Properties_DoNotRaisePropertyChanged_WhenSetToSameValue()
    {
        // Arrange
        _viewModel.DatabasePath = "TestPath";
        var eventRaised = false;
        _viewModel.PropertyChanged += (_, _) => eventRaised = true;

        // Act
        _viewModel.DatabasePath = "TestPath"; // Same value

        // Assert
        Assert.False(eventRaised);
    }

    #endregion

    #region Filtering Tests

    [Fact]
    public void PluginFilter_UpdatesFilteredPlugins_WhenChanged()
    {
        // Arrange
        ProjectPlugins(
            _viewModel,
            new PluginListItem { Name = "Plugin1.esp" },
            new PluginListItem { Name = "Plugin2.esp" },
            new PluginListItem { Name = "TestMod.esp" });

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
        ProjectPlugins(
            _viewModel,
            new PluginListItem { Name = "PLUGIN.esp" },
            new PluginListItem { Name = "plugin.esp" },
            new PluginListItem { Name = "PlUgIn.esp" });

        // Act
        _viewModel.PluginFilter = "plugin";

        // Assert
        Assert.Equal(3, _viewModel.FilteredPlugins.Count);
    }

    [Fact]
    public void ApplyFilter_ShowsAllPlugins_WhenFilterIsEmpty()
    {
        // Arrange
        ProjectPlugins(
            _viewModel,
            new PluginListItem { Name = "Plugin1.esp" },
            new PluginListItem { Name = "Plugin2.esp" });
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
        ProjectPlugins(
            _viewModel,
            new PluginListItem { Name = "Plugin1.esp" },
            new PluginListItem { Name = "Plugin2.esp" });

        // Act
        _viewModel.PluginFilter = "   ";

        // Assert
        Assert.Equal(_viewModel.Plugins.Count, _viewModel.FilteredPlugins.Count);
    }

    /// <summary>
    ///     Verifies filtering changes visibility only and restores projected order, instances, selection, and identity.
    /// </summary>
    [Fact]
    public void PluginFilter_HideShow_PreservesPluginListOrderInstancesAndProjectedSelection()
    {
        var first = new PluginListItem { Name = "First.esp", IsSelected = true, MembershipVersion = 7 };
        var second = new PluginListItem { Name = "Second.esp", MembershipVersion = 7 };
        var third = new PluginListItem { Name = "Third.esp", IsSelected = true, MembershipVersion = 7 };
        ProjectPlugins(_viewModel, first, second, third);

        _viewModel.PluginFilter = "Second";

        Assert.Same(second, Assert.Single(_viewModel.FilteredPlugins));

        _viewModel.PluginFilter = string.Empty;

        Assert.Equal([first, second, third], _viewModel.FilteredPlugins);
        Assert.True(_viewModel.FilteredPlugins[0].IsSelected);
        Assert.False(_viewModel.FilteredPlugins[1].IsSelected);
        Assert.True(_viewModel.FilteredPlugins[2].IsSelected);
        Assert.All(_viewModel.FilteredPlugins, plugin => Assert.Equal(7, plugin.MembershipVersion));
    }

    /// <summary>
    ///     Verifies presentation consumers can observe the stable projection without mutating its membership.
    /// </summary>
    [Fact]
    public void Plugins_PublicContract_ExposesReadOnlyProjection()
    {
        var property = typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.Plugins));

        Assert.NotNull(property);
        Assert.Equal(typeof(ReadOnlyObservableCollection<PluginListItem>), property.PropertyType);
        var collection = Assert.IsAssignableFrom<ICollection<PluginListItem>>(_viewModel.Plugins);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(new PluginListItem { Name = "Injected.esp" }));
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

    #region Workflow Activity Precedence Tests

    /// <summary>
    /// Verifies the Workflow Activity precedence rule as a truth table over both projected facts: run activity owns
    /// the progress channel while it is active, scan activity shows otherwise, and neither active shows nothing.
    /// </summary>
    /// <param name="runActive">Whether run activity is projected as active.</param>
    /// <param name="scanActive">Whether scan activity is projected as active.</param>
    /// <param name="expectedStatus">The status the channel is expected to show.</param>
    /// <param name="expectedValue">The progress value the channel is expected to show.</param>
    /// <param name="expectedVisible">Whether the channel is expected to be visible at all.</param>
    [Theory]
    [InlineData(false, false, "", 0d, false)]
    [InlineData(true, false, "Processing User.esp", 40d, true)]
    [InlineData(false, true, "Scanning plugins... (3/12)", 25d, true)]
    [InlineData(true, true, "Processing User.esp", 40d, true)]
    public void ActivityProjections_Precedence_ShowsTheWinningActivityOnTheChannel(
        bool runActive,
        bool scanActive,
        string expectedStatus,
        double expectedValue,
        bool expectedVisible)
    {
        _viewModel.ApplyRunActivityProjection(
            runActive ? new ActivityProjection(true, "Processing User.esp", 40) : ActivityProjection.None);
        _viewModel.ApplyScanActivityProjection(
            scanActive ? new ActivityProjection(true, "Scanning plugins... (3/12)", 25) : ActivityProjection.None);

        Assert.Equal(expectedStatus, _viewModel.ProgressStatus);
        Assert.Equal(expectedValue, _viewModel.ProgressValue);
        Assert.Equal(expectedVisible, _viewModel.IsProgressVisible);
    }

    /// <summary>
    /// Verifies the end-of-run case the precedence rule exists to make correct: a run clearing its activity while a
    /// Plugin List refresh is still scanning falls back to the refresh rather than blanking the channel.
    /// </summary>
    [Fact]
    public void ApplyRunActivityProjection_ClearedWhileScanActive_FallsBackToScanActivity()
    {
        _viewModel.ApplyScanActivityProjection(new ActivityProjection(true, "Scanning plugins... (3/12)", 25));
        _viewModel.ApplyRunActivityProjection(new ActivityProjection(true, "Processing User.esp", 40));

        _viewModel.ApplyRunActivityProjection(ActivityProjection.None);

        Assert.Equal("Scanning plugins... (3/12)", _viewModel.ProgressStatus);
        Assert.Equal(25, _viewModel.ProgressValue);
        Assert.True(_viewModel.IsProgressVisible);
    }

    /// <summary>
    /// Verifies that scan activity arriving mid-run cannot displace the run's report, which is the collision this
    /// projection exists to arbitrate.
    /// </summary>
    [Fact]
    public void ApplyScanActivityProjection_WhileRunActive_RaisesNoChannelNotification()
    {
        _viewModel.ApplyRunActivityProjection(new ActivityProjection(true, "Processing User.esp", 40));
        var notifiedProperties = new List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        _viewModel.ApplyScanActivityProjection(new ActivityProjection(true, "Scanning plugins...", 0));

        Assert.Empty(notifiedProperties);
        Assert.Equal("Processing User.esp", _viewModel.ProgressStatus);
        Assert.Equal(40, _viewModel.ProgressValue);
    }

    /// <summary>
    /// Verifies that a changed channel raises every derived notification the Main Window binds to.
    /// </summary>
    [Fact]
    public void ApplyScanActivityProjection_ChangedChannel_RaisesDerivedNotifications()
    {
        var notifiedProperties = new List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        _viewModel.ApplyScanActivityProjection(new ActivityProjection(true, "Scanning plugins...", 10));

        Assert.Contains(nameof(MainWindowViewModel.ProgressStatus), notifiedProperties);
        Assert.Contains(nameof(MainWindowViewModel.ProgressValue), notifiedProperties);
        Assert.Contains(nameof(MainWindowViewModel.IsProgressVisible), notifiedProperties);
    }

    /// <summary>
    /// Verifies that the process button caption is derived from run activity alone, so presentation owns the wording
    /// of a control whose meaning comes from a single boolean.
    /// </summary>
    [Fact]
    public void ProcessButtonText_DerivedFromRunActivity_TracksWhetherARunIsActive()
    {
        Assert.Equal("Process FormIDs", _viewModel.ProcessButtonText);
        var notifiedProperties = new List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        _viewModel.ApplyRunActivityProjection(new ActivityProjection(true, "Initializing...", 0));

        Assert.Equal("Cancel Processing", _viewModel.ProcessButtonText);
        Assert.Contains(nameof(MainWindowViewModel.ProcessButtonText), notifiedProperties);

        _viewModel.ApplyRunActivityProjection(ActivityProjection.None);

        Assert.Equal("Process FormIDs", _viewModel.ProcessButtonText);
    }

    /// <summary>
    /// Verifies that scan activity leaves the process button alone: only run activity decides what pressing it means.
    /// </summary>
    [Fact]
    public void ProcessButtonText_ScanActivityProjected_StaysAtItsIdleCaption()
    {
        _viewModel.ApplyScanActivityProjection(new ActivityProjection(true, "Scanning plugins...", 0));

        Assert.Equal("Process FormIDs", _viewModel.ProcessButtonText);
    }

    /// <summary>
    /// Verifies that an off-thread projection is marshalled as one posted action, so no observer can read a status
    /// from one snapshot paired with a value from another.
    /// </summary>
    /// <param name="projectRunActivity">Whether the run activity seam is exercised rather than the scan seam.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActivityProjection_WithoutDispatcherAccess_AppliesAsOnePostedAction(bool projectRunActivity)
    {
        var dispatcher = new RecordingThreadDispatcher(false);
        var viewModel = new MainWindowViewModel(dispatcher);
        var activity = new ActivityProjection(true, "Working...", 60);

        if (projectRunActivity)
        {
            viewModel.ApplyRunActivityProjection(activity);
        }
        else
        {
            viewModel.ApplyScanActivityProjection(activity);
        }

        Assert.Equal(1, dispatcher.PostCount);
        Assert.Equal(string.Empty, viewModel.ProgressStatus);

        dispatcher.DrainPostedActions(true);

        Assert.Equal("Working...", viewModel.ProgressStatus);
        Assert.Equal(60, viewModel.ProgressValue);
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

    /// <summary>
    ///     Verifies mutable message presentation remains independent from read-only Plugin membership projection.
    /// </summary>
    [Fact]
    public void MessageCollections_PublicMutation_RetainsItems()
    {
        // Act
        _viewModel.ErrorMessages.Add("Error");
        _viewModel.InformationMessages.Add("Info");
        _viewModel.WarningMessages.Add("Warning");

        // Assert
        Assert.Single(_viewModel.ErrorMessages);
        Assert.Single(_viewModel.InformationMessages);
        Assert.Single(_viewModel.WarningMessages);
    }

    #endregion

    #region Construction Contract Tests

    /// <summary>
    ///     Verifies the ViewModel cannot be constructed without the dispatcher its marshalling invariant depends on.
    /// </summary>
    [Fact]
    public void Constructor_NullDispatcher_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MainWindowViewModel(null!));
    }

    /// <summary>
    ///     Verifies the ViewModel carries no disposal obligation now that no delay path owns a cancellation source.
    /// </summary>
    [Fact]
    public void TypeShape_ViewModelWithoutDelayPath_CarriesNoDisposalObligation()
    {
        Assert.DoesNotContain(typeof(IDisposable), typeof(MainWindowViewModel).GetInterfaces());
    }

    #endregion

    #region Filter Application Tests

    /// <summary>
    ///     Verifies a filter change reaches the filtered projection immediately, with no delay path in between.
    /// </summary>
    [Fact]
    public void PluginFilter_Changed_AppliesFilterImmediately()
    {
        // Arrange
        var viewModel = new MainWindowViewModel(new SynchronousThreadDispatcher());
        ProjectPlugins(
            viewModel,
            new PluginListItem { Name = "Plugin1.esp" },
            new PluginListItem { Name = "TestMod.esp" });

        // Act
        viewModel.PluginFilter = "Plugin";

        // Assert
        Assert.Single(viewModel.FilteredPlugins);
        Assert.Equal("Plugin1.esp", viewModel.FilteredPlugins[0].Name);
    }

    /// <summary>
    ///     Verifies a filter change made off the owning dispatcher is posted rather than applied in place.
    /// </summary>
    [Fact]
    public void PluginFilter_UnavailableDispatcher_PostsBeforeUpdatingFilteredPlugins()
    {
        // Arrange
        var dispatcher = new RecordingThreadDispatcher(hasAccess: true);
        var viewModel = new MainWindowViewModel(dispatcher);
        ProjectPlugins(
            viewModel,
            new PluginListItem { Name = "Plugin1.esp" },
            new PluginListItem { Name = "TestMod.esp" });
        dispatcher.HasAccess = false;

        // Act
        viewModel.PluginFilter = "Plugin";

        // Assert
        Assert.True(dispatcher.PostCount > 0);
        Assert.Equal(2, viewModel.FilteredPlugins.Count);

        dispatcher.DrainPostedActions(hasAccessDuringDrain: true);

        Assert.Single(viewModel.FilteredPlugins);
        Assert.Equal("Plugin1.esp", viewModel.FilteredPlugins[0].Name);
    }

    /// <summary>
    ///     Verifies filtering reconciles in place, so a hidden item keeps its instance identity and selection.
    /// </summary>
    [Fact]
    public void PluginFilter_Changed_PreservesPluginInstancesAndSelectionAcrossHideShow()
    {
        // Arrange
        var viewModel = new MainWindowViewModel(new SynchronousThreadDispatcher());
        var selectedPlugin = new PluginListItem { Name = "SelectedPlugin.esp", IsSelected = true };
        var otherPlugin = new PluginListItem { Name = "OtherPlugin.esp" };
        ProjectPlugins(viewModel, selectedPlugin, otherPlugin);

        // Act
        viewModel.PluginFilter = "Other";

        // Assert
        Assert.DoesNotContain(selectedPlugin, viewModel.FilteredPlugins);
        Assert.Contains(otherPlugin, viewModel.FilteredPlugins);

        // Act
        viewModel.PluginFilter = "Selected";

        // Assert
        var visiblePlugin = Assert.Single(viewModel.FilteredPlugins);
        Assert.Same(selectedPlugin, visiblePlugin);
        Assert.True(visiblePlugin.IsSelected);
    }

    #endregion

    #region Filter Suspension Tests

    /// <summary>
    ///     Verifies an adapter-owned membership replacement reapplies the active presentation filter atomically.
    /// </summary>
    [Fact]
    public void ReplacePluginProjection_ActiveFilter_AppliesFilteredMembership()
    {
        // Arrange
        _viewModel.PluginFilter = "Test";

        // Act
        ProjectPlugins(
            _viewModel,
            new PluginListItem { Name = "TestPlugin.esp" },
            new PluginListItem { Name = "OtherPlugin.esp" });

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
        ProjectPlugins(
            _viewModel,
            Enumerable.Range(0, 1000)
                .Select(i => new PluginListItem { Name = $"Plugin{i}.esp" })
                .ToArray());

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
    public void Filter_HandlesSpecialCharacters()
    {
        // Arrange
        ProjectPlugins(
            _viewModel,
            new PluginListItem { Name = "Plugin[Special].esp" },
            new PluginListItem { Name = "Plugin(Test).esp" },
            new PluginListItem { Name = "Plugin.Test.esp" });

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
        _viewModel.DatabasePath = "TestPath";

        // Act - Test that setting the same value doesn't raise PropertyChanged
        var eventRaised = false;
        _viewModel.PropertyChanged += (_, _) => eventRaised = true;
        _viewModel.DatabasePath = "TestPath"; // Same value

        // Assert
        Assert.False(eventRaised);
        Assert.Equal("TestPath", _viewModel.DatabasePath);
    }

    #endregion

    /// <summary>
    ///     Publishes one ordered membership through the adapter-owned ViewModel projection seam.
    /// </summary>
    private static void ProjectPlugins(
        MainWindowViewModel viewModel,
        params PluginListItem[] projectedItems)
    {
        viewModel.ReplacePluginProjection(projectedItems);
    }

    /// <summary>
    /// Publishes one available-directory membership through the complete Game Context projection seam.
    /// </summary>
    private void ProjectGameContext(params string[] availableDirectories)
    {
        _viewModel.ApplyGameContextProjection(
            _viewModel.SelectedGame,
            string.IsNullOrEmpty(_viewModel.GameDirectory) ? null : _viewModel.GameDirectory,
            availableDirectories,
            _viewModel.AdvancedMode ? AdvancedMode.On : AdvancedMode.Off);
    }

    private sealed class RecordingThreadDispatcher(bool hasAccess) : IThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _postedActions = new();
        private int _postCount;

        public bool HasAccess { get; set; } = hasAccess;

        public int PostCount => Volatile.Read(ref _postCount);

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
