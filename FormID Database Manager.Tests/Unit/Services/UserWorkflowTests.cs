#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class UserWorkflowTests
{
    private const string GameDirectory = @"C:\Games\Skyrim";
    private const string DatabasePath = @"C:\Databases\formids.db";
    private const string FormIdListPath = @"C:\Lists\formids.txt";

    private readonly SynchronousThreadDispatcher _dispatcher = new();
    private readonly Mock<IFileDialogService> _fileDialogService = new();
    private readonly Mock<GameDetectionService> _gameDetectionService;
    private readonly Mock<IGameLocationService> _gameLocationService = new();
    private readonly PluginList _pluginList;
    private readonly RecordingPluginListDiscovery _pluginListDiscovery;
    private readonly PluginListPresentationAdapter _pluginListPresentationAdapter;
    private readonly RecordingProcessingRunExecutor _processingRunExecutor;
    private readonly List<ProcessingRunRequest> _processingRuns = [];
    private readonly List<PluginListSource> _refreshes = [];
    private readonly MainWindowViewModel _viewModel;

    public UserWorkflowTests()
    {
        _viewModel = new MainWindowViewModel(_dispatcher);
        _gameDetectionService = FormID_Database_Manager.TestUtilities.Mocks.MockFactory.CreateGameDetectionServiceMock();
        _pluginListDiscovery = new RecordingPluginListDiscovery(_refreshes);
        _pluginList = new PluginList(_gameDetectionService.Object, _pluginListDiscovery);
        _pluginListPresentationAdapter = new PluginListPresentationAdapter(_pluginList, _viewModel, _dispatcher);
        _processingRunExecutor = new RecordingProcessingRunExecutor(_processingRuns);
    }

    /// <summary>
    /// Verifies that an explicit release intent clears stale source state before installed-location lookup completes.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_ChangedRelease_ImmediatelyClearsStateThenPublishesOrderedLocations()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLookupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE)).Returns([@"C:\Old"]);
        _pluginListDiscovery.PluginNames = ["Old.esp"];
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _pluginListDiscovery.PluginNames = [];
        _refreshes.Clear();
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns(() =>
            {
                lookupStarted.SetResult();
                allowLookupToFinish.Task.GetAwaiter().GetResult();
                return [@"C:\Games\Fallout4", @"D:\Games\Fallout4"];
            });

        var selection = sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        await lookupStarted.Task;

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.Null(_pluginList.Current.Confirmed);
        Assert.Empty(_viewModel.Plugins);

        allowLookupToFinish.SetResult();
        await selection;

        Assert.Equal(@"C:\Games\Fallout4", _viewModel.GameDirectory);
        Assert.Equal([@"C:\Games\Fallout4", @"D:\Games\Fallout4"], _viewModel.DetectedDirectories.ToArray());
        Assert.Empty(_viewModel.Plugins);
        AssertSingleRefresh(@"C:\Games\Fallout4", GameRelease.Fallout4, false);
    }

    /// <summary>
    /// Verifies that Advanced Mode becomes authoritative without retiring a current installed-location lookup.
    /// </summary>
    [Fact]
    public async Task SetAdvancedModeAsync_DuringLocationLookup_ProjectsImmediatelyAndRefreshesWithLatestMode()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLookupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns(() =>
            {
                lookupStarted.SetResult();
                allowLookupToFinish.Task.GetAwaiter().GetResult();
                return [@"C:\Games\Fallout4"];
            });
        var sut = CreateSut();

        var releaseSelection = sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        await lookupStarted.Task;

        await sut.SetAdvancedModeAsync(AdvancedMode.On);

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.GameDirectory);
        Assert.True(_viewModel.AdvancedMode);
        Assert.Empty(_refreshes);
        Assert.Null(_pluginList.Current.Confirmed);

        allowLookupToFinish.SetResult();
        await releaseSelection;

        Assert.Equal(@"C:\Games\Fallout4", _viewModel.GameDirectory);
        AssertSingleRefresh(@"C:\Games\Fallout4", GameRelease.Fallout4, true);
    }

    /// <summary>
    /// Verifies that an equal Advanced Mode event does not request duplicate Plugin List work.
    /// </summary>
    [Fact]
    public async Task SetAdvancedModeAsync_EqualValue_IsNoOp()
    {
        var sut = CreateSut();

        await sut.SetAdvancedModeAsync(AdvancedMode.Off);

        Assert.False(_viewModel.AdvancedMode);
        Assert.Empty(_refreshes);
    }

    /// <summary>
    /// Verifies that an explicit directory intent retires older location resolution and survives discovery failure.
    /// </summary>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_DuringLocationLookup_ManualDirectoryRemainsAuthoritative()
    {
        const string manualDirectory = @"D:\Games\Skyrim";
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLookupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var discoveryCompletion = new TaskCompletionSource<PluginListDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns(() =>
            {
                lookupStarted.SetResult();
                allowLookupToFinish.Task.GetAwaiter().GetResult();
                return [@"C:\Automatic\Skyrim"];
            });
        _pluginListDiscovery.Handler = (_, _) => discoveryCompletion.Task;
        var sut = CreateSut();

        var releaseSelection = sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await lookupStarted.Task;

        var directorySelection = sut.SelectDetectedDirectoryAsync(manualDirectory);

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(manualDirectory, _viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.Equal(@"D:\Games\Skyrim\Data", Assert.Single(_refreshes).DataDirectory);

        allowLookupToFinish.SetResult();
        await releaseSelection;

        Assert.Equal(manualDirectory, _viewModel.GameDirectory);
        Assert.Single(_refreshes);

        discoveryCompletion.SetResult(PluginListDiscoveryResult.Failed("selected release does not match directory"));
        await directorySelection;

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(manualDirectory, _viewModel.GameDirectory);
        Assert.Null(_pluginList.Current.Confirmed);
        Assert.Contains(
            "Failed to load plugins: selected release does not match directory",
            _viewModel.ErrorMessages);
    }

    /// <summary>
    /// Verifies that an empty current lookup retains the explicit release and presents Browse guidance.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_WithoutInstalledFolders_RetainsIncompleteContextAndRecordsGuidance()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns([]);

        var sut = CreateSut();

        await sut.SelectGameReleaseAsync(GameRelease.Fallout4);

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.Contains(
            "No installed locations found for Fallout4. Use Browse to select a directory.",
            _viewModel.InformationMessages);
        Assert.Empty(_refreshes);
    }

    /// <summary>
    /// Verifies that an equal explicit release event performs no duplicate lookup or refresh work.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_EqualRelease_IsNoOp()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE)).Returns([GameDirectory]);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _gameLocationService.Invocations.Clear();
        _refreshes.Clear();

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        _gameLocationService.Verify(
            x => x.GetGameFolders(It.IsAny<GameRelease>()),
            Times.Never);
        Assert.Empty(_refreshes);
        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal([GameDirectory], _viewModel.DetectedDirectories);
    }

    /// <summary>
    /// Verifies that a single installed location remains part of the complete ordered available-directory snapshot.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_SingleInstalledDirectory_ProjectsCompleteAvailableSet()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns([GameDirectory]);
        var sut = CreateSut();

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal([GameDirectory], _viewModel.DetectedDirectories);
        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, false);
    }

    /// <summary>
    /// Verifies that the directory projected from a current lookup does not request a duplicate refresh.
    /// </summary>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_EqualDetectedDirectory_DoesNotDuplicateRefresh()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns([GameDirectory, @"D:\Games\Skyrim"]);

        var sut = CreateSut();

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await sut.SelectDetectedDirectoryAsync(GameDirectory);

        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, false);
    }

    /// <summary>
    /// Verifies that an explicit changed directory is projected while preserving the ordered suggestions.
    /// </summary>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_ChangedDirectory_RefreshesPluginListForExplicitValue()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns([GameDirectory, @"D:\Games\Skyrim"]);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _refreshes.Clear();

        await sut.SelectDetectedDirectoryAsync(@"D:\Games\Skyrim");

        Assert.Equal(@"D:\Games\Skyrim", _viewModel.GameDirectory);
        Assert.Equal([GameDirectory, @"D:\Games\Skyrim"], _viewModel.DetectedDirectories);
        AssertSingleRefresh(@"D:\Games\Skyrim", GameRelease.SkyrimSE, false);
    }

    /// <summary>
    /// Verifies that only the latest of two controlled release lookups can publish guidance, state, or refresh work.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_OverlappingSelections_AppliesLatestSelectionOnly()
    {
        var olderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOlderToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns(() =>
            {
                olderStarted.SetResult();
                allowOlderToFinish.Task.GetAwaiter().GetResult();
                return [];
            });
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns([@"C:\NewFallout"]);

        var sut = CreateSut();

        var olderSelection = sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await olderStarted.Task;

        var newerSelection = sut.SelectGameReleaseAsync(GameRelease.Fallout4);

        allowOlderToFinish.SetResult();
        await Task.WhenAll(olderSelection, newerSelection);

        Assert.Equal(@"C:\NewFallout", _viewModel.GameDirectory);
        AssertSingleRefresh(@"C:\NewFallout", GameRelease.Fallout4, false);
        Assert.DoesNotContain(
            "No installed locations found for SkyrimSE. Use Browse to select a directory.",
            _viewModel.InformationMessages);
    }

    /// <summary>
    /// Verifies that a retired installed-location failure cannot escape and create an obsolete WinUI message.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_RetiredLookupFailure_IsSuppressed()
    {
        var olderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOlderToFail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns(() =>
            {
                olderStarted.SetResult();
                allowOlderToFail.Task.GetAwaiter().GetResult();
                throw new InvalidOperationException("obsolete lookup failure");
            });
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns([@"C:\NewFallout"]);
        var sut = CreateSut();

        var olderSelection = sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await olderStarted.Task;
        var newerSelection = sut.SelectGameReleaseAsync(GameRelease.Fallout4);

        allowOlderToFail.SetResult();
        await Task.WhenAll(olderSelection, newerSelection);

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Equal(@"C:\NewFallout", _viewModel.GameDirectory);
        AssertSingleRefresh(@"C:\NewFallout", GameRelease.Fallout4, false);
        Assert.DoesNotContain(_viewModel.ErrorMessages, message => message.Contains("obsolete", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that opening Browse retires older location resolution even when the picker is later cancelled.
    /// </summary>
    /// <returns>A task that completes after the controlled resolution and picker operations settle.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_PickerCancellation_RetiresOlderLocationLookupWithoutChangingSnapshot()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLookupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pickerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pickerCompletion = new TaskCompletionSource<FileDialogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns(() =>
            {
                lookupStarted.SetResult();
                allowLookupToFinish.Task.GetAwaiter().GetResult();
                return [GameDirectory];
            });
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .Returns(() =>
            {
                pickerStarted.SetResult();
                return pickerCompletion.Task;
            });
        var sut = CreateSut();

        var releaseSelection = sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await lookupStarted.Task;
        var browse = sut.BrowseGameDirectoryAsync();
        await pickerStarted.Task;

        allowLookupToFinish.SetResult();
        await releaseSelection;

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.Empty(_refreshes);

        pickerCompletion.SetResult(FileDialogResult.Cancelled());
        await browse;

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_refreshes);
    }

    /// <summary>
    /// Verifies that cancelling a newer Browse leaves the snapshot unchanged and retires an older picker result.
    /// </summary>
    /// <returns>A task that completes after both controlled picker operations settle.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_NewerPickerCancellation_RetiresOlderPickerResult()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns([@"C:\Existing", @"D:\Suggested"]);
        var olderPicker = new TaskCompletionSource<FileDialogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var newerPicker = new TaskCompletionSource<FileDialogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _fileDialogService.SetupSequence(x => x.SelectGameDirectory())
            .Returns(olderPicker.Task)
            .Returns(newerPicker.Task);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        _refreshes.Clear();

        var olderBrowse = sut.BrowseGameDirectoryAsync();
        var newerBrowse = sut.BrowseGameDirectoryAsync();
        newerPicker.SetResult(FileDialogResult.Cancelled());
        await newerBrowse;
        olderPicker.SetResult(FileDialogResult.Success(GameDirectory));
        await olderBrowse;

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Equal(@"C:\Existing", _viewModel.GameDirectory);
        Assert.Equal([@"C:\Existing", @"D:\Suggested"], _viewModel.DetectedDirectories);
        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_refreshes);
        _gameDetectionService.Verify(x => x.DetectGame(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Verifies that only the latest of two controlled Browse pickers can publish its selected directory.
    /// </summary>
    /// <returns>A task that completes after both controlled picker operations settle.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_OverlappingPickers_LatestSelectionWins()
    {
        const string latestDirectory = @"D:\Games\Fallout4";
        var olderPicker = new TaskCompletionSource<FileDialogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var newerPicker = new TaskCompletionSource<FileDialogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _fileDialogService.SetupSequence(x => x.SelectGameDirectory())
            .Returns(olderPicker.Task)
            .Returns(newerPicker.Task);
        _gameDetectionService.Setup(x => x.DetectGame(latestDirectory)).Returns(GameRelease.Fallout4);
        var sut = CreateSut();

        var olderBrowse = sut.BrowseGameDirectoryAsync();
        var newerBrowse = sut.BrowseGameDirectoryAsync();
        newerPicker.SetResult(FileDialogResult.Success(latestDirectory));
        await newerBrowse;
        olderPicker.SetResult(FileDialogResult.Success(GameDirectory));
        await olderBrowse;

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Equal(latestDirectory, _viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        AssertSingleRefresh(latestDirectory, GameRelease.Fallout4, false);
        _gameDetectionService.Verify(x => x.DetectGame(GameDirectory), Times.Never);
    }

    /// <summary>
    /// Verifies that a newer release intent prevents an older picker result from publishing any Browse state.
    /// </summary>
    /// <returns>A task that completes after the release intent and controlled picker settle.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_PickerSupersededByReleaseSelection_IgnoresOlderResult()
    {
        const string latestDirectory = @"C:\Games\Fallout4";
        var pickerCompletion = new TaskCompletionSource<FileDialogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _fileDialogService.Setup(x => x.SelectGameDirectory()).Returns(pickerCompletion.Task);
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4)).Returns([latestDirectory]);
        var sut = CreateSut();

        var browse = sut.BrowseGameDirectoryAsync();
        var releaseSelection = sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        pickerCompletion.SetResult(FileDialogResult.Success(GameDirectory));
        await Task.WhenAll(browse, releaseSelection);

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Equal(latestDirectory, _viewModel.GameDirectory);
        Assert.Equal([latestDirectory], _viewModel.DetectedDirectories);
        AssertSingleRefresh(latestDirectory, GameRelease.Fallout4, false);
        _gameDetectionService.Verify(x => x.DetectGame(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a newer explicit directory suppresses an older picker failure and its error message.
    /// </summary>
    /// <returns>A task that completes after the directory intent and controlled picker settle.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_PickerFailureSupersededByDirectorySelection_IsSilent()
    {
        const string latestDirectory = @"D:\Games\Skyrim";
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns([GameDirectory, latestDirectory]);
        var pickerCompletion = new TaskCompletionSource<FileDialogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _fileDialogService.Setup(x => x.SelectGameDirectory()).Returns(pickerCompletion.Task);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _refreshes.Clear();

        var browse = sut.BrowseGameDirectoryAsync();
        await sut.SelectDetectedDirectoryAsync(latestDirectory);
        pickerCompletion.SetResult(FileDialogResult.Failure("obsolete picker failure"));
        await browse;

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(latestDirectory, _viewModel.GameDirectory);
        Assert.Equal([GameDirectory, latestDirectory], _viewModel.DetectedDirectories);
        AssertSingleRefresh(latestDirectory, GameRelease.SkyrimSE, false);
        Assert.DoesNotContain(
            _viewModel.ErrorMessages,
            message => message.Contains("obsolete picker failure", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that Browse detects a missing release without retaining suggestions or starting location lookup.
    /// </summary>
    /// <returns>A task that completes after Browse detection and Plugin List discovery finish.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_SelectedDirectoryDetectsGameAndRefreshesPlugins()
    {
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        _gameDetectionService.Setup(x => x.DetectGame(GameDirectory)).Returns(GameRelease.SkyrimSE);

        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.DetectedDirectories);
        _gameLocationService.Verify(
            x => x.GetGameFolders(It.IsAny<GameRelease>()),
            Times.Never);
        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, false);
    }

    /// <summary>
    /// Verifies that a newer release intent prevents an older Browse detection result from publishing context or refresh.
    /// </summary>
    /// <returns>A task that completes after the controlled detection and newer release intent settle.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_DetectionSupersededByReleaseSelection_CannotPublishDetectedContext()
    {
        var pickerCompletion = new TaskCompletionSource<FileDialogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var detectionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDetectionToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .Returns(pickerCompletion.Task);
        _gameDetectionService.Setup(x => x.DetectGame(GameDirectory))
            .Returns(() =>
            {
                detectionStarted.SetResult();
                allowDetectionToFinish.Task.GetAwaiter().GetResult();
                return GameRelease.SkyrimSE;
            });
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns([@"C:\Games\Fallout4"]);
        var sut = CreateSut();

        var browse = sut.BrowseGameDirectoryAsync();
        pickerCompletion.SetResult(FileDialogResult.Success(GameDirectory));
        await detectionStarted.Task;
        var newerRelease = sut.SelectGameReleaseAsync(GameRelease.Fallout4);

        allowDetectionToFinish.SetResult();
        await Task.WhenAll(browse, newerRelease);

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Equal(@"C:\Games\Fallout4", _viewModel.GameDirectory);
        Assert.Equal([@"C:\Games\Fallout4"], _viewModel.DetectedDirectories);
        AssertSingleRefresh(@"C:\Games\Fallout4", GameRelease.Fallout4, false);
    }

    /// <summary>
    /// Verifies that browsing the current path retries discovery and leaves prior confirmation cleared on failure.
    /// </summary>
    /// <returns>A task that completes after the same-path discovery retry fails.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_SamePathDiscoveryFailure_RetriesAndClearsConfirmation()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns([GameDirectory, @"D:\Games\Skyrim"]);
        _pluginListDiscovery.PluginNames = ["PreviouslyConfirmed.esp"];
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.NotNull(_pluginList.Current.Confirmed);
        _refreshes.Clear();
        _pluginListDiscovery.Handler = (_, _) =>
            Task.FromResult<PluginListDiscoveryResult>(PluginListDiscoveryResult.Failed("directory mismatch"));
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal([GameDirectory, @"D:\Games\Skyrim"], _viewModel.DetectedDirectories);
        Assert.Null(_pluginList.Current.Confirmed);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory), Assert.Single(_refreshes));
        Assert.Contains("Failed to load plugins: directory mismatch", _viewModel.ErrorMessages);
        _gameDetectionService.Verify(x => x.DetectGame(It.IsAny<string>()), Times.Never);
        _gameLocationService.Verify(x => x.GetGameFolders(GameRelease.SkyrimSE), Times.Once);
    }

    /// <summary>
    /// Verifies that current detection failure retains the chosen path, clears confirmation, and presents guidance.
    /// </summary>
    /// <returns>A task that completes after detection failure is presented.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_SelectedDirectoryWithoutDetectableGame_RecordsWorkflowError()
    {
        _pluginListDiscovery.PluginNames = ["PreviouslyConfirmed.esp"];
        await _pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        Assert.NotNull(_pluginList.Current.Confirmed);
        _refreshes.Clear();
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        _gameDetectionService.Setup(x => x.DetectGame(GameDirectory)).Returns((GameRelease?)null);

        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Null(_viewModel.SelectedGame);
        Assert.Null(_pluginList.Current.Confirmed);
        Assert.Contains(
            "Could not detect game from directory. Please select a game from the dropdown.",
            _viewModel.ErrorMessages);
        Assert.Empty(_refreshes);
    }

    /// <summary>
    /// Verifies that current picker failure leaves the existing Game Context unchanged and reports the failure.
    /// </summary>
    /// <returns>A task that completes after picker failure is presented.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_CurrentPickerFailure_LeavesSnapshotUnchangedAndRecordsError()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns([@"C:\Existing", @"D:\Suggested"]);
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Failure("picker unavailable"));
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        _refreshes.Clear();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Equal(@"C:\Existing", _viewModel.GameDirectory);
        Assert.Equal([@"C:\Existing", @"D:\Suggested"], _viewModel.DetectedDirectories);
        Assert.Contains("Error selecting game directory: picker unavailable", _viewModel.ErrorMessages);
        Assert.Empty(_refreshes);
    }

    /// <summary>
    /// Verifies that picker cancellation leaves an existing Game Context unchanged without adding an error.
    /// </summary>
    /// <returns>A task that completes after picker cancellation is observed.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_PickerCancel_LeavesStateUnchangedAndAddsNoError()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4)).Returns([@"C:\Existing"]);
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Cancelled());

        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        _refreshes.Clear();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(@"C:\Existing", _viewModel.GameDirectory);
        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_refreshes);
    }

    [Fact]
    public async Task SelectDatabaseAsync_PickerFailure_LeavesStateUnchangedAndRecordsWorkflowError()
    {
        _viewModel.DatabasePath = DatabasePath;
        _fileDialogService.Setup(x => x.SelectDatabaseFile())
            .ReturnsAsync(FileDialogResult.Failure("picker unavailable"));

        var sut = CreateSut();

        await sut.SelectDatabaseAsync();

        Assert.Equal(DatabasePath, _viewModel.DatabasePath);
        Assert.Contains("Error selecting database: picker unavailable", _viewModel.ErrorMessages);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_NoSelectedGame_RecordsValidationMessage()
    {
        var sut = CreateSut();

        await sut.ProcessFormIdsAsync();

        Assert.Contains("Please select a game from the dropdown first.", _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
        Assert.False(_viewModel.IsProcessing);
        Assert.Equal("Process FormIDs", _viewModel.ProcessButtonText);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_PluginIngestionWithoutGameDirectory_RecordsValidationMessage()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE)).Returns([]);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        await sut.ProcessFormIdsAsync();

        Assert.Contains("Game directory must be specified when processing plugins", _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_PluginIngestionWithoutSelectedPlugins_RecordsValidationMessage()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE)).Returns([GameDirectory]);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await sut.ProcessFormIdsAsync();
        Assert.Contains("No plugins selected", _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
    }

    /// <summary>
    /// Verifies that an empty confirmed selection retains the existing user-facing validation message.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_ConfirmedListWithoutSelection_RecordsExistingValidationMessage()
    {
        var sut = CreateSut();
        await ConfirmPluginListAsync(sut, ["User.esp"]);

        await sut.ProcessFormIdsAsync();

        Assert.Contains("No plugins selected", _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_FormIdTextFile_SkipsPluginSelectionValidation()
    {
        _viewModel.DatabasePath = DatabasePath;
        _viewModel.FormIdListPath = FormIdListPath;
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE)).Returns([]);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<FormIdTextProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(FormIdListPath, run.FormIdListPath);
    }

    /// <summary>
    /// Verifies that text-run validation and default Store naming use authority even while presentation is still queued.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_FormIdTextFile_UsesAuthoritativeReleaseBeforeQueuedProjection()
    {
        var queuedActions = new Queue<Action>();
        var dispatcher = new QueuedThreadDispatcher(
            () => false,
            action =>
            {
                queuedActions.Enqueue(action);
                return true;
            },
            "Dispatcher rejected test work.");
        var viewModel = new MainWindowViewModel(dispatcher)
        {
            FormIdListPath = FormIdListPath
        };
        var pluginList = new PluginList(_gameDetectionService.Object, _pluginListDiscovery);
        var processingRuns = new List<ProcessingRunRequest>();
        var executor = new RecordingProcessingRunExecutor(processingRuns);
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4)).Returns([]);
        using var sut = new UserWorkflow(
            viewModel,
            _fileDialogService.Object,
            _gameDetectionService.Object,
            _gameLocationService.Object,
            pluginList,
            executor);

        await sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        Assert.Null(viewModel.SelectedGame);
        Assert.NotEmpty(queuedActions);

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<FormIdTextProcessingRunRequest>(Assert.Single(processingRuns));
        Assert.Equal(GameRelease.Fallout4, run.GameRelease);
        Assert.Equal("Fallout4.db", Path.GetFileName(run.DatabasePath));
        Assert.Equal(run.DatabasePath, viewModel.DatabasePath);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_EmptyDatabasePath_CreatesDefaultPathForSelectedGame()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
        _viewModel.DatabasePath = string.Empty;

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(DefaultDatabasePathProvider.CreateDefaultDatabasePath(GameRelease.SkyrimSE), _viewModel.DatabasePath);
        Assert.Equal(_viewModel.DatabasePath, run.DatabasePath);
        Assert.Equal("SkyrimSE.db", Path.GetFileName(run.DatabasePath));
        Assert.Equal(["User.esp"], run.PluginNames);
    }

    /// <summary>
    /// Verifies that a Plugin Processing Run captures one confirmed source and ordered selection snapshot.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_PluginRun_CapturesCanonicalConfirmedPluginListFactsInOrder()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["First.esp", "Second.esp", "Third.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Third.esp", true);
        sut.SetPluginSelection(confirmed.MembershipVersion, "First.esp", true);

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(GameRelease.SkyrimSE, run.GameRelease);
        Assert.Equal(confirmed.Source.DataDirectory, run.GameDirectory);
        Assert.Equal(["First.esp", "Third.esp"], run.PluginNames);
    }

    /// <summary>
    /// Verifies that later same-source refresh and selection publication cannot mutate an already captured run request.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DuringSameSourceRefresh_CapturedRequestRemainsImmutable()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["First.esp", "Second.esp", "Third.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "First.esp", true);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Third.esp", true);

        var refreshCompletion = new TaskCompletionSource<PluginListDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListDiscovery.Handler = (_, _) => refreshCompletion.Task;
        var refresh = sut.SetAdvancedModeAsync(AdvancedMode.On);

        await sut.ProcessFormIdsAsync();
        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));

        refreshCompletion.SetResult(PluginListDiscoveryResult.Completed(
            ["Third.esp", "Second.esp", "First.esp", "New.esp"]));
        await refresh;
        var refreshed = Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
        sut.SetPluginSelection(refreshed.MembershipVersion, "Second.esp", true);

        Assert.Equal(confirmed.Source.GameRelease, run.GameRelease);
        Assert.Equal(confirmed.Source.DataDirectory, run.GameDirectory);
        Assert.Equal(["First.esp", "Third.esp"], run.PluginNames);
    }

    /// <summary>
    /// Verifies that a different-source transition invalidates old selection before a run request can be created.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DuringSourceTransition_DoesNotMixOldSelectionWithNewGameContext()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["Old.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Old.esp", true);

        var refreshCompletion = new TaskCompletionSource<PluginListDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListDiscovery.Handler = (_, _) => refreshCompletion.Task;
        var transition = sut.SelectDetectedDirectoryAsync(@"D:\Games\Fallout4");
        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(@"D:\Games\Fallout4", _viewModel.GameDirectory);
        Assert.Null(_pluginList.Current.Confirmed);

        await sut.ProcessFormIdsAsync();

        Assert.Empty(_processingRuns);
        Assert.Contains("No plugins selected", _viewModel.ErrorMessages);

        refreshCompletion.SetResult(PluginListDiscoveryResult.Completed(["New.esp"]));
        await transition;
    }

    /// <summary>
    /// Verifies that a run started reentrantly from release projection cannot reuse the previous source confirmation.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DuringReleaseProjection_UsesCapturedAuthoritativeContext()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["Old.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Old.esp", true);
        _viewModel.DatabasePath = string.Empty;
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4)).Returns([]);
        Task? reentrantRun = null;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.SelectedGame))
            {
                reentrantRun = sut.ProcessFormIdsAsync();
            }
        };

        await sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        Assert.NotNull(reentrantRun);
        await reentrantRun;

        Assert.Empty(_processingRuns);
        Assert.Contains("Game directory must be specified when processing plugins", _viewModel.ErrorMessages);
        Assert.Equal(
            "Fallout4.db",
            Path.GetFileName(_viewModel.DatabasePath));
    }

    /// <summary>
    /// Verifies that a run started reentrantly from directory projection rejects confirmation for the previous source.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DuringDirectoryProjection_RejectsMismatchedConfirmation()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["Old.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Old.esp", true);
        _viewModel.DatabasePath = DatabasePath;
        var refreshCompletion = new TaskCompletionSource<PluginListDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListDiscovery.Handler = (_, _) => refreshCompletion.Task;
        Task? reentrantRun = null;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.GameDirectory))
            {
                reentrantRun = sut.ProcessFormIdsAsync();
            }
        };

        var transition = sut.SelectDetectedDirectoryAsync(@"D:\Games\Skyrim");
        Assert.NotNull(reentrantRun);
        await reentrantRun;

        Assert.Empty(_processingRuns);
        Assert.Contains("No plugins selected", _viewModel.ErrorMessages);

        refreshCompletion.SetResult(PluginListDiscoveryResult.Completed(["New.esp"]));
        await transition;
    }

    /// <summary>
    /// Verifies that source matching accepts an equivalent normalized directory spelling during projection.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DuringEquivalentSourceProjection_AcceptsConfirmedSelection()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
        _viewModel.DatabasePath = DatabasePath;
        Task? reentrantRun = null;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.GameDirectory))
            {
                reentrantRun = sut.ProcessFormIdsAsync();
            }
        };

        await sut.SelectDetectedDirectoryAsync(Path.Combine(GameDirectory, "Data", "."));
        Assert.NotNull(reentrantRun);
        await reentrantRun;

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(confirmed.Source.DataDirectory, run.GameDirectory);
        Assert.Equal(GameRelease.SkyrimSE, run.GameRelease);
        Assert.Equal(["User.esp"], run.PluginNames);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_UpdateModeOn_CreatesReplaceModeRunRequest()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _viewModel.UpdateMode = true;

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(UpdateMode.ReplacePluginRecords, run.UpdateMode);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_StartsNewRun_ClearsStaleWarnings()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _viewModel.WarningMessages.Add("Stale warning");

        await sut.ProcessFormIdsAsync();

        Assert.Empty(_viewModel.WarningMessages);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_ProcessingRunWarningEvent_AddsWarningMessage()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.EventsToReport.Add(ProcessingRunEvent.Warning("Skipped User.esp"));

        await sut.ProcessFormIdsAsync();

        Assert.Contains("Skipped User.esp", _viewModel.WarningMessages);
        Assert.Empty(_viewModel.ErrorMessages);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_ProcessingRunErrorEvent_AddsErrorMessage()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.EventsToReport.Add(ProcessingRunEvent.Error("Failed User.esp"));

        await sut.ProcessFormIdsAsync();

        Assert.Contains("Failed User.esp", _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_AlreadyProcessing_CancelsCurrentRunAndSetsCancellingState()
    {
        _viewModel.IsProcessing = true;

        var sut = CreateSut();
        await sut.ProcessFormIdsAsync();

        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.True(_viewModel.IsProcessing);
        Assert.Equal("Cancelling...", _viewModel.ProgressStatus);
    }

    [Fact]
    public void Dispose_CalledTwice_CancelsAndDisposesProcessingRunExecutorOnce()
    {
        var sut = CreateSut();

        _pluginListPresentationAdapter.Dispose();
        sut.Dispose();
        sut.Dispose();

        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.Equal(1, _processingRunExecutor.DisposeCallCount);
        Assert.Throws<ObjectDisposedException>(() => _pluginList.Invalidate());
    }

    /// <summary>
    /// Verifies that disposal prevents a late installed-location result from projecting directories or refreshing Plugins.
    /// </summary>
    [Fact]
    public async Task Dispose_DuringInstalledLocationLookup_RetiresLateProjectionAndRefresh()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLookupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns(() =>
            {
                lookupStarted.SetResult();
                allowLookupToFinish.Task.GetAwaiter().GetResult();
                return [@"C:\Games\Fallout4"];
            });
        var sut = CreateSut();
        var selection = sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        await lookupStarted.Task;

        _pluginListPresentationAdapter.Dispose();
        sut.Dispose();
        allowLookupToFinish.SetResult();
        await selection;

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.Empty(_refreshes);
    }

    /// <summary>
    /// Verifies that disposal prevents a late folder-picker failure from publishing an obsolete error.
    /// </summary>
    [Fact]
    public async Task Dispose_DuringFolderPicker_RetiresLateFailureMessage()
    {
        var pickerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowPickerToFinish = new TaskCompletionSource<FileDialogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .Returns(() =>
            {
                pickerStarted.SetResult();
                return allowPickerToFinish.Task;
            });
        var sut = CreateSut();
        var browse = sut.BrowseGameDirectoryAsync();
        await pickerStarted.Task;

        _pluginListPresentationAdapter.Dispose();
        sut.Dispose();
        allowPickerToFinish.SetResult(FileDialogResult.Failure("picker unavailable"));
        await browse;

        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_refreshes);
    }

    /// <summary>
    /// Verifies that a changed Advanced Mode value is projected before same-source Plugin List refresh completes.
    /// </summary>
    [Fact]
    public async Task SetAdvancedModeAsync_ChangedCompleteContext_ProjectsAndRefreshesCurrentSource()
    {
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE)).Returns([GameDirectory]);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _refreshes.Clear();
        var refreshCompletion = new TaskCompletionSource<PluginListDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListDiscovery.Handler = (_, _) => refreshCompletion.Task;

        var refresh = sut.SetAdvancedModeAsync(AdvancedMode.On);

        Assert.True(_viewModel.AdvancedMode);
        Assert.Equal(
            PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory),
            Assert.Single(_refreshes));

        refreshCompletion.SetResult(PluginListDiscoveryResult.Completed([]));
        await refresh;

        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, true);
    }

    /// <summary>
    /// Verifies that incomplete Game Context explicitly invalidates confirmation and its presentation projection.
    /// </summary>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_ClearedSelection_InvalidatesConfirmedPluginList()
    {
        var sut = CreateSut();
        await ConfirmPluginListAsync(sut, ["Old.esp"]);
        Assert.Single(_viewModel.Plugins);

        await sut.SelectDetectedDirectoryAsync(null);

        Assert.Null(_pluginList.Current.Confirmed);
        Assert.IsType<PluginListNoSourceActivity>(_pluginList.Current.Activity);
        Assert.Empty(_viewModel.Plugins);
    }

    /// <summary>
    /// Verifies that bulk commands target complete confirmed membership rather than the filtered projection.
    /// </summary>
    [Fact]
    public async Task SelectAllAndNonePlugins_ActiveFilter_ApplyToCompleteConfirmedMembership()
    {
        var sut = CreateSut();
        await ConfirmPluginListAsync(sut, ["Visible.esp", "Hidden.esp", "AlsoHidden.esp"]);
        _viewModel.PluginFilter = "Visible";
        Assert.Equal("Visible.esp", Assert.Single(_viewModel.FilteredPlugins).Name);

        sut.SelectAllPlugins();

        var selected = Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
        Assert.Equal(["Visible.esp", "Hidden.esp", "AlsoHidden.esp"], selected.SelectedPluginNames);

        sut.SelectNoPlugins();

        Assert.Empty(Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed).SelectedPluginNames);
    }

    private UserWorkflow CreateSut()
    {
        return new UserWorkflow(
            _viewModel,
            _fileDialogService.Object,
            _gameDetectionService.Object,
            _gameLocationService.Object,
            _pluginList,
            _processingRunExecutor);
    }

    /// <summary>
    /// Asserts one canonical discovery source and the confirmed Advanced Mode produced by that refresh.
    /// </summary>
    private void AssertSingleRefresh(string gameDirectory, GameRelease gameRelease, bool advancedMode)
    {
        var source = Assert.Single(_refreshes);
        Assert.Equal(PluginListSource.Create(gameRelease, gameDirectory), source);
        var confirmed = Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
        Assert.Equal(advancedMode ? AdvancedMode.On : AdvancedMode.Off, confirmed.AdvancedMode);
    }

    /// <summary>
    /// Loads deterministic membership through the real Plugin List and returns its confirmation.
    /// </summary>
    private async Task<ConfirmedPluginList> ConfirmPluginListAsync(
        UserWorkflow sut,
        IReadOnlyList<string> pluginNames)
    {
        _pluginListDiscovery.PluginNames = pluginNames;
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns([GameDirectory]);

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        return Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
    }

    /// <summary>
    /// Establishes one selected confirmed Plugin through the supported workflow boundary.
    /// </summary>
    private async Task ConfigureValidPluginProcessingRunAsync(UserWorkflow sut)
    {
        _viewModel.DatabasePath = DatabasePath;
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
    }

    private sealed class RecordingProcessingRunExecutor(List<ProcessingRunRequest> processingRuns)
        : IProcessingRunExecutor
    {
        public int CancelCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public List<ProcessingRunEvent> EventsToReport { get; } = [];

        public Task ExecuteAsync(
            ProcessingRunRequest request,
            IProgress<ProcessingRunEvent>? progress = null)
        {
            processingRuns.Add(request);
            foreach (var runEvent in EventsToReport)
            {
                progress?.Report(runEvent);
            }

            return Task.CompletedTask;
        }

        public void Cancel()
        {
            CancelCallCount++;
        }

        public void Dispose()
        {
            DisposeCallCount++;
        }
    }

    private sealed class RecordingPluginListDiscovery(List<PluginListSource> refreshes) : IPluginListDiscovery
    {
        public Func<PluginListSource, CancellationToken, Task<PluginListDiscoveryResult>>? Handler { get; set; }

        public IReadOnlyList<string> PluginNames { get; set; } = [];

        /// <inheritdoc />
        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            refreshes.Add(source);
            if (Handler is not null)
            {
                return Handler(source, cancellationToken);
            }

            return Task.FromResult(PluginListDiscoveryResult.Completed(PluginNames));
        }
    }
}
