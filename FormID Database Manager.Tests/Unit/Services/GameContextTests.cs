#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.Tests.Fakes;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
/// Drives the Game Context through its own interface over a real Plugin List, an in-memory installation probe, a
/// mocked picker and a recording sink, and asserts only what a caller can observe: the states and notices reported to
/// the sink, in order, the Plugin List's state, and which sources the Plugin List was asked to discover.
/// </summary>
/// <remarks>
/// <para>
/// No Plugin List presentation adapter is attached here, because the Game Context does not own one. A discovery
/// failure is therefore observed as the Plugin List's failed state rather than as the adapter's rendered message.
/// </para>
/// <para>
/// No ViewModel is involved either: the Game Context reports to a sink, and what the User Workflow's adapter writes
/// from those reports is covered by <see cref="UserWorkflowTests" /> (ADR-0013). Notices are compared as value records,
/// so no sentence is restated here; the wording is pinned by <see cref="GameContextPresentationTests" />.
/// </para>
/// </remarks>
public sealed class GameContextTests : IDisposable
{
    private const string GameDirectory = @"C:\Games\Skyrim";

    private readonly Mock<IFileDialogService> _fileDialogService = new();
    private readonly InMemoryGameInstallationProbe _gameInstallationProbe = new();
    private readonly RecordingGameContextSink _sink = new();
    private ScriptedGameLoadOrders _loadOrders = ScriptedGameLoadOrders.Fixed();
    private PluginList? _pluginList;
    private int _refreshMark;
    private int _reportMark;

    /// <summary>
    /// Gets the Plugin List the Game Context under test borrows.
    /// </summary>
    private PluginList PluginList =>
        _pluginList ?? throw new InvalidOperationException("Create the Game Context before reading its Plugin List.");

    /// <summary>
    /// Gets the sources the Plugin List was asked to discover since the last <see cref="ForgetSetup" />.
    /// </summary>
    private IReadOnlyList<PluginListSource> Refreshes => _loadOrders.DiscoveredSources.Skip(_refreshMark).ToArray();

    /// <summary>
    /// Gets everything the Game Context reported since the last <see cref="ForgetSetup" />, in report order.
    /// </summary>
    private IReadOnlyList<GameContextSinkEntry> Reports => _sink.Entries.Skip(_reportMark).ToArray();

    /// <summary>
    /// Gets the notices the Game Context reported since the last <see cref="ForgetSetup" />, in report order.
    /// </summary>
    private IReadOnlyList<GameContextNotice> ReportedNotices =>
        Reports.OfType<NoticeReported>().Select(static entry => entry.Notice).ToArray();

    /// <summary>
    /// Gets the most recent state the Game Context reported, ever, or the empty state when it has reported none. This is
    /// the state a sink adapter would be showing.
    /// </summary>
    private GameContextState LastReportedState =>
        _sink.Entries.OfType<StateReported>().LastOrDefault()?.State ?? GameContextState.Empty;

    /// <summary>
    /// Disposes the Plugin List, which the Game Context borrows and so never disposes itself.
    /// </summary>
    public void Dispose()
    {
        _pluginList?.Dispose();
    }

    /// <summary>
    /// Verifies that an explicit release intent reports a state with no directory before installed-location lookup
    /// completes, and then reports the ordered locations with the first one adopted.
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
        ForgetSetup();
        GateInstalledLocationLookup(GameRelease.Fallout4, lookupStarted, allowLookupToFinish);

        var selection = sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        await lookupStarted.Task;

        AssertReports(Reported(State(GameRelease.Fallout4, null)));
        Assert.IsType<PluginListNoSourceState>(PluginList.Current);

        allowLookupToFinish.SetResult();
        await selection;

        AssertReports(
            Reported(State(GameRelease.Fallout4, null)),
            Reported(State(
                GameRelease.Fallout4,
                @"C:\Games\Fallout4",
                [@"C:\Games\Fallout4", @"D:\Games\Fallout4"])));
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

