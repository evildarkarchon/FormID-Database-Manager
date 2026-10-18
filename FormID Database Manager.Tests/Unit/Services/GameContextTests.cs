#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.Tests.Fakes;
using FormID_Database_Manager.ViewModels;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
/// Drives the Game Context through its own interface over a real Plugin List, an in-memory installation probe, a
/// mocked picker and a real ViewModel, and asserts only what a caller can observe: the ViewModel projection and
/// messages, the Plugin List's state, and which sources the Plugin List was asked to discover.
/// </summary>
/// <remarks>
/// No Plugin List presentation adapter is attached here, because the Game Context does not own one. A discovery
/// failure is therefore observed as the Plugin List's failed state rather than as the adapter's rendered message.
/// </remarks>
public sealed class GameContextTests : IDisposable
{
    private const string GameDirectory = @"C:\Games\Skyrim";

    private readonly Mock<IFileDialogService> _fileDialogService = new();
    private readonly InMemoryGameInstallationProbe _gameInstallationProbe = new();
    private readonly MainWindowViewModel _viewModel = new(new SynchronousThreadDispatcher());
    private ScriptedGameLoadOrders _loadOrders = ScriptedGameLoadOrders.Fixed();
    private PluginList? _pluginList;
    private int _refreshMark;

    /// <summary>
    /// Gets the Plugin List the Game Context under test borrows.
    /// </summary>
    private PluginList PluginList =>
        _pluginList ?? throw new InvalidOperationException("Create the Game Context before reading its Plugin List.");

    /// <summary>
    /// Gets the sources the Plugin List was asked to discover since the last <see cref="ForgetRefreshes" />.
    /// </summary>
    private IReadOnlyList<PluginListSource> Refreshes => _loadOrders.DiscoveredSources.Skip(_refreshMark).ToArray();

    /// <summary>
    /// Disposes the Plugin List, which the Game Context borrows and so never disposes itself.
    /// </summary>
    public void Dispose()
    {
        _pluginList?.Dispose();
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
        var sut = CreateSut(ScriptedGameLoadOrders.Sequenced(
            ScriptedDiscovery.Completes("Old.esp"),
            ScriptedDiscovery.Completes()));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.NotNull(PluginList.Current.Confirmed);
        ForgetRefreshes();
        GateInstalledLocationLookup(GameRelease.Fallout4, lookupStarted, allowLookupToFinish);

        var selection = sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        await lookupStarted.Task;

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.IsType<PluginListNoSourceState>(PluginList.Current);

        allowLookupToFinish.SetResult();
        await selection;

        Assert.Equal(@"C:\Games\Fallout4", _viewModel.GameDirectory);
        Assert.Equal([@"C:\Games\Fallout4", @"D:\Games\Fallout4"], _viewModel.DetectedDirectories.ToArray());
        Assert.Empty(Assert.IsType<ConfirmedPluginList>(PluginList.Current.Confirmed).Entries);
        AssertSingleRefresh(@"C:\Games\Fallout4", GameRelease.Fallout4, AdvancedMode.Off);
    }

