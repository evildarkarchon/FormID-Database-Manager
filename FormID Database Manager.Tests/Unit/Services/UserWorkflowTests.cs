#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.Tests.Fakes;
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
    private readonly GameInstallations _gameInstallations;
    private readonly InMemoryGameInstallationProbe _gameInstallationProbe = new();
    private readonly PluginList _pluginList;
    private readonly RecordingPluginListGameLoadOrders _pluginListGameLoadOrders;
    private readonly PluginListPresentationAdapter _pluginListPresentationAdapter;
    private readonly RecordingProcessingRunExecutor _processingRunExecutor;
    private readonly List<ProcessingRunRequest> _processingRuns = [];
    private readonly List<PluginListSource> _refreshes = [];
    private readonly MainWindowViewModel _viewModel;

    public UserWorkflowTests()
    {
        _viewModel = new MainWindowViewModel(_dispatcher);
        // Game Installation resolution has no interface to mock: the real detection and location members run against
        // whatever layout and install records a test declares on the probe.
        _gameInstallations = new GameInstallations(_gameInstallationProbe);
        _pluginListGameLoadOrders = new RecordingPluginListGameLoadOrders(_refreshes);
        _pluginList = new PluginList(_pluginListGameLoadOrders);
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
        _gameInstallationProbe
            .WithInstalledDirectories(GameRelease.SkyrimSE, @"C:\Old")
            .WithInstalledDirectories(GameRelease.Fallout4, @"C:\Games\Fallout4", @"D:\Games\Fallout4");
        _pluginListGameLoadOrders.PluginNames = ["Old.esp"];
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _pluginListGameLoadOrders.PluginNames = [];
        _refreshes.Clear();
        GateInstalledLocationLookup(GameRelease.Fallout4, lookupStarted, allowLookupToFinish);

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
    /// Verifies that a slow installed-location lookup is placed off the calling thread. The workflow hands control
    /// back while the lookup is still running; a lookup left on the calling thread could not return until it finished,
    /// which is the frozen window this offload exists to prevent.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_SlowInstalledLocationLookup_DoesNotBlockTheCallingThread()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLookupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        GateInstalledLocationLookup(GameRelease.SkyrimSE, lookupStarted, allowLookupToFinish);
        var sut = CreateSut();

        var selection = sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await lookupStarted.Task;

        Assert.False(selection.IsCompleted);

        allowLookupToFinish.SetResult();
        await selection;

        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
    }

    /// <summary>
    /// Verifies that slow detection is placed off the calling thread. The picker result is already available, so
    /// Browse runs straight into detection, yet it still hands control back while probing continues — browsing to a
    /// slow or network directory cannot hang the window.
    /// </summary>
    [Fact]
    public async Task BrowseGameDirectoryAsync_SlowDetection_DoesNotBlockTheCallingThread()
    {
        var detectionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDetectionToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        _gameInstallationProbe
            .WithFile(Path.Combine(GameDirectory, "Data", "Skyrim.esm"))
            .WithFile(Path.Combine(GameDirectory, "SkyrimSE.exe"));
        // Detection probes several paths; the gate opens on the first and is transparent from then on.
        _gameInstallationProbe.BeforeProbe = _ =>
        {
            detectionStarted.TrySetResult();
            allowDetectionToFinish.Task.GetAwaiter().GetResult();
        };
        var sut = CreateSut();

        var browse = sut.BrowseGameDirectoryAsync();
        await detectionStarted.Task;

        Assert.False(browse.IsCompleted);

        allowDetectionToFinish.SetResult();
        await browse;

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, false);
    }

    /// <summary>
    /// Verifies that Advanced Mode becomes authoritative without retiring a current installed-location lookup.
    /// </summary>
    [Fact]
    public async Task SetAdvancedModeAsync_DuringLocationLookup_ProjectsImmediatelyAndRefreshesWithLatestMode()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLookupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Games\Fallout4");
        GateInstalledLocationLookup(GameRelease.Fallout4, lookupStarted, allowLookupToFinish);
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
        var discoveryCompletion = new TaskCompletionSource<AvailablePluginsDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, @"C:\Automatic\Skyrim");
        GateInstalledLocationLookup(GameRelease.SkyrimSE, lookupStarted, allowLookupToFinish);
        _pluginListGameLoadOrders.Handler = (_, _) => discoveryCompletion.Task;
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

        discoveryCompletion.SetResult(
            PluginListGameLoadOrdersStub.LocalAccessFailure("selected release does not match directory"));
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
        // No install records are declared for Fallout4, so the lookup answers with nothing recorded.
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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _refreshes.Clear();

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        // One lookup across both calls: the repeat did not start a second one.
        Assert.Equal([GameRelease.SkyrimSE], _gameInstallationProbe.InstalledDirectoryLookups);
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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, @"D:\Games\Skyrim");

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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, @"D:\Games\Skyrim");
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

        // SkyrimSE has no declared install records, so the gated older lookup answers with nothing recorded.
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\NewFallout");
        GateInstalledLocationLookup(GameRelease.SkyrimSE, olderStarted, allowOlderToFinish);

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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\NewFallout");
        // The module does not swallow a failing lookup, so this reaches the workflow's guard rather than the probe's.
        OnInstalledLocationLookup(GameRelease.SkyrimSE, () =>
        {
            olderStarted.SetResult();
            allowOlderToFail.Task.GetAwaiter().GetResult();
            throw new InvalidOperationException("obsolete lookup failure");
        });
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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        GateInstalledLocationLookup(GameRelease.SkyrimSE, lookupStarted, allowLookupToFinish);
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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Existing", @"D:\Suggested");
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
        Assert.Empty(_gameInstallationProbe.ProbedPaths);
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
        _gameInstallationProbe.WithFile(Path.Combine(latestDirectory, "Data", "Fallout4.esm"));
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
        Assert.False(_gameInstallationProbe.ProbedAnythingUnder(GameDirectory));
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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, latestDirectory);
        var sut = CreateSut();

        var browse = sut.BrowseGameDirectoryAsync();
        var releaseSelection = sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        pickerCompletion.SetResult(FileDialogResult.Success(GameDirectory));
        await Task.WhenAll(browse, releaseSelection);

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Equal(latestDirectory, _viewModel.GameDirectory);
        Assert.Equal([latestDirectory], _viewModel.DetectedDirectories);
        AssertSingleRefresh(latestDirectory, GameRelease.Fallout4, false);
        Assert.Empty(_gameInstallationProbe.ProbedPaths);
    }

    /// <summary>
    /// Verifies that a newer explicit directory suppresses an older picker failure and its error message.
    /// </summary>
    /// <returns>A task that completes after the directory intent and controlled picker settle.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_PickerFailureSupersededByDirectorySelection_IsSilent()
    {
        const string latestDirectory = @"D:\Games\Skyrim";
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, latestDirectory);
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
        _gameInstallationProbe
            .WithFile(Path.Combine(GameDirectory, "Data", "Skyrim.esm"))
            .WithFile(Path.Combine(GameDirectory, "SkyrimSE.exe"));

        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.Empty(_gameInstallationProbe.InstalledDirectoryLookups);
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
        _gameInstallationProbe
            .WithFile(Path.Combine(GameDirectory, "Data", "Skyrim.esm"))
            .WithFile(Path.Combine(GameDirectory, "SkyrimSE.exe"));
        // Detection probes several paths; the gate opens on the first and is transparent from then on.
        _gameInstallationProbe.BeforeProbe = _ =>
        {
            detectionStarted.TrySetResult();
            allowDetectionToFinish.Task.GetAwaiter().GetResult();
        };
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Games\Fallout4");
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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, @"D:\Games\Skyrim");
        _pluginListGameLoadOrders.PluginNames = ["PreviouslyConfirmed.esp"];
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.NotNull(_pluginList.Current.Confirmed);
        _refreshes.Clear();
        _pluginListGameLoadOrders.Handler = (_, _) =>
            Task.FromResult(PluginListGameLoadOrdersStub.LocalAccessFailure("directory mismatch"));
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal([GameDirectory, @"D:\Games\Skyrim"], _viewModel.DetectedDirectories);
        Assert.Null(_pluginList.Current.Confirmed);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory), Assert.Single(_refreshes));
        Assert.Contains("Failed to load plugins: directory mismatch", _viewModel.ErrorMessages);
        Assert.Empty(_gameInstallationProbe.ProbedPaths);
        Assert.Equal([GameRelease.SkyrimSE], _gameInstallationProbe.InstalledDirectoryLookups);
    }

    /// <summary>
    /// Verifies that current detection failure retains the chosen path, clears confirmation, and presents guidance.
    /// </summary>
    /// <returns>A task that completes after detection failure is presented.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_SelectedDirectoryWithoutDetectableGame_RecordsWorkflowError()
    {
        _pluginListGameLoadOrders.PluginNames = ["PreviouslyConfirmed.esp"];
        await _pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        Assert.NotNull(_pluginList.Current.Confirmed);
        _refreshes.Clear();
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        // No layout is declared, so detection finds no known game master — its one reason to return null.

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
    /// Verifies that a path detection cannot use at all is reported as the path problem it is, rather than as a
    /// missing game the user is asked to supply from the dropdown.
    /// </summary>
    /// <returns>A task that completes after the malformed path is presented.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_MalformedSelectedDirectory_RecordsPathErrorRatherThanDetectionGuidance()
    {
        const string malformedDirectory = "C:\\Games\\Sky\0rim";
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(malformedDirectory));

        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();

        Assert.Null(_viewModel.SelectedGame);
        // The chosen path stays on display beside the error, exactly as it does for a game-less directory: the user
        // is being told what is wrong with the path they picked, so the path has to remain visible.
        Assert.Equal(malformedDirectory, _viewModel.GameDirectory);
        Assert.DoesNotContain(
            "Could not detect game from directory. Please select a game from the dropdown.",
            _viewModel.ErrorMessages);
        Assert.Contains(
            _viewModel.ErrorMessages,
            message => message.StartsWith("Could not read the selected directory:", StringComparison.Ordinal));
        Assert.Empty(_refreshes);
    }

    /// <summary>
    /// Verifies that current picker failure leaves the existing Game Context unchanged and reports the failure.
    /// </summary>
    /// <returns>A task that completes after picker failure is presented.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_CurrentPickerFailure_LeavesSnapshotUnchangedAndRecordsError()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Existing", @"D:\Suggested");
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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Existing");
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
        Assert.False(_viewModel.IsProgressVisible);
        Assert.Equal("Process FormIDs", _viewModel.ProcessButtonText);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_PluginIngestionWithoutGameDirectory_RecordsValidationMessage()
    {
        // Nothing is declared for SkyrimSE, so the release stays selected without a directory.
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        await sut.ProcessFormIdsAsync();

        Assert.Contains("Game directory must be specified when processing plugins", _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_PluginIngestionWithoutSelectedPlugins_RecordsValidationMessage()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
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
        var pluginList = new PluginList(_pluginListGameLoadOrders);
        var processingRuns = new List<ProcessingRunRequest>();
        var executor = new RecordingProcessingRunExecutor(processingRuns);
        using var sut = new UserWorkflow(
            viewModel,
            _fileDialogService.Object,
            _gameInstallations,
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

        var refreshCompletion = new TaskCompletionSource<AvailablePluginsDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListGameLoadOrders.Handler = (_, _) => refreshCompletion.Task;
        var refresh = sut.SetAdvancedModeAsync(AdvancedMode.On);

        await sut.ProcessFormIdsAsync();
        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));

        refreshCompletion.SetResult(PluginListGameLoadOrdersStub.Discovered(
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

        var refreshCompletion = new TaskCompletionSource<AvailablePluginsDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListGameLoadOrders.Handler = (_, _) => refreshCompletion.Task;
        var transition = sut.SelectDetectedDirectoryAsync(@"D:\Games\Fallout4");
        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(@"D:\Games\Fallout4", _viewModel.GameDirectory);
        Assert.Null(_pluginList.Current.Confirmed);

        await sut.ProcessFormIdsAsync();

        Assert.Empty(_processingRuns);
        Assert.Contains("No plugins selected", _viewModel.ErrorMessages);

        refreshCompletion.SetResult(PluginListGameLoadOrdersStub.Discovered(["New.esp"]));
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
        var refreshCompletion = new TaskCompletionSource<AvailablePluginsDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListGameLoadOrders.Handler = (_, _) => refreshCompletion.Task;
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

        refreshCompletion.SetResult(PluginListGameLoadOrdersStub.Discovered(["New.esp"]));
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

    /// <summary>
    ///     Verifies that the Dry Run toggle reaches the run request, for both request kinds.
    /// </summary>
    /// <remarks>
    ///     Issue #67. The toggle is what makes a dry run reachable at all: without it the plan a run can produce is
    ///     something no user can ask for.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_DryRunOn_CreatesADryRunRequest()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _viewModel.DryRun = true;

        await sut.ProcessFormIdsAsync();

        Assert.True(Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns)).DryRun);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_DryRunOnWithAFormIdTextFile_CreatesADryRunRequest()
    {
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _viewModel.FormIdListPath = @"C:\Imports\formids.txt";
        _viewModel.DryRun = true;

        await sut.ProcessFormIdsAsync();

        Assert.True(Assert.IsType<FormIdTextProcessingRunRequest>(Assert.Single(_processingRuns)).DryRun);
    }

    /// <summary>
    ///     Verifies that a dry run leaves an empty database path empty.
    /// </summary>
    /// <remarks>
    ///     A dry run opens no FormID Record Store, so defaulting a path for it would leave one the user never chose in
    ///     the box after a run that deliberately wrote nothing.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_DryRunWithEmptyDatabasePath_DoesNotChooseADefaultPath()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
        _viewModel.DatabasePath = string.Empty;
        _viewModel.DryRun = true;

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Empty(_viewModel.DatabasePath);
        Assert.Empty(run.DatabasePath);
    }

    /// <summary>
    ///     Verifies the workflow writes a dry run's plan to the information messages the user reads.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DryRunOutcome_AddsTheRenderedPlanToTheInformationMessages()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _viewModel.DryRun = true;
        _processingRunExecutor.Outcome = new PlannedRunOutcome(new PluginRunPlan(new PluginIngestionPlan([
            new PlannedPluginIngestion("User.esp"),
            new PlannedPluginSkip("Absent.esp", PlannedSkipReason.NotPresentInLoadOrder)
        ])));

        await sut.ProcessFormIdsAsync();

        // The last message rather than the only one: confirming a Plugin List already reported what it loaded, and a
        // run does not clear the information list the way it clears warnings and errors.
        Assert.Equal(
            "Dry run: 1 would be ingested, 1 would be skipped, and 0 would fail." + Environment.NewLine +
            "Would ingest User.esp" + Environment.NewLine +
            "Would skip Absent.esp: not present in the load order",
            _viewModel.InformationMessages[^1]);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Empty(_viewModel.ErrorMessages);
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

    /// <summary>
    ///     Verifies the workflow performs the writes a warned run's rendered report implies, and no others.
    /// </summary>
    /// <remarks>
    ///     A run no longer says a word about how it ended: the executor returns the outcome and the workflow renders
    ///     it, so this asserts the writes rather than the routing of a warning event that no longer exists.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_RunOutcomeWithASkippedPlugin_AddsTheRenderedWarningMessage()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.Outcome = CreatePluginRunOutcome(
            new SkippedPlugin("User.esp", SkippedPluginReason.ZeroFormIdRecords));

        await sut.ProcessFormIdsAsync();

        Assert.Equal(
            [
                "1 processing warning." + Environment.NewLine +
                "User.esp: User.esp produced zero FormID records."
            ],
            _viewModel.WarningMessages);
        Assert.Empty(_viewModel.ErrorMessages);
    }

    /// <summary>
    ///     Verifies the workflow performs the writes a failed run's rendered report implies, and no others.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_RunOutcomeWithAFailedPlugin_AddsTheRenderedErrorMessage()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.Outcome = CreatePluginRunOutcome(
            new FailedPlugin(
                "User.esp",
                new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header.")));

        await sut.ProcessFormIdsAsync();

        Assert.Equal(
            [
                "1 failed plugin." + Environment.NewLine +
                "User.esp: Error opening User.esp: Invalid plugin header."
            ],
            _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
    }

    /// <summary>
    ///     Verifies a run stopped by an unresolvable master reaches the user as the failure's own message, without the
    ///     generic processing-error prefix that every other terminal failure gets.
    /// </summary>
    /// <remarks>
    ///     Issue #52. The message already names the missing master and what to do about it, so burying it behind
    ///     "Error processing FormIDs" would waste the one piece of information the fix exists to deliver (ADR-0006).
    ///     The generic case below is asserted alongside it so this stays a distinction rather than a coincidence.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_UnresolvableMaster_ShowsTheFailureMessageUnwrapped()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        var failure = new UnresolvableMasterException("User.esp", "Starfield.esm");
        // The real executor reports this status for any terminal failure before rethrowing, so it is reproduced here:
        // what the assertion needs to show is that it stays out of the error list rather than doubling the message.
        _processingRunExecutor.ProgressToReport.Add(
            new ProcessingRunFailure(failure.Message));
        _processingRunExecutor.ExecuteFailure = failure;
        // Recorded after the setup that confirmed the Plugin List, so the sequence below is the run's alone.
        var projectedStatuses = RecordProjectedStatuses();

        await sut.ProcessFormIdsAsync();

        Assert.Equal([failure.Message], _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.False(_viewModel.IsProgressVisible);
        // Cleared by the workflow's own finally, so the prefixed status is transient and never the lasting report.
        Assert.Equal(string.Empty, _viewModel.ProgressStatus);
        // The complete sequence rather than a containment check: the prefixed status appearing once on the transient
        // channel and nowhere else is the whole distinction this test draws.
        Assert.Equal(
            ["Initializing...", $"Error during processing: {failure.Message}", string.Empty],
            projectedStatuses);
    }

    /// <summary>
    ///     Verifies where each part of a rendered run report lands when one run produced both: the warning message in
    ///     the warning list, the failure message in the error list, and the completion status only on the transient
    ///     progress channel.
    /// </summary>
    /// <remarks>
    ///     The separate warned-run and failed-run tests above cannot show this, because each has only one part to
    ///     place. Routing is the workflow's own responsibility — the wording of all three parts is pinned by
    ///     <see cref="ProcessingRunPresentationTests" /> — so what is asserted here is which list each part reached,
    ///     and that the completion status reached none of them.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_RunOutcomeWithWarningsAndFailures_RoutesEachRenderedPartToItsOwnList()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.Outcome = CreatePluginRunOutcome(
            new IngestedPlugin("User.esp", 1, new ProcessingWarning(1, ["Recoverable issue"])),
            new FailedPlugin(
                "Bad.esp",
                new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header.")));
        var projectedStatuses = RecordProjectedStatuses();

        await sut.ProcessFormIdsAsync();

        Assert.Equal(
            [
                "1 processing warning." + Environment.NewLine +
                "User.esp: 1 recoverable record issue. Recoverable issue"
            ],
            _viewModel.WarningMessages);
        Assert.Equal(
            [
                "1 failed plugin." + Environment.NewLine +
                "Bad.esp: Error opening Bad.esp: Invalid plugin header."
            ],
            _viewModel.ErrorMessages);
        Assert.Equal(
            [
                "Initializing...",
                "Processing completed with failures: 1 ingested, 0 skipped, and 1 failed Plugins.",
                string.Empty
            ],
            projectedStatuses);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_UnexpectedRunFailure_ShowsTheGenericProcessingErrorMessage()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.ExecuteFailure = new InvalidOperationException("store unavailable");

        await sut.ProcessFormIdsAsync();

        Assert.Equal(["Error processing FormIDs: store unavailable"], _viewModel.ErrorMessages);
        Assert.False(_viewModel.IsProgressVisible);
    }

    /// <summary>
    ///     Verifies a press arriving while a run is in flight cancels that run instead of starting a second one, and
    ///     that the acknowledgement reaches the progress channel while the run is still the activity that owns it.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_PressedWhileRunActive_CancelsThatRunAndShowsCancellingStatus()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.ProgressToReport.Add(new IngestingPlugin("User.esp", 2, 5));
        var statusDuringCancellation = string.Empty;
        var buttonTextDuringRun = string.Empty;
        // The run's own progress report notifies synchronously while the run is still in flight, which is how this
        // test acts mid-run without the recording executor needing a gate.
        OnRunStatus("Ingesting plugin 2 of 5: User.esp", () =>
        {
            buttonTextDuringRun = _viewModel.ProcessButtonText;
            // The second press: the workflow's own run state, not the ViewModel, decides this means cancel.
            sut.ProcessFormIdsAsync().GetAwaiter().GetResult();
            statusDuringCancellation = _viewModel.ProgressStatus;
        });

        await sut.ProcessFormIdsAsync();

        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.Single(_processingRuns);
        Assert.Equal("Cancel Processing", buttonTextDuringRun);
        Assert.Equal("Cancelling...", statusDuringCancellation);
        Assert.Equal("Process FormIDs", _viewModel.ProcessButtonText);
    }

    /// <summary>
    ///     Verifies that cancelling an active Processing Run leaves a lasting acknowledgement in the information
    ///     messages, and that the acknowledgement never reaches the transient progress channel at all.
    /// </summary>
    /// <remarks>
    ///     Issue #60. The notice used to be written to the progress channel by this workflow's cancellation handler and
    ///     then erased by the run's own cleanup one step later, so the user never saw it and nothing covered the case.
    ///     Both halves are asserted here: that it lands in the message lists where the other terminal facts about a run
    ///     go, and that it survives the cleanup that used to erase it.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_CancelledMidRun_PutsTheAcknowledgementInTheInformationMessages()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.ProgressToReport.Add(new IngestingPlugin("User.esp", 2, 5));
        var projectedStatuses = RecordProjectedStatuses();
        // The second press is the cancellation intent, delivered mid-run from the run's own progress notification.
        OnRunStatus("Ingesting plugin 2 of 5: User.esp", () =>
        {
            sut.ProcessFormIdsAsync().GetAwaiter().GetResult();
            // Armed only once the press has reached the executor, so the run ends as cancelled because it was
            // cancelled: the real executor returns this outcome for the cancellation it accepted, and never for one
            // it was not asked for.
            _processingRunExecutor.Outcome = new CancelledRunOutcome();
        });

        await sut.ProcessFormIdsAsync();

        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        // The Plugin List refresh that set this run up leaves its own information message ahead of the acknowledgement.
        Assert.Contains("Processing cancelled by user.", _viewModel.InformationMessages);
        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
        // The run's cleanup hands the channel back, and can no longer erase an acknowledgement that was never on it.
        Assert.Equal(string.Empty, _viewModel.ProgressStatus);
        Assert.False(_viewModel.IsProgressVisible);
        // The complete sequence rather than a containment check: showing that the acknowledgement never reached a
        // channel that erases itself means stating everything that did reach it, not just the one string it did not.
        Assert.Equal(
            ["Initializing...", "Ingesting plugin 2 of 5: User.esp", "Cancelling...", string.Empty],
            projectedStatuses);
    }

    /// <summary>
    ///     Verifies the window this workflow owns run state to close: a press landing after validation has begun but
    ///     before the executor has been handed the run cancels rather than launching a second run.
    /// </summary>
    /// <remarks>
    ///     The second press is delivered from the notification raised when the workflow defaults the database path,
    ///     which happens inside that window. The executor's cancellation source does not exist yet at that point, so
    ///     an executor-owned run flag could not have answered this press correctly.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_PressedBeforeTheRunReachesTheExecutor_DoesNotLaunchASecondRun()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
        var secondPressed = false;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MainWindowViewModel.DatabasePath) || secondPressed)
            {
                return;
            }

            secondPressed = true;
            sut.ProcessFormIdsAsync().GetAwaiter().GetResult();
        };

        await sut.ProcessFormIdsAsync();

        Assert.True(secondPressed);
        Assert.Single(_processingRuns);
        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
    }

    /// <summary>
    ///     Verifies the collision this projection exists to arbitrate: a Plugin List refresh started during a
    ///     Processing Run never takes the progress channel off the run, and the run's report survives the refresh.
    /// </summary>
    /// <param name="refreshTrigger">The user input used to trigger the mid-run refresh.</param>
    [Theory]
    [InlineData(RefreshTrigger.AdvancedMode)]
    [InlineData(RefreshTrigger.GameRelease)]
    [InlineData(RefreshTrigger.GameDirectory)]
    public async Task ProcessFormIdsAsync_PluginListRefreshDuringRun_KeepsTheRunOnTheProgressChannel(
        RefreshTrigger refreshTrigger)
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Games\Fallout4");
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        _processingRunExecutor.ProgressToReport.Add(new IngestingPlugin("User.esp", 2, 5));
        var projectedStatuses = RecordProjectedStatuses();
        var statusAfterRefresh = string.Empty;
        var valueAfterRefresh = 0d;
        // The refresh is triggered from the run's own progress notification, so it genuinely overlaps a run in flight.
        OnRunStatus("Ingesting plugin 2 of 5: User.esp", () =>
        {
            TriggerRefreshAsync(sut, refreshTrigger).GetAwaiter().GetResult();
            statusAfterRefresh = _viewModel.ProgressStatus;
            valueAfterRefresh = _viewModel.ProgressValue;
        });

        await sut.ProcessFormIdsAsync();

        Assert.Equal("Ingesting plugin 2 of 5: User.esp", statusAfterRefresh);
        // The second of five selected Plugins, which the run's progress renders as 40%.
        Assert.Equal(40, valueAfterRefresh);
        Assert.DoesNotContain(projectedStatuses, status => status.StartsWith("Scanning", StringComparison.Ordinal));
        // The run's own reports are the only thing the channel ever showed, and it ends empty.
        Assert.Equal(["Initializing...", "Ingesting plugin 2 of 5: User.esp", string.Empty], projectedStatuses);
    }

    /// <summary>
    ///     The user inputs that each start a Plugin List refresh, and stay live during a Processing Run because the
    ///     window disables nothing.
    /// </summary>
    public enum RefreshTrigger
    {
        /// <summary>Toggling Advanced Mode.</summary>
        AdvancedMode,

        /// <summary>Choosing a different GameRelease.</summary>
        GameRelease,

        /// <summary>Browsing to a game directory.</summary>
        GameDirectory
    }

    /// <summary>
    ///     Runs a handler once, the first time the progress channel shows the given run status.
    /// </summary>
    /// <param name="status">The run status text to wait for.</param>
    /// <param name="handler">The work to run while that run is still in flight.</param>
    /// <remarks>
    ///     A Processing Run reports its progress synchronously through the workflow, so the resulting notification is
    ///     raised from inside the run. That makes this the seam for acting mid-run without gating the executor. The
    ///     handler runs once because its own work changes the status again and would otherwise recurse.
    /// </remarks>
    private void OnRunStatus(string status, Action handler)
    {
        var handled = false;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (handled ||
                args.PropertyName != nameof(MainWindowViewModel.ProgressStatus) ||
                _viewModel.ProgressStatus != status)
            {
                return;
            }

            handled = true;
            handler();
        };
    }

    /// <summary>
    ///     Starts recording every status the transient progress channel shows from this point on.
    /// </summary>
    /// <returns>The list that receives the projected statuses, in the order they were shown.</returns>
    /// <remarks>
    ///     Attached after a test's setup rather than in the fixture, so a Plugin List refresh that ran while the run
    ///     was being configured does not land in a sequence that is supposed to be the run's alone.
    /// </remarks>
    private List<string> RecordProjectedStatuses()
    {
        var projectedStatuses = new List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.ProgressStatus))
            {
                projectedStatuses.Add(_viewModel.ProgressStatus);
            }
        };

        return projectedStatuses;
    }

    /// <summary>
    ///     Triggers a Plugin List refresh through the requested user input.
    /// </summary>
    /// <param name="sut">The workflow under test.</param>
    /// <param name="refreshTrigger">The input to exercise.</param>
    private static Task TriggerRefreshAsync(UserWorkflow sut, RefreshTrigger refreshTrigger)
    {
        return refreshTrigger switch
        {
            RefreshTrigger.AdvancedMode => sut.SetAdvancedModeAsync(AdvancedMode.On),
            RefreshTrigger.GameRelease => sut.SelectGameReleaseAsync(GameRelease.Fallout4),
            RefreshTrigger.GameDirectory => sut.BrowseGameDirectoryAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(refreshTrigger), refreshTrigger, "Unsupported trigger.")
        };
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
    /// Verifies that a throwing run cancellation still retires every owned collaborator, and that a later cleanup
    /// failure cannot replace the cancellation failure the caller observes.
    /// </summary>
    [Fact]
    public void Dispose_CancellationCallbackThrows_StillDisposesCollaboratorsAndKeepsPrimaryFailure()
    {
        var callbackFailure = new InvalidOperationException("cancellation callback failed");
        _processingRunExecutor.CancelFailure = new AggregateException(callbackFailure);
        _processingRunExecutor.DisposeFailure = new InvalidOperationException("executor cleanup failed");
        var sut = CreateSut();

        _pluginListPresentationAdapter.Dispose();
        var thrown = Assert.Throws<AggregateException>(() => sut.Dispose());

        Assert.Same(callbackFailure, Assert.Single(thrown.InnerExceptions));
        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.Equal(1, _processingRunExecutor.DisposeCallCount);
        Assert.Throws<ObjectDisposedException>(() => _pluginList.Invalidate());
    }

    /// <summary>
    /// Verifies that a cleanup failure with no other failure in flight still propagates and does not stop the
    /// remaining collaborator from being retired.
    /// </summary>
    [Fact]
    public void Dispose_StandaloneCleanupFailure_PropagatesAndStillRetiresPluginList()
    {
        var disposalFailure = new InvalidOperationException("executor cleanup failed");
        _processingRunExecutor.DisposeFailure = disposalFailure;
        var sut = CreateSut();

        _pluginListPresentationAdapter.Dispose();
        var thrown = Assert.Throws<InvalidOperationException>(() => sut.Dispose());

        Assert.Same(disposalFailure, thrown);
        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Games\Fallout4");
        GateInstalledLocationLookup(GameRelease.Fallout4, lookupStarted, allowLookupToFinish);
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
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _refreshes.Clear();
        var refreshCompletion = new TaskCompletionSource<AvailablePluginsDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListGameLoadOrders.Handler = (_, _) => refreshCompletion.Task;

        var refresh = sut.SetAdvancedModeAsync(AdvancedMode.On);

        Assert.True(_viewModel.AdvancedMode);
        Assert.Equal(
            PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory),
            Assert.Single(_refreshes));

        refreshCompletion.SetResult(PluginListGameLoadOrdersStub.Discovered([]));
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
        Assert.IsType<PluginListNoSourceState>(_pluginList.Current);
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
            _gameInstallations,
            _pluginList,
            _processingRunExecutor);
    }

    /// <summary>
    /// Holds one GameRelease's installed-location lookup open, so a test can act while it is still in flight.
    /// </summary>
    /// <param name="release">The GameRelease whose lookup is gated; every other release answers immediately.</param>
    /// <param name="started">Signalled once the gated lookup has begun.</param>
    /// <param name="allowToFinish">Completed by the test to let the gated lookup return.</param>
    private void GateInstalledLocationLookup(
        GameRelease release,
        TaskCompletionSource started,
        TaskCompletionSource allowToFinish)
    {
        OnInstalledLocationLookup(release, () =>
        {
            started.SetResult();
            allowToFinish.Task.GetAwaiter().GetResult();
        });
    }

    /// <summary>
    /// Runs a handler when one GameRelease's installed-location lookup begins.
    /// </summary>
    /// <param name="release">The GameRelease whose lookup the handler responds to.</param>
    /// <param name="handler">The work to run inside that lookup, which may block or throw.</param>
    private void OnInstalledLocationLookup(GameRelease release, Action handler)
    {
        _gameInstallationProbe.BeforeInstalledDirectoriesLookup = lookedUpRelease =>
        {
            if (lookedUpRelease == release)
            {
                handler();
            }
        };
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
        _pluginListGameLoadOrders.PluginNames = pluginNames;
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        return Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
    }

    /// <summary>
    ///     Builds a selected-Plugin outcome whose report agrees with the outcomes it is given.
    /// </summary>
    /// <param name="outcomes">One outcome per selected Plugin, in selection order.</param>
    /// <returns>A completed selected-Plugin outcome carrying that report.</returns>
    /// <remarks>
    ///     The ingestion request is rebuilt here rather than taken from the run, because a report validates its
    ///     outcomes against the selection they came from and the recording executor never builds one of its own.
    /// </remarks>
    private static PluginRunOutcome CreatePluginRunOutcome(params PluginIngestionOutcome[] outcomes)
    {
        var request = new SelectedPluginIngestionRequest(
            GameDirectory,
            GameRelease.SkyrimSE,
            outcomes.Select(static outcome => outcome.PluginName),
            UpdateMode.Append);

        return new PluginRunOutcome(new PluginIngestionReport(request, outcomes));
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

        /// <summary>
        ///     The failure raised by <see cref="Cancel" />, standing in for a registered run cancellation callback
        ///     that throws out of the real executor's cancellation source.
        /// </summary>
        public Exception? CancelFailure { get; set; }

        /// <summary>
        ///     The failure raised by <see cref="Dispose" />, standing in for an executor whose own cleanup fails.
        /// </summary>
        public Exception? DisposeFailure { get; set; }

        /// <summary>
        ///     The failure raised by <see cref="ExecuteAsync" /> after any configured events are reported, standing in
        ///     for a run that ends in a terminal failure rather than an outcome.
        /// </summary>
        public Exception? ExecuteFailure { get; set; }

        /// <summary>
        ///     How the run ends when it does not fail.
        /// </summary>
        /// <remarks>
        ///     The default is a dry run of nothing, which renders no message and no status at all, so a test that is
        ///     not about how a run ended sees only the reports it scripted for itself.
        /// </remarks>
        public ProcessingRunOutcome Outcome { get; set; } =
            new PlannedRunOutcome(new PluginRunPlan(new PluginIngestionPlan([])));

        public List<ProcessingRunProgress> ProgressToReport { get; } = [];

        public Task<ProcessingRunOutcome> ExecuteAsync(
            ProcessingRunRequest request,
            IProgress<ProcessingRunProgress>? progress = null)
        {
            processingRuns.Add(request);
            foreach (var runEvent in ProgressToReport)
            {
                progress?.Report(runEvent);
            }

            return ExecuteFailure is { } failure
                ? Task.FromException<ProcessingRunOutcome>(failure)
                : Task.FromResult(Outcome);
        }

        public void Cancel()
        {
            CancelCallCount++;
            if (CancelFailure is { } failure)
            {
                throw failure;
            }
        }

        public void Dispose()
        {
            DisposeCallCount++;
            if (DisposeFailure is { } failure)
            {
                throw failure;
            }
        }
    }

    private sealed class RecordingPluginListGameLoadOrders(List<PluginListSource> refreshes)
        : PluginListGameLoadOrdersStub
    {
        public Func<PluginListSource, CancellationToken, Task<AvailablePluginsDiscoveryResult>>? Handler { get; set; }

        public IReadOnlyList<string> PluginNames { get; set; } = [];

        /// <inheritdoc />
        public override Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = PluginListSource.Create(gameRelease, canonicalDataDirectory);
            refreshes.Add(source);
            if (Handler is not null)
            {
                return Handler(source, cancellationToken);
            }

            return Task.FromResult(Discovered(PluginNames));
        }
    }
}