        Assert.Equal(GameDirectory, LastReportedState.SelectedGameDirectory);
    }

    /// <summary>
    /// Verifies that an empty current lookup retains the explicit release and reports the Browse guidance notice.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_WithoutInstalledFolders_RetainsIncompleteContextAndRecordsGuidance()
    {
        // No install records are declared for Fallout4, so the lookup answers with nothing recorded.
        var sut = CreateSut();

        await sut.SelectGameReleaseAsync(GameRelease.Fallout4);

        AssertReports(
            Reported(State(GameRelease.Fallout4, null)),
            Reported(new NoInstalledLocationsFound(GameRelease.Fallout4)));
        Assert.Equal(GameRelease.Fallout4, sut.Current.SelectedGameRelease);
        Assert.Null(sut.Current.SelectedGameDirectory);
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that an equal explicit release event performs no duplicate lookup, report or refresh work.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_EqualRelease_IsNoOp()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        ForgetSetup();

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        // One lookup across both calls: the repeat did not start a second one.
        Assert.Equal([GameRelease.SkyrimSE], _gameInstallationProbe.InstalledDirectoryLookups);
        Assert.Empty(Refreshes);
        Assert.Empty(Reports);
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

        AssertReports(
            Reported(State(GameRelease.SkyrimSE, null)),
            Reported(State(GameRelease.SkyrimSE, GameDirectory, [GameDirectory])));
        Assert.Equal(GameRelease.SkyrimSE, sut.Current.SelectedGameRelease);
        Assert.Equal(GameDirectory, sut.Current.SelectedGameDirectory);
        Assert.Equal([GameDirectory], sut.Current.AvailableDirectories);
        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, AdvancedMode.Off);
    }

    /// <summary>
    /// Verifies that only the latest of two controlled release lookups can report guidance or state, or refresh.
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

        AssertReports(
            Reported(State(GameRelease.SkyrimSE, null)),
            Reported(State(GameRelease.Fallout4, null)),
            Reported(State(GameRelease.Fallout4, @"C:\NewFallout", [@"C:\NewFallout"])));
        AssertSingleRefresh(@"C:\NewFallout", GameRelease.Fallout4, AdvancedMode.Off);
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

        AssertStateEqual(State(GameRelease.Fallout4, @"C:\NewFallout", [@"C:\NewFallout"]), LastReportedState);
        AssertSingleRefresh(@"C:\NewFallout", GameRelease.Fallout4, AdvancedMode.Off);
        Assert.Empty(ReportedNotices);
    }

    /// <summary>
    /// Verifies that a current installed-location failure is not swallowed: it propagates to the caller, which in the
    /// shell is the final unexpected-error boundary, after the release itself was reported.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_CurrentLookupFailure_Propagates()
    {
        OnInstalledLocationLookup(GameRelease.SkyrimSE, () => throw new InvalidOperationException("lookup failed"));
        var sut = CreateSut();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SelectGameReleaseAsync(GameRelease.SkyrimSE));

        Assert.Equal("lookup failed", thrown.Message);
        AssertReports(Reported(State(GameRelease.SkyrimSE, null)));
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that picking the directory a current lookup already adopted reports nothing and requests no duplicate
    /// refresh.
    /// </summary>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_EqualDetectedDirectory_DoesNotDuplicateRefresh()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, @"D:\Games\Skyrim");

        var sut = CreateSut();

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        var reportsBeforePick = _sink.Entries.Count;
        await sut.SelectDetectedDirectoryAsync(GameDirectory);

        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, AdvancedMode.Off);
        Assert.Equal(reportsBeforePick, _sink.Entries.Count);
    }

    /// <summary>
    /// Verifies that picking the current installation through an equivalent spelling — its Data directory rather than
    /// its game root — changes nothing: nothing is reported, and the Plugin List and its selection are kept.
    /// </summary>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_EquivalentSpellingOfCurrentDirectory_KeepsPluginListAndSelection()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("User.esp"));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        SelectPlugin("User.esp");
        var stateBeforePick = sut.Current;
        var pluginListBeforePick = PluginList.Current;
        ForgetSetup();

        await sut.SelectDetectedDirectoryAsync(Path.Combine(GameDirectory, "Data"));

        Assert.Same(stateBeforePick, sut.Current);
        Assert.Same(pluginListBeforePick, PluginList.Current);
        Assert.Equal(["User.esp"], PluginList.Current.Confirmed!.SelectedPluginNames);
        Assert.Empty(Reports);
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that a directory pick that cannot become a Plugin List Source is refused with the one refusal notice and
    /// leaves the previous directory, the source and the Plugin List as they were.
    /// </summary>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_UnusableDirectory_RefusesAndLeavesStateAndPluginListUntouched()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("User.esp"));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        var stateBeforePick = sut.Current;
        var pluginListBeforePick = PluginList.Current;
        ForgetSetup();

        await sut.SelectDetectedDirectoryAsync("C:\\Games\\Sky\0rim");

        Assert.Same(stateBeforePick, sut.Current);
        Assert.Same(pluginListBeforePick, PluginList.Current);
        AssertReports(Reported(RefusalOf(() => PluginListSource.Create(GameRelease.SkyrimSE, "C:\\Games\\Sky\0rim"))));
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies the Game Context invariant in every reported state across a run of transitions: the displayed directory
    /// and the Plugin List Source are both present or both absent, both require a GameRelease, and the source is the
    /// one the displayed directory makes.
    /// </summary>
    [Fact]
    public async Task Current_EveryReportedState_HoldsDirectoryAndSourceTogether()
    {
        var sut = await RunEveryKindOfTransitionAsync();

        var reportedStates = _sink.Entries.OfType<StateReported>().Select(static entry => entry.State).ToArray();
        Assert.NotEmpty(reportedStates);
        Assert.All(reportedStates, state =>
        {
            Assert.Equal(state.SelectedGameDirectory is null, state.PluginListSource is null);
            if (state.SelectedGameDirectory is { } directory)
            {
                var gameRelease = Assert.NotNull(state.SelectedGameRelease);
                Assert.Equal(PluginListSource.Create(gameRelease, directory), state.PluginListSource);
            }
        });
        Assert.Equal(GameRelease.Fallout4, sut.Current.SelectedGameRelease);
        Assert.Equal(PluginListSource.Create(GameRelease.Fallout4, @"E:\Games\Fallout4"), sut.Current.PluginListSource);
    }

    /// <summary>
    /// Verifies the publish step's order across a run of transitions: the Game Context writes each state before it
    /// reports it, so a sink that reads <see cref="GameContext.Current" /> from inside the report reads the very state
    /// being reported. That order is what makes a re-entrant ComboBox echo a no-op (ADR-0013).
    /// </summary>
    [Fact]
    public async Task Current_AtEveryReport_IsTheReportedState()
    {
        GameContext? observed = null;
        var mismatches = new List<GameContextState>();
        var reportedStates = 0;
        _sink.OnEntry = entry =>
        {
            if (entry is StateReported { State: var state })
            {
                reportedStates++;
                if (!ReferenceEquals(state, observed!.Current))
                {
                    mismatches.Add(state);
                }
            }
        };

        await RunEveryKindOfTransitionAsync(sut => observed = sut);

        Assert.True(reportedStates > 0);
        Assert.Empty(mismatches);
    }

    /// <summary>
    /// Verifies that install-record directories that cannot become a Plugin List Source are left out when they are
    /// listed, and that the first remaining one is adopted.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_UnusableInstallRecordDirectory_IsLeftOutAndFirstUsableIsAdopted()
    {
        const string secondDirectory = @"D:\Games\Skyrim";
        _gameInstallationProbe.WithInstalledDirectories(
            GameRelease.SkyrimSE,
            "C:\\Games\\Sky\0rim",
            GameDirectory,
            secondDirectory);
        var sut = CreateSut();

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        AssertReports(
            Reported(State(GameRelease.SkyrimSE, null)),
            Reported(State(GameRelease.SkyrimSE, GameDirectory, [GameDirectory, secondDirectory])));
        Assert.Equal([GameDirectory, secondDirectory], sut.Current.AvailableDirectories);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory), sut.Current.PluginListSource);
        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, AdvancedMode.Off);
    }

    /// <summary>
    /// Verifies that when no install-record directory can become a Plugin List Source, the Game Context reports the
    /// Browse guidance notice and holds no directory.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_NoUsableInstallRecordDirectory_RecordsNoInstalledLocationsGuidance()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, "C:\\Games\\Sky\0rim");
        var sut = CreateSut();

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        Assert.Equal(GameRelease.SkyrimSE, sut.Current.SelectedGameRelease);
        Assert.Null(sut.Current.SelectedGameDirectory);
        Assert.Null(sut.Current.PluginListSource);
        Assert.Empty(sut.Current.AvailableDirectories);
        AssertReports(
            Reported(State(GameRelease.SkyrimSE, null)),
            Reported(new NoInstalledLocationsFound(GameRelease.SkyrimSE)));
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that an explicit changed directory is reported while preserving the ordered suggestions.
    /// </summary>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_ChangedDirectory_RefreshesPluginListForExplicitValue()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, @"D:\Games\Skyrim");
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        ForgetSetup();

        await sut.SelectDetectedDirectoryAsync(@"D:\Games\Skyrim");

        AssertReports(Reported(State(GameRelease.SkyrimSE, @"D:\Games\Skyrim", [GameDirectory, @"D:\Games\Skyrim"])));
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
        GameContextSinkEntry[] expectedReports =
        [
            Reported(State(GameRelease.SkyrimSE, null)),
            Reported(State(GameRelease.SkyrimSE, manualDirectory))
        ];

        var releaseSelection = sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await lookupStarted.Task;

        var directorySelection = sut.SelectDetectedDirectoryAsync(manualDirectory);

        AssertReports(expectedReports);
        Assert.Equal(@"D:\Games\Skyrim\Data", Assert.Single(Refreshes).DataDirectory);

        allowLookupToFinish.SetResult();
        await releaseSelection;

        AssertReports(expectedReports);
        Assert.Single(Refreshes);

        discovery.Release();
        await directorySelection;

        AssertReports(expectedReports);
        var failed = Assert.IsType<PluginListFailedState>(PluginList.Current);
        Assert.Null(failed.Confirmed);
        Assert.Equal("selected release does not match directory", failed.ErrorMessage);
    }

    /// <summary>
    /// Verifies that incomplete Game Context is reported and explicitly invalidates the confirmed Plugin List.
    /// </summary>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_ClearedSelection_InvalidatesConfirmedPluginList()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("Old.esp"));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.NotNull(PluginList.Current.Confirmed);
        ForgetSetup();

        await sut.SelectDetectedDirectoryAsync(null);

        Assert.Null(PluginList.Current.Confirmed);
        Assert.IsType<PluginListNoSourceState>(PluginList.Current);
        AssertReports(Reported(State(GameRelease.SkyrimSE, null, [GameDirectory])));
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

        AssertReports(
            Reported(State(GameRelease.Fallout4, null)),
            Reported(State(GameRelease.Fallout4, null, advancedMode: AdvancedMode.On)));
        Assert.Equal(AdvancedMode.On, sut.Current.AdvancedMode);
        Assert.Empty(Refreshes);
        Assert.Null(PluginList.Current.Confirmed);

        allowLookupToFinish.SetResult();
        await releaseSelection;

        AssertReports(
            Reported(State(GameRelease.Fallout4, null)),
            Reported(State(GameRelease.Fallout4, null, advancedMode: AdvancedMode.On)),
            Reported(State(GameRelease.Fallout4, @"C:\Games\Fallout4", [@"C:\Games\Fallout4"], AdvancedMode.On)));
        AssertSingleRefresh(@"C:\Games\Fallout4", GameRelease.Fallout4, AdvancedMode.On);
    }

    /// <summary>
    /// Verifies that an equal Advanced Mode event reports nothing and does not request duplicate Plugin List work.
    /// </summary>
    [Fact]
    public async Task SetAdvancedModeAsync_EqualValue_IsNoOp()
    {
        var sut = CreateSut();

        await sut.SetAdvancedModeAsync(AdvancedMode.Off);

        Assert.Empty(Reports);
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
        Assert.Empty(Reports);
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that a changed Advanced Mode value is reported before same-source Plugin List refresh completes.
    /// </summary>
    [Fact]
    public async Task SetAdvancedModeAsync_ChangedCompleteContext_ProjectsAndRefreshesCurrentSource()
    {
        var refresh = ScriptedDiscovery.Held();
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Sequenced(ScriptedDiscovery.Completes(), refresh));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        ForgetSetup();

        var modeChange = sut.SetAdvancedModeAsync(AdvancedMode.On);

        AssertReports(Reported(State(GameRelease.SkyrimSE, GameDirectory, [GameDirectory], AdvancedMode.On)));
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

        Assert.Equal(GameRelease.SkyrimSE, LastReportedState.SelectedGameRelease);
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

        AssertReports(Reported(State(GameRelease.SkyrimSE, null)));
        Assert.Empty(Refreshes);

        pickerCompletion.SetResult(FileDialogResult.Cancelled());
        await browse;

        AssertReports(Reported(State(GameRelease.SkyrimSE, null)));
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
        var stateBeforeBrowse = sut.Current;
        ForgetSetup();

        var olderBrowse = sut.BrowseGameDirectoryAsync();
        var newerBrowse = sut.BrowseGameDirectoryAsync();
        newerPicker.SetResult(FileDialogResult.Cancelled());
        await newerBrowse;
        olderPicker.SetResult(FileDialogResult.Success(GameDirectory));
        await olderBrowse;

        Assert.Same(stateBeforeBrowse, sut.Current);
        Assert.Empty(Reports);
        Assert.Empty(Refreshes);
        Assert.Empty(_gameInstallationProbe.ProbedPaths);
    }

    /// <summary>
    /// Verifies that only the latest of two controlled Browse pickers can report its selected directory.
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

        AssertReports(Reported(State(GameRelease.Fallout4, latestDirectory)));
        AssertSingleRefresh(latestDirectory, GameRelease.Fallout4, AdvancedMode.Off);
        Assert.False(_gameInstallationProbe.ProbedAnythingUnder(GameDirectory));
    }

    /// <summary>
    /// Verifies that a newer release intent prevents an older picker result from reporting any Browse state.
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

        AssertReports(
            Reported(State(GameRelease.Fallout4, null)),
            Reported(State(GameRelease.Fallout4, latestDirectory, [latestDirectory])));
        AssertSingleRefresh(latestDirectory, GameRelease.Fallout4, AdvancedMode.Off);
        Assert.Empty(_gameInstallationProbe.ProbedPaths);
    }

    /// <summary>
    /// Verifies that a newer explicit directory suppresses an older picker failure and its notice.
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
        ForgetSetup();

        var browse = sut.BrowseGameDirectoryAsync();
        await sut.SelectDetectedDirectoryAsync(latestDirectory);
        pickerCompletion.SetResult(FileDialogResult.Failure("obsolete picker failure"));
        await browse;

        AssertReports(Reported(State(GameRelease.SkyrimSE, latestDirectory, [GameDirectory, latestDirectory])));
        AssertSingleRefresh(latestDirectory, GameRelease.SkyrimSE, AdvancedMode.Off);
    }

    /// <summary>
    /// Verifies that Browse detects a missing release without retaining suggestions or starting location lookup.
    /// </summary>
    /// <returns>A task that completes after Browse detection and Plugin List discovery finish.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_DirectoryWithoutGameRelease_DetectsGameAndRefreshesPlugins()
    {
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        WithSkyrimLayout(GameDirectory);

        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();

        AssertReports(Reported(State(GameRelease.SkyrimSE, GameDirectory)));
        Assert.Equal(GameRelease.SkyrimSE, sut.Current.SelectedGameRelease);
        Assert.Equal(GameDirectory, sut.Current.SelectedGameDirectory);
        Assert.Empty(_gameInstallationProbe.InstalledDirectoryLookups);
        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, AdvancedMode.Off);
    }

    /// <summary>
    /// Verifies that Game Context keeps its latest-intent generation check after a report returns: when the report of a
    /// Browse's adopted directory re-enters and starts a newer release selection, the Browse is retired, reports
    /// nothing further and never refreshes the Plugin List with the source it adopted.
    /// </summary>
    /// <returns>A task that completes after the retired Browse and the newer release selection settle.</returns>
    /// <remarks>
    /// The newer selection's installed-location lookup is held open until the Browse has finished, so whatever the
    /// Browse does after its report is observed before the newer intent can report or refresh anything of its own.
    /// </remarks>
    [Fact]
    public async Task BrowseGameDirectoryAsync_ReportReentersWithNewerReleaseSelection_RetiresTheBrowse()
    {
        const string newerDirectory = @"C:\Games\Fallout4";
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLookupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        WithSkyrimLayout(GameDirectory);
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, newerDirectory);
        GateInstalledLocationLookup(GameRelease.Fallout4, lookupStarted, allowLookupToFinish);
        var sut = CreateSut();
        Task? newerRelease = null;
        _sink.OnEntry = entry =>
        {
            // Re-enter only from the Browse's own adoption report, once; the newer intent's reports pass through.
            if (newerRelease is null &&
                entry is StateReported { State.SelectedGameRelease: GameRelease.SkyrimSE })
            {
                newerRelease = sut.SelectGameReleaseAsync(GameRelease.Fallout4);
            }
        };

        await sut.BrowseGameDirectoryAsync();
        await lookupStarted.Task;

        Assert.NotNull(newerRelease);
        AssertReports(
            Reported(State(GameRelease.SkyrimSE, GameDirectory)),
            Reported(State(GameRelease.Fallout4, null)));
        Assert.Empty(Refreshes);

        allowLookupToFinish.SetResult();
        await newerRelease;

        AssertReports(
            Reported(State(GameRelease.SkyrimSE, GameDirectory)),
            Reported(State(GameRelease.Fallout4, null)),
            Reported(State(GameRelease.Fallout4, newerDirectory, [newerDirectory])));
        AssertSingleRefresh(newerDirectory, GameRelease.Fallout4, AdvancedMode.Off);
    }

    /// <summary>
    /// Verifies that a newer release intent prevents an older Browse detection result from reporting context or
    /// refreshing.
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

        AssertReports(
            Reported(State(GameRelease.Fallout4, null)),
            Reported(State(GameRelease.Fallout4, @"C:\Games\Fallout4", [@"C:\Games\Fallout4"])));
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

        AssertReports(
            Reported(State(GameRelease.Fallout4, null)),
            Reported(State(GameRelease.Fallout4, @"C:\Games\Fallout4", [@"C:\Games\Fallout4"])));
        AssertSingleRefresh(@"C:\Games\Fallout4", GameRelease.Fallout4, AdvancedMode.Off);
    }

    /// <summary>
    /// Verifies that browsing to an equivalent spelling of the current source while its Plugin List loaded fine is a
    /// no-op: the same source is the same directory, so nothing is invalidated, reloaded or reported.
    /// </summary>
    /// <returns>A task that completes after the Browse is handled.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_EquivalentSpellingOfConfirmedSource_IsANoOp()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, @"D:\Games\Skyrim");
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("User.esp"));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        SelectPlugin("User.esp");
        var stateBeforeBrowse = sut.Current;
        var pluginListBeforeBrowse = PluginList.Current;
        ForgetSetup();
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(Path.Combine(GameDirectory, "Data")));

        await sut.BrowseGameDirectoryAsync();

        Assert.Same(stateBeforeBrowse, sut.Current);
        Assert.Same(pluginListBeforeBrowse, PluginList.Current);
        Assert.Equal(["User.esp"], PluginList.Current.Confirmed!.SelectedPluginNames);
        Assert.Empty(Reports);
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that browsing to the current source while its Plugin List is in the failed state is the user's
    /// explicit retry: the Plugin List is invalidated and then refreshed again, and the Game Context is unchanged and
    /// reports nothing.
    /// </summary>
    /// <returns>A task that completes after the retried discovery finishes.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_EqualSourceWhosePluginListFailed_InvalidatesAndRetries()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, @"D:\Games\Skyrim");
        var sut = CreateSut(ScriptedGameLoadOrders.Sequenced(
            ScriptedDiscovery.Fails("directory mismatch"),
            ScriptedDiscovery.Completes("Retried.esp")));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.IsType<PluginListFailedState>(PluginList.Current);
        var stateBeforeBrowse = sut.Current;
        ForgetSetup();
        var publishedStates = RecordPluginListStates();
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(Path.Combine(GameDirectory, "Data")));

        await sut.BrowseGameDirectoryAsync();

        Assert.IsType<PluginListNoSourceState>(publishedStates[0]);
        Assert.Same(stateBeforeBrowse, sut.Current);
        Assert.Empty(Reports);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory), Assert.Single(Refreshes));
        Assert.Equal(["Retried.esp"], PluginList.Current.Confirmed!.Entries.Select(static entry => entry.Name));
        Assert.Empty(_gameInstallationProbe.ProbedPaths);
    }

    /// <summary>
    /// Verifies that an accepted Browse to a different source invalidates the Plugin List before adopting the directory
    /// and refreshing, so the old confirmation is gone before the new discovery starts.
    /// </summary>
    /// <returns>A task that completes after the new discovery finishes.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_DifferentSource_InvalidatesBeforeRefreshing()
    {
        const string browsedDirectory = @"D:\Games\Skyrim";
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("User.esp"));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        ForgetSetup();
        var publishedStates = RecordPluginListStates();
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(browsedDirectory));

        await sut.BrowseGameDirectoryAsync();

        Assert.IsType<PluginListNoSourceState>(publishedStates[0]);
        AssertReports(Reported(State(GameRelease.SkyrimSE, browsedDirectory, [GameDirectory])));
        Assert.Equal(browsedDirectory, sut.Current.SelectedGameDirectory);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, browsedDirectory), sut.Current.PluginListSource);
        AssertSingleRefresh(browsedDirectory, GameRelease.SkyrimSE, AdvancedMode.Off);
    }

    /// <summary>
    /// Verifies that a Browse with no GameRelease whose directory holds no detectable game is refused with the
    /// detection notice and keeps nothing: the directory is not held, and the Plugin List is not touched.
    /// </summary>
    /// <returns>A task that completes after detection failure is reported.</returns>
    /// <remarks>
    /// The Plugin List is driven only through the Game Context: a confirmed list is loaded, then the release is
    /// cleared, which is the only way the Game Context reaches "no release" after a confirmation. A directory is only
    /// adopted once a GameRelease is known, so a refused detection must not even invalidate the list again.
    /// </remarks>
    [Fact]
    public async Task BrowseGameDirectoryAsync_SelectedDirectoryWithoutDetectableGame_RefusesAndKeepsNothing()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("PreviouslyConfirmed.esp"));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.NotNull(PluginList.Current.Confirmed);
        await sut.SelectGameReleaseAsync(null);
        var stateBeforeBrowse = sut.Current;
        var pluginListBeforeBrowse = PluginList.Current;
        ForgetSetup();
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        // Only install records are declared, so detection finds no known game master — its one reason to return null.

        await sut.BrowseGameDirectoryAsync();

        Assert.Same(stateBeforeBrowse, sut.Current);
        Assert.Same(pluginListBeforeBrowse, PluginList.Current);
        AssertReports(Reported(new NoGameDetected()));
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that a path detection cannot use at all is reported as the path problem it is, rather than as a
    /// missing game the user is asked to supply from the dropdown, and that the refused path is not kept.
    /// </summary>
    /// <returns>A task that completes after the malformed path is reported.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_MalformedDirectoryWithoutGameRelease_RefusesWithPathErrorAndKeepsNothing()
    {
        const string malformedDirectory = "C:\\Games\\Sky\0rim";
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(malformedDirectory));
        var sut = CreateSut();
        var pluginListBeforeBrowse = PluginList.Current;

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameContextState.Empty, sut.Current);
        Assert.Same(pluginListBeforeBrowse, PluginList.Current);
        var detection = new GameInstallations(new InMemoryGameInstallationProbe());
        AssertReports(Reported(RefusalOf(() => detection.Detect(malformedDirectory))));
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that a Browse with a GameRelease already selected refuses a directory that cannot become a Plugin List
    /// Source with the one refusal notice, and leaves the previous directory, the source and the Plugin List as they
    /// were.
    /// </summary>
    /// <returns>A task that completes after the refusal is reported.</returns>
    /// <remarks>
    /// Detection is skipped when a GameRelease is selected, so building the source is the only thing that can notice
    /// the path is unusable. It used to throw out of the Plugin List refresh into the shell's "Unexpected error".
    /// </remarks>
    [Fact]
    public async Task BrowseGameDirectoryAsync_UnusableDirectoryWithGameRelease_RefusesAndLeavesStateAndPluginListUntouched()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("PreviouslyConfirmed.esp"));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        var stateBeforeBrowse = sut.Current;
        var pluginListBeforeBrowse = PluginList.Current;
        ForgetSetup();
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success("C:\\Games\\Sky\0rim"));

        await sut.BrowseGameDirectoryAsync();

        Assert.Same(stateBeforeBrowse, sut.Current);
        Assert.Same(pluginListBeforeBrowse, PluginList.Current);
        AssertReports(Reported(RefusalOf(() => PluginListSource.Create(GameRelease.SkyrimSE, "C:\\Games\\Sky\0rim"))));
        Assert.Empty(Refreshes);
        Assert.Empty(_gameInstallationProbe.ProbedPaths);
    }

    /// <summary>
    /// Verifies that a Browse with no GameRelease whose detection throws for a reason other than the path's shape is
    /// refused with the refusal notice and keeps nothing.
    /// </summary>
    /// <returns>A task that completes after the refusal is reported.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_DetectionThrowsWithoutGameRelease_RefusesAndKeepsNothing()
    {
        _gameInstallationProbe.BeforeProbe = _ => throw new IOException("drive not ready");
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        var sut = CreateSut();
        var pluginListBeforeBrowse = PluginList.Current;

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameContextState.Empty, sut.Current);
        Assert.Same(pluginListBeforeBrowse, PluginList.Current);
        AssertReports(Reported(new DirectoryRefused("drive not ready")));
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that current picker failure leaves the existing Game Context unchanged and reports the failure notice.
    /// </summary>
    /// <returns>A task that completes after picker failure is reported.</returns>
    [Fact]
    public async Task BrowseGameDirectoryAsync_CurrentPickerFailure_LeavesSnapshotUnchangedAndRecordsError()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Existing", @"D:\Suggested");
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Failure("picker unavailable"));
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        var stateBeforeBrowse = sut.Current;
        ForgetSetup();

        await sut.BrowseGameDirectoryAsync();

        Assert.Same(stateBeforeBrowse, sut.Current);
        AssertReports(Reported(new GameDirectoryPickerFailed("picker unavailable")));
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that picker cancellation leaves an existing Game Context unchanged and reports nothing.
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
        var stateBeforeBrowse = sut.Current;
        ForgetSetup();

        await sut.BrowseGameDirectoryAsync();

        Assert.Same(stateBeforeBrowse, sut.Current);
        Assert.Empty(Reports);
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that a sink echoing a release selection's own reported states back as intents changes nothing, as the
    /// shell's game and directory ComboBoxes do: they are bound one-way and forward every selection change, including
    /// the ones the projection of a report itself causes.
    /// </summary>
    /// <remarks>
    /// A release selection reports both the GameRelease and, once lookup finishes, the first detected directory, so
    /// both kinds of echo happen. Every echo must complete synchronously as a no-op, and only the real intent may reach
    /// the Plugin List.
    /// </remarks>
    [Fact]
    public async Task SelectGameReleaseAsync_ProjectionEchoedBackAsIntent_IsANoOp()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, @"D:\Games\Skyrim");
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("User.esp"));
        var echoes = EchoReportsBackAsIntents(sut);

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        Assert.Contains(EchoedSelection.GameRelease, echoes.Select(static echo => echo.Selection));
        Assert.Contains(EchoedSelection.Directory, echoes.Select(static echo => echo.Selection));
        Assert.All(echoes, echo => Assert.True(echo.Intent.IsCompletedSuccessfully));
        Assert.Equal([PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory)], Refreshes);
        Assert.Equal(
            PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory),
            Assert.IsType<ConfirmedPluginList>(PluginList.Current.Confirmed).Source);
        Assert.Equal(GameDirectory, sut.Current.SelectedGameDirectory);
        AssertReports(
            Reported(State(GameRelease.SkyrimSE, null)),
            Reported(State(GameRelease.SkyrimSE, GameDirectory, [GameDirectory, @"D:\Games\Skyrim"])));
    }

    /// <summary>
    /// Verifies that a sink echoing a detected-directory pick's own reported state back as an intent changes nothing.
    /// </summary>
    /// <remarks>
    /// Every echo must complete synchronously as a no-op, and only the real pick may reach the Plugin List after the
    /// release selection that listed the directories.
    /// </remarks>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_ProjectionEchoedBackAsIntent_IsANoOp()
    {
        const string secondDirectory = @"D:\Games\Skyrim";
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, secondDirectory);
        var sut = CreateSut(ScriptedGameLoadOrders.Fixed("User.esp"));
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        ForgetSetup();
        var echoes = EchoReportsBackAsIntents(sut);

        await sut.SelectDetectedDirectoryAsync(secondDirectory);

        Assert.Contains(EchoedSelection.Directory, echoes.Select(static echo => echo.Selection));
        Assert.All(echoes, echo => Assert.True(echo.Intent.IsCompletedSuccessfully));
        Assert.Equal([PluginListSource.Create(GameRelease.SkyrimSE, secondDirectory)], Refreshes);
        Assert.Equal(
            PluginListSource.Create(GameRelease.SkyrimSE, secondDirectory),
            Assert.IsType<ConfirmedPluginList>(PluginList.Current.Confirmed).Source);
        Assert.Equal(secondDirectory, sut.Current.SelectedGameDirectory);
        AssertReports(Reported(State(GameRelease.SkyrimSE, secondDirectory, [GameDirectory, secondDirectory])));
    }

    /// <summary>
    /// Verifies that disposal prevents a late installed-location result from reporting directories or refreshing
    /// Plugins.
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

        AssertReports(Reported(State(GameRelease.Fallout4, null)));
        Assert.Empty(Refreshes);
    }

    /// <summary>
    /// Verifies that disposal prevents a late folder-picker failure from reporting an obsolete notice.
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

        Assert.Empty(Reports);
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
    /// Builds the Game Context under test over a real Plugin List driven by the given scripted load orders, reporting
    /// to the recording sink.
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
            _sink);
    }

    /// <summary>
    /// Drives one Game Context through every kind of transition that reports a state: a release selection, a
    /// detected-directory pick, an Advanced Mode change, a cleared directory, a cleared release and a Browse that
    /// detects a different game.
    /// </summary>
    /// <param name="onCreated">Run with the Game Context as soon as it exists, before any transition.</param>
    /// <returns>The Game Context, after the last transition.</returns>
    private async Task<GameContext> RunEveryKindOfTransitionAsync(Action<GameContext>? onCreated = null)
    {
        const string browsedDirectory = @"E:\Games\Fallout4";
        _gameInstallationProbe
            .WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, @"D:\Games\Skyrim")
            .WithFile(Path.Combine(browsedDirectory, "Data", "Fallout4.esm"));
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(browsedDirectory));
        var sut = CreateSut();
        onCreated?.Invoke(sut);

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        await sut.SelectDetectedDirectoryAsync(@"D:\Games\Skyrim");
        await sut.SetAdvancedModeAsync(AdvancedMode.On);
        await sut.SelectDetectedDirectoryAsync(null);
        await sut.SelectGameReleaseAsync(null);
        await sut.BrowseGameDirectoryAsync();

        return sut;
    }

    /// <summary>
    /// Starts counting refreshes and reports afresh, so a test's setup does not count against what it asserts.
    /// </summary>
    private void ForgetSetup()
    {
        _refreshMark = _loadOrders.DiscoveredSources.Count;
        _reportMark = _sink.Entries.Count;
    }

    /// <summary>
    /// Selects one Plugin of the currently confirmed Plugin List, so a test can tell whether a later transition kept
    /// the user's selection.
    /// </summary>
    /// <param name="pluginName">The confirmed Plugin to select.</param>
    private void SelectPlugin(string pluginName)
    {
        var confirmed = Assert.IsType<ConfirmedPluginList>(PluginList.Current.Confirmed);
        PluginList.Apply(new PluginSelectionByNameIntent(confirmed.MembershipVersion, pluginName, true));
        Assert.Equal([pluginName], PluginList.Current.Confirmed!.SelectedPluginNames);
    }

    /// <summary>
    /// Makes the recording sink mirror the shell's one-way ComboBoxes, forwarding each reported selection back to the
    /// Game Context as an intent from inside the report, and records every intent it forwards.
    /// </summary>
    /// <param name="sut">The Game Context that receives the echoed intents.</param>
    /// <returns>The list that receives each echoed selection and the task its forwarded intent returned.</returns>
    /// <remarks>
    /// Every reported GameRelease is echoed, and a reported directory is echoed only while it is one of the available
    /// directories, because the directory ComboBox has no item to select otherwise. Echoing on every report rather than
    /// only on a change is stricter than the shell, which only forwards what changed.
    /// </remarks>
    private List<(EchoedSelection Selection, Task Intent)> EchoReportsBackAsIntents(GameContext sut)
    {
        var echoes = new List<(EchoedSelection Selection, Task Intent)>();
        _sink.OnEntry = entry =>
        {
            if (entry is not StateReported { State: var state })
            {
                return;
            }

            if (state.SelectedGameRelease is { } game)
            {
                echoes.Add((EchoedSelection.GameRelease, sut.SelectGameReleaseAsync(game)));
            }

            if (state.SelectedGameDirectory is { } directory && state.AvailableDirectories.Contains(directory))
            {
                echoes.Add((EchoedSelection.Directory, sut.SelectDetectedDirectoryAsync(directory)));
            }
        };

        return echoes;
    }

    /// <summary>
    /// Starts recording every state the Plugin List publishes from this point on, in publication order.
    /// </summary>
    /// <returns>The list that receives the published states.</returns>
    private List<PluginListState> RecordPluginListStates()
    {
        var publishedStates = new List<PluginListState>();
        PluginList.Changed += (_, _) => publishedStates.Add(PluginList.Current);
        return publishedStates;
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
    /// Asserts one canonical discovery source since the last <see cref="ForgetSetup" /> and the confirmed Advanced
    /// Mode produced by that refresh.
    /// </summary>
    private void AssertSingleRefresh(string gameDirectory, GameRelease gameRelease, AdvancedMode advancedMode)
    {
        Assert.Equal(PluginListSource.Create(gameRelease, gameDirectory), Assert.Single(Refreshes));
        var confirmed = Assert.IsType<ConfirmedPluginList>(PluginList.Current.Confirmed);
        Assert.Equal(advancedMode, confirmed.AdvancedMode);
    }

    /// <summary>
    /// Asserts that exactly the given reports were made since the last <see cref="ForgetSetup" />, in that order.
    /// </summary>
    /// <param name="expected">The expected reports.</param>
    /// <remarks>
    /// States are compared field by field through <see cref="AssertStateEqual" />, because record equality would
    /// compare their available directories by reference. Notices are value records and are compared directly.
    /// </remarks>
    private void AssertReports(params GameContextSinkEntry[] expected)
    {
        var actual = Reports;
        Assert.True(
            expected.Length == actual.Count,
            $"Expected {expected.Length} reports but the sink recorded {actual.Count}: {string.Join(", ", actual)}");
        for (var index = 0; index < expected.Length; index++)
        {
            if (expected[index] is StateReported { State: var expectedState })
            {
                AssertStateEqual(expectedState, Assert.IsType<StateReported>(actual[index]).State);
            }
            else
            {
                Assert.Equal(expected[index], actual[index]);
            }
        }
    }

    /// <summary>
    /// Asserts that two Game Context states hold the same values, comparing the available directories by content.
    /// </summary>
    /// <param name="expected">The expected state.</param>
    /// <param name="actual">The reported state.</param>
    private static void AssertStateEqual(GameContextState expected, GameContextState actual)
    {
        Assert.Equal(expected.SelectedGameRelease, actual.SelectedGameRelease);
        Assert.Equal(expected.SelectedGameDirectory, actual.SelectedGameDirectory);
        Assert.Equal(expected.PluginListSource, actual.PluginListSource);
        Assert.Equal(expected.AvailableDirectories.ToArray(), actual.AvailableDirectories.ToArray());
        Assert.Equal(expected.AdvancedMode, actual.AdvancedMode);
    }

    /// <summary>
    /// Builds the Game Context state a transition is expected to report, with the Plugin List Source the displayed
    /// directory makes, so the invariant holds in every expected state too.
    /// </summary>
    /// <param name="gameRelease">The expected GameRelease.</param>
    /// <param name="directory">The expected displayed directory, or null when none is held.</param>
    /// <param name="availableDirectories">The expected available directories; none when omitted.</param>
    /// <param name="advancedMode">The expected Advanced Mode.</param>
    /// <returns>The expected state.</returns>
    private static GameContextState State(
        GameRelease? gameRelease,
        string? directory,
        string[]? availableDirectories = null,
        AdvancedMode advancedMode = AdvancedMode.Off)
    {
        var source = gameRelease is { } release && directory is not null
            ? PluginListSource.Create(release, directory)
            : null;
        return new GameContextState(gameRelease, directory, source, [.. availableDirectories ?? []], advancedMode);
    }

    /// <summary>
    /// Wraps an expected state as the report it should arrive in.
    /// </summary>
    private static GameContextSinkEntry Reported(GameContextState state)
    {
        return new StateReported(state);
    }

    /// <summary>
    /// Wraps an expected notice as the report it should arrive in.
    /// </summary>
    private static GameContextSinkEntry Reported(GameContextNotice notice)
    {
        return new NoticeReported(notice);
    }

    /// <summary>
    /// Returns the refusal notice a directory pick should produce, built from the failure the unusable path actually
    /// raises in the module that rejects it.
    /// </summary>
    /// <param name="rejectPath">The call that rejects the path the same way the Game Context's own call does.</param>
    /// <returns>The expected refusal notice.</returns>
    /// <remarks>
    /// The failure's message comes from the runtime's path handling, so it is captured here rather than written down.
    /// </remarks>
    private static DirectoryRefused RefusalOf(Action rejectPath)
    {
        return new DirectoryRefused(Assert.ThrowsAny<Exception>(rejectPath).Message);
    }

    /// <summary>
    /// Which ComboBox an echoed intent came from.
    /// </summary>
    private enum EchoedSelection
    {
        /// <summary>The game ComboBox, echoing a reported GameRelease.</summary>
        GameRelease,

        /// <summary>The directory ComboBox, echoing a reported directory.</summary>
        Directory
    }
}