    /// <summary>
    /// Verifies that a slow installed-location lookup is placed off the calling thread. The Game Context hands control
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
        Assert.Equal(GameRelease.Fallout4, sut.Current.SelectedGameRelease);
        Assert.Null(sut.Current.SelectedGameDirectory);
        Assert.Contains(
            "No installed locations found for Fallout4. Use Browse to select a directory.",
            _viewModel.InformationMessages);
        Assert.Empty(Refreshes);
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
        ForgetRefreshes();

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        // One lookup across both calls: the repeat did not start a second one.
        Assert.Equal([GameRelease.SkyrimSE], _gameInstallationProbe.InstalledDirectoryLookups);
        Assert.Empty(Refreshes);
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
        Assert.Equal(GameRelease.SkyrimSE, sut.Current.SelectedGameRelease);
        Assert.Equal(GameDirectory, sut.Current.SelectedGameDirectory);
        Assert.Equal([GameDirectory], sut.Current.AvailableDirectories);
        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, AdvancedMode.Off);
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
        AssertSingleRefresh(@"C:\NewFallout", GameRelease.Fallout4, AdvancedMode.Off);
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
        // The module does not swallow a failing lookup, so this reaches the Game Context's guard rather than the probe's.
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
        AssertSingleRefresh(@"C:\NewFallout", GameRelease.Fallout4, AdvancedMode.Off);
        Assert.DoesNotContain(_viewModel.ErrorMessages, message => message.Contains("obsolete", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that a current installed-location failure is not swallowed: it propagates to the caller, which in the
    /// shell is the final unexpected-error boundary.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_CurrentLookupFailure_Propagates()
    {
        OnInstalledLocationLookup(GameRelease.SkyrimSE, () => throw new InvalidOperationException("lookup failed"));
        var sut = CreateSut();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SelectGameReleaseAsync(GameRelease.SkyrimSE));

        Assert.Equal("lookup failed", thrown.Message);
        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Empty(Refreshes);
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

        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, AdvancedMode.Off);
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
        ForgetRefreshes();

        await sut.SelectDetectedDirectoryAsync(@"D:\Games\Skyrim");

        Assert.Equal(@"D:\Games\Skyrim", _viewModel.GameDirectory);
        Assert.Equal([GameDirectory, @"D:\Games\Skyrim"], _viewModel.DetectedDirectories);
        Assert.Equal(@"D:\Games\Skyrim", sut.Current.SelectedGameDirectory);
        AssertSingleRefresh(@"D:\Games\Skyrim", GameRelease.SkyrimSE, AdvancedMode.Off);
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
        // Held, so the manual directory's discovery is still in flight when the older lookup finishes, and only then
        // ends in an expected local-access failure.
        var discovery = new HeldDiscovery(
            PluginListGameLoadOrdersStub.LocalAccessFailure("selected release does not match directory"));
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, @"C:\Automatic\Skyrim");
        GateInstalledLocationLookup(GameRelease.SkyrimSE, lookupStarted, allowLookupToFinish);
        var sut = CreateSut(ScriptedGameLoadOrders.Sequenced(discovery));

        var releaseSelection = sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await lookupStarted.Task;

        var directorySelection = sut.SelectDetectedDirectoryAsync(manualDirectory);

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(manualDirectory, _viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.Equal(@"D:\Games\Skyrim\Data", Assert.Single(Refreshes).DataDirectory);

        allowLookupToFinish.SetResult();
        await releaseSelection;

        Assert.Equal(manualDirectory, _viewModel.GameDirectory);
        Assert.Single(Refreshes);

        discovery.Release();
        await directorySelection;

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(manualDirectory, _viewModel.GameDirectory);
        var failed = Assert.IsType<PluginListFailedState>(PluginList.Current);
        Assert.Null(failed.Confirmed);
        Assert.Equal("selected release does not match directory", failed.ErrorMessage);
    }

    /// <summary>
    /// Verifies that incomplete Game Context explicitly invalidates the confirmed Plugin List.
    /// </summary>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_ClearedSelection_InvalidatesConfirmedPluginList()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("Old.esp"));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.NotNull(PluginList.Current.Confirmed);

        await sut.SelectDetectedDirectoryAsync(null);

        Assert.Null(PluginList.Current.Confirmed);
        Assert.IsType<PluginListNoSourceState>(PluginList.Current);
        Assert.Empty(_viewModel.GameDirectory);
        Assert.Null(sut.Current.SelectedGameDirectory);
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
        Assert.Equal(AdvancedMode.On, sut.Current.AdvancedMode);
        Assert.Empty(Refreshes);
        Assert.Null(PluginList.Current.Confirmed);

        allowLookupToFinish.SetResult();
        await releaseSelection;

        Assert.Equal(@"C:\Games\Fallout4", _viewModel.GameDirectory);
        AssertSingleRefresh(@"C:\Games\Fallout4", GameRelease.Fallout4, AdvancedMode.On);
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
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that an undefined Advanced Mode value is a programming error rejected before anything changes.
    /// </summary>
    [Fact]
    public async Task SetAdvancedModeAsync_UndefinedValue_ThrowsWithoutChangingState()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.SetAdvancedModeAsync((AdvancedMode)42));

        Assert.Equal(GameContextState.Empty, sut.Current);
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that a changed Advanced Mode value is projected before same-source Plugin List refresh completes.
    /// </summary>
    [Fact]
    public async Task SetAdvancedModeAsync_ChangedCompleteContext_ProjectsAndRefreshesCurrentSource()
    {
        var refresh = ScriptedDiscovery.Held();
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Sequenced(ScriptedDiscovery.Completes(), refresh));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        ForgetRefreshes();

        var modeChange = sut.SetAdvancedModeAsync(AdvancedMode.On);

        Assert.True(_viewModel.AdvancedMode);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory), Assert.Single(Refreshes));

        refresh.Release();
        await modeChange;

        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, AdvancedMode.On);
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
        WithSkyrimLayout(GameDirectory);
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
        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, AdvancedMode.Off);
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
        Assert.Empty(Refreshes);

        pickerCompletion.SetResult(FileDialogResult.Cancelled());
        await browse;

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(Refreshes);
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
        ForgetRefreshes();

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
        Assert.Empty(Refreshes);
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
        AssertSingleRefresh(latestDirectory, GameRelease.Fallout4, AdvancedMode.Off);
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
        AssertSingleRefresh(latestDirectory, GameRelease.Fallout4, AdvancedMode.Off);
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
        ForgetRefreshes();

        var browse = sut.BrowseGameDirectoryAsync();
        await sut.SelectDetectedDirectoryAsync(latestDirectory);
        pickerCompletion.SetResult(FileDialogResult.Failure("obsolete picker failure"));
        await browse;

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(latestDirectory, _viewModel.GameDirectory);
        Assert.Equal([GameDirectory, latestDirectory], _viewModel.DetectedDirectories);
        AssertSingleRefresh(latestDirectory, GameRelease.SkyrimSE, AdvancedMode.Off);
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
        WithSkyrimLayout(GameDirectory);

        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.Equal(GameRelease.SkyrimSE, sut.Current.SelectedGameRelease);
        Assert.Equal(GameDirectory, sut.Current.SelectedGameDirectory);
        Assert.Empty(_gameInstallationProbe.InstalledDirectoryLookups);
        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, AdvancedMode.Off);
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
        WithSkyrimLayout(GameDirectory);
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
        AssertSingleRefresh(@"C:\Games\Fallout4", GameRelease.Fallout4, AdvancedMode.Off);
    }

    /// <summary>
    /// Verifies that a detection failure overtaken by a newer intent is swallowed rather than reported, so a retired
    /// Browse cannot reach the shell's error boundary.
    /// </summary>
    [Fact]
    public async Task BrowseGameDirectoryAsync_RetiredDetectionFailure_IsSuppressed()
    {
        var pickerCompletion = new TaskCompletionSource<FileDialogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var detectionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDetectionToFail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fileDialogService.Setup(x => x.SelectGameDirectory()).Returns(pickerCompletion.Task);
        _gameInstallationProbe.BeforeProbe = _ =>
        {
            detectionStarted.TrySetResult();
            allowDetectionToFail.Task.GetAwaiter().GetResult();
            throw new IOException("obsolete detection failure");
        };
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Games\Fallout4");
        var sut = CreateSut();

        var browse = sut.BrowseGameDirectoryAsync();
        pickerCompletion.SetResult(FileDialogResult.Success(GameDirectory));
        await detectionStarted.Task;
        var newerRelease = sut.SelectGameReleaseAsync(GameRelease.Fallout4);

        allowDetectionToFail.SetResult();
        await Task.WhenAll(browse, newerRelease);

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Equal(@"C:\Games\Fallout4", _viewModel.GameDirectory);
        Assert.Empty(_viewModel.ErrorMessages);
        AssertSingleRefresh(@"C:\Games\Fallout4", GameRelease.Fallout4, AdvancedMode.Off);
    }

    /// <summary>
    /// Verifies that browsing the current path retries discovery and leaves prior confirmation cleared on failure.
    /// </summary>
    /// <returns>A task that completes after the same-path discovery retry fails.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_SamePathDiscoveryFailure_RetriesAndClearsConfirmation()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, @"D:\Games\Skyrim");
        var sut = CreateSut(ScriptedGameLoadOrders.Sequenced(
            ScriptedDiscovery.Completes("PreviouslyConfirmed.esp"),
            ScriptedDiscovery.Fails("directory mismatch")));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.NotNull(PluginList.Current.Confirmed);
        ForgetRefreshes();
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal([GameDirectory, @"D:\Games\Skyrim"], _viewModel.DetectedDirectories);
        var failed = Assert.IsType<PluginListFailedState>(PluginList.Current);
        Assert.Null(failed.Confirmed);
        Assert.Equal("directory mismatch", failed.ErrorMessage);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory), Assert.Single(Refreshes));
        Assert.Empty(_gameInstallationProbe.ProbedPaths);
        Assert.Equal([GameRelease.SkyrimSE], _gameInstallationProbe.InstalledDirectoryLookups);
    }

    /// <summary>
    /// Verifies that current detection failure retains the chosen path, invalidates the Plugin List again, and presents
    /// guidance, all without discovery.
    /// </summary>
    /// <returns>A task that completes after detection failure is presented.</returns>
    /// <remarks>
    /// The Plugin List is driven only through the Game Context: a confirmed list is loaded, then the release is
    /// cleared, which is the only way the Game Context reaches "no release" after a confirmation. Browse is an explicit
    /// retry, so it invalidates once more even though the list already has no source.
    /// </remarks>
    [Fact]
    public async Task BrowseGameDirectoryAsync_SelectedDirectoryWithoutDetectableGame_RecordsWorkflowError()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("PreviouslyConfirmed.esp"));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.NotNull(PluginList.Current.Confirmed);
        await sut.SelectGameReleaseAsync(null);
        var revisionBeforeBrowse = PluginList.Current.StateRevision;
        ForgetRefreshes();
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        // Only install records are declared, so detection finds no known game master — its one reason to return null.

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Null(_viewModel.SelectedGame);
        Assert.IsType<PluginListNoSourceState>(PluginList.Current);
        Assert.True(PluginList.Current.StateRevision > revisionBeforeBrowse);
        Assert.Contains(
            "Could not detect game from directory. Please select a game from the dropdown.",
            _viewModel.ErrorMessages);
        Assert.Empty(Refreshes);
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
        Assert.Empty(Refreshes);
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
        var stateBeforeBrowse = sut.Current;
        ForgetRefreshes();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Equal(@"C:\Existing", _viewModel.GameDirectory);
        Assert.Equal([@"C:\Existing", @"D:\Suggested"], _viewModel.DetectedDirectories);
        Assert.Same(stateBeforeBrowse, sut.Current);
        Assert.Contains("Error selecting game directory: picker unavailable", _viewModel.ErrorMessages);
        Assert.Empty(Refreshes);
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
        ForgetRefreshes();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(@"C:\Existing", _viewModel.GameDirectory);
        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that the shell's ComboBoxes echoing this module's own projection back as intents change nothing: the
    /// game and directory ComboBoxes are bound one-way and forward every selection change, including the ones the
    /// projection itself causes.
    /// </summary>
    /// <remarks>
    /// The handlers below mirror the shell's: a game selection forwards the projected GameRelease, and a directory
    /// selection forwards the projected directory only while it is one of the detected directories, because the
    /// directory ComboBox has no item to select otherwise. Every echo must complete synchronously as a no-op, and only
    /// the two real intents may reach the Plugin List.
    /// </remarks>
    [Fact]
    public async Task SelectionIntents_ProjectionEchoedBackAsIntent_IsANoOp()
    {
        const string secondDirectory = @"D:\Games\Skyrim";
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, secondDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("User.esp"));
        var echoes = new List<Task>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.SelectedGame) && _viewModel.SelectedGame is { } game)
            {
                echoes.Add(sut.SelectGameReleaseAsync(game));
            }
            else if (args.PropertyName == nameof(MainWindowViewModel.GameDirectory) &&
                     _viewModel.DetectedDirectories.Contains(_viewModel.GameDirectory))
            {
                echoes.Add(sut.SelectDetectedDirectoryAsync(_viewModel.GameDirectory));
            }
        };

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await sut.SelectDetectedDirectoryAsync(secondDirectory);

        Assert.True(echoes.Count >= 2, "Both the game and the directory projection should have echoed back.");
        Assert.All(echoes, echo => Assert.True(echo.IsCompletedSuccessfully));
        Assert.Equal(
            [
                PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory),
                PluginListSource.Create(GameRelease.SkyrimSE, secondDirectory)
            ],
            Refreshes);
        Assert.Equal(
            PluginListSource.Create(GameRelease.SkyrimSE, secondDirectory),
            Assert.IsType<ConfirmedPluginList>(PluginList.Current.Confirmed).Source);
        Assert.Equal(secondDirectory, sut.Current.SelectedGameDirectory);
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

        sut.Dispose();
        allowLookupToFinish.SetResult();
        await selection;

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.Empty(Refreshes);
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

        sut.Dispose();
        allowPickerToFinish.SetResult(FileDialogResult.Failure("picker unavailable"));
        await browse;

        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that disposal retires the generation but leaves the borrowed Plugin List usable, because whoever
    /// composed the Game Context owns the Plugin List's lifetime.
    /// </summary>
    [Fact]
    public void Dispose_BorrowedPluginList_RemainsUsable()
    {
        var sut = CreateSut();

        sut.Dispose();

        var invalidation = Record.Exception(() => PluginList.Invalidate());
        Assert.Null(invalidation);
    }

    /// <summary>
    /// Builds the Game Context under test over a real Plugin List driven by the given scripted load orders.
    /// </summary>
    /// <param name="loadOrders">The discovery script; defaults to every discovery confirming an empty Plugin set.</param>
    private GameContext CreateSut(ScriptedGameLoadOrders? loadOrders = null)
    {
        _loadOrders = loadOrders ?? _loadOrders;
        _pluginList = new PluginList(_loadOrders);
        return new GameContext(
            new GameInstallations(_gameInstallationProbe),
            _fileDialogService.Object,
            _pluginList,
            _viewModel);
    }

    /// <summary>
    /// Starts counting refreshes afresh, so a test's setup discoveries do not count against what it asserts.
    /// </summary>
    private void ForgetRefreshes()
    {
        _refreshMark = _loadOrders.DiscoveredSources.Count;
    }

    /// <summary>
    /// Declares the on-disk layout Skyrim Special Edition detection recognises under a game root.
    /// </summary>
    /// <param name="gameRoot">The game root that should detect as Skyrim Special Edition.</param>
    private void WithSkyrimLayout(string gameRoot)
    {
        _gameInstallationProbe
            .WithFile(Path.Combine(gameRoot, "Data", "Skyrim.esm"))
            .WithFile(Path.Combine(gameRoot, "SkyrimSE.exe"));
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
    /// Asserts one canonical discovery source since the last <see cref="ForgetRefreshes" /> and the confirmed Advanced
    /// Mode produced by that refresh.
    /// </summary>
    private void AssertSingleRefresh(string gameDirectory, GameRelease gameRelease, AdvancedMode advancedMode)
    {
        Assert.Equal(PluginListSource.Create(gameRelease, gameDirectory), Assert.Single(Refreshes));
        var confirmed = Assert.IsType<ConfirmedPluginList>(PluginList.Current.Confirmed);
        Assert.Equal(advancedMode, confirmed.AdvancedMode);
    }
}
