#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class PluginListTests
{
    /// <summary>
    ///     Verifies Plugin List consumes the highest Game Load Orders seam and retains deterministic disposal without
    ///     pinning its concrete kind or exact constructor shape.
    /// </summary>
    [Fact]
    public void PluginListConstructor_HighestGameLoadOrdersSeam_ConsumesDependencyAndSupportsDisposal()
    {
        var pluginListType = typeof(PluginList);
        var dependencyTypes = pluginListType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IDisposable), pluginListType.GetInterfaces());
        Assert.Contains(typeof(IGameLoadOrders), dependencyTypes);
    }

    /// <summary>
    ///     Verifies the state contract is one closed hierarchy with shared sourced-state coherence and no retired
    ///     activity-object protocol.
    /// </summary>
    [Fact]
    public void TypeShape_StateHierarchy_HasOnlyConcreteCoherentVariants()
    {
        var stateType = typeof(PluginListState);
        var sourcedStateType = typeof(SourcedPluginListState);
        var concreteStateTypes = stateType.Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && stateType.IsAssignableFrom(type))
            .OrderBy(type => type.Name)
            .ToArray();

        Assert.True(stateType.IsAbstract);
        Assert.True(sourcedStateType.IsAbstract);
        Assert.Equal(
            [
                typeof(PluginListCancelledState),
                typeof(PluginListFailedState),
                typeof(PluginListFaultedState),
                typeof(PluginListNoSourceState),
                typeof(PluginListReadyState),
                typeof(PluginListRefreshingState)
            ],
            concreteStateTypes);
        Assert.All(
            concreteStateTypes.Where(type => type != typeof(PluginListNoSourceState)),
            type => Assert.True(sourcedStateType.IsAssignableFrom(type)));
        Assert.Null(stateType.Assembly.GetType($"{stateType.Namespace}.PluginListActivity"));
    }

    [Fact]
    public async Task RefreshAsync_InitialDiscovery_PublishesImmutableConfirmedPluginListInPluginListOrder()
    {
        var discovery = new DeterministicPluginListGameLoadOrders(
            "skyrim.ESM",
            "UserA.esp",
            "usera.ESP",
            "UserB.esp");
        using var sut = new PluginList(discovery);
        var changedCount = 0;
        EventArgs? lastEventArgs = null;
        var publishedStates = new List<PluginListState>();
        sut.Changed += (_, eventArgs) =>
        {
            changedCount++;
            lastEventArgs = eventArgs;
            publishedStates.Add(sut.Current);
        };

        Assert.Equal(0, sut.Current.StateRevision);
        Assert.Equal(0, sut.Current.ActivityRevision);
        Assert.Null(sut.Current.Confirmed);
        Assert.IsType<PluginListNoSourceState>(sut.Current);

        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        var current = sut.Current;
        var confirmed = Assert.IsType<ConfirmedPluginList>(current.Confirmed);
        Assert.Equal(1, confirmed.MembershipVersion);
        Assert.Equal(GameRelease.SkyrimSE, confirmed.Source.GameRelease);
        Assert.Equal(discovery.DataDirectory, confirmed.Source.DataDirectory, ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(AdvancedMode.Off, confirmed.AdvancedMode);
        Assert.Equal(["UserA.esp", "UserB.esp"], confirmed.Entries.Select(entry => entry.Name).ToArray());
        Assert.Empty(confirmed.SelectedPluginNames);
        var ready = Assert.IsType<PluginListReadyState>(current);
        Assert.Same(confirmed, ready.Confirmed);
        Assert.Same(confirmed.Source, ready.Source);
        Assert.Equal(confirmed.MembershipVersion, ready.MembershipVersion);
        Assert.True(current.StateRevision >= 2);
        Assert.True(changedCount >= 2);
        Assert.Same(EventArgs.Empty, lastEventArgs);
        Assert.Contains(publishedStates, state => state is PluginListRefreshingState);
        Assert.Contains(publishedStates, state => state is PluginListReadyState);
    }

    /// <summary>
    ///     Verifies each refresh activity occurrence advances both revisions, including structurally equal progress.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_EqualProgressOccurrences_AdvanceStateAndActivityRevisions()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var operation = discovery.Enqueue();
        using var sut = new PluginList(discovery);

        Assert.Equal(0, sut.Current.StateRevision);
        Assert.Equal(0, sut.Current.ActivityRevision);

        var refresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var refreshing = sut.Current;

        Assert.Equal(1, refreshing.StateRevision);
        Assert.Equal(1, refreshing.ActivityRevision);

        operation.ReportProgress(3, 12);
        var firstProgress = sut.Current;
        operation.ReportProgress(3, 12);
        var secondProgress = sut.Current;

        Assert.Equal(refreshing.StateRevision + 1, firstProgress.StateRevision);
        Assert.Equal(refreshing.ActivityRevision + 1, firstProgress.ActivityRevision);
        Assert.Equal(firstProgress.StateRevision + 1, secondProgress.StateRevision);
        Assert.Equal(firstProgress.ActivityRevision + 1, secondProgress.ActivityRevision);
        var firstRefreshing = Assert.IsType<PluginListRefreshingState>(firstProgress);
        var secondRefreshing = Assert.IsType<PluginListRefreshingState>(secondProgress);
        Assert.Equal(firstRefreshing.Source, secondRefreshing.Source);
        Assert.Equal(firstRefreshing.ScannedCount, secondRefreshing.ScannedCount);
        Assert.Equal(firstRefreshing.TotalCount, secondRefreshing.TotalCount);

        operation.Complete("User.esp");
        await refresh;

        Assert.Equal(secondProgress.StateRevision + 1, sut.Current.StateRevision);
        Assert.Equal(secondProgress.ActivityRevision + 1, sut.Current.ActivityRevision);
        Assert.IsType<PluginListReadyState>(sut.Current);
    }

    /// <summary>
    ///     Verifies Plugin List passes its canonical source, raw observer, and linked caller/retirement lifetime directly
    ///     through the highest Game Load Orders seam.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_Source_DelegatesCanonicalFactsAndLinkedCancellationToGameLoadOrders()
    {
        var gameLoadOrders = new ControlledPluginListGameLoadOrders();
        var operation = gameLoadOrders.Enqueue();
        var gameDirectory = CreateGameDirectory();
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var sut = new PluginList(gameLoadOrders);

        var refresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            callerCancellation.Token);

        Assert.Equal(GameRelease.SkyrimSE, operation.GameRelease);
        Assert.Equal(
            System.IO.Path.Combine(gameDirectory, "Data"),
            operation.CanonicalDataDirectory,
            ignoreCase: OperatingSystem.IsWindows());
        Assert.NotNull(operation.Progress);
        Assert.NotEqual(callerCancellation.Token, operation.CancellationToken);
        Assert.False(operation.CancellationToken.IsCancellationRequested);

        callerCancellation.Cancel();

        Assert.True(operation.CancellationToken.IsCancellationRequested);
        operation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
    }

    [Fact]
    public async Task RefreshAsync_AdvancedMode_IncludesBasePlugins()
    {
        var discovery = new DeterministicPluginListGameLoadOrders("skyrim.ESM", "UPDATE.ESM", "User.esp");
        using var sut = new PluginList(discovery);

        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.On,
            TestContext.Current.CancellationToken);

        var confirmed = Assert.IsType<ConfirmedPluginList>(sut.Current.Confirmed);
        Assert.Equal(
            ["skyrim.ESM", "UPDATE.ESM", "User.esp"],
            confirmed.Entries.Select(entry => entry.Name).ToArray());
    }

    /// <summary>
    ///     Verifies Advanced Mode Off hides the real base Plugins of each supported game — not a stub set — in whatever
    ///     casing discovery reports them, while user Plugins stay visible.
    /// </summary>
    /// <param name="gameRelease">The GameRelease whose base Plugin rules apply to this refresh.</param>
    /// <param name="firstBasePlugin">A base Plugin of that GameRelease, spelled as its set does.</param>
    /// <param name="secondBasePlugin">A second base Plugin, spelled in a different case.</param>
    /// <param name="thirdBasePlugin">A third base Plugin, spelled in a different case.</param>
    [Theory]
    [InlineData(GameRelease.SkyrimSE, "Skyrim.esm", "UPDATE.ESM", "ccQDRSSE001-SurvivalMode.esm")]
    [InlineData(GameRelease.SkyrimLE, "Skyrim.esm", "Dawnguard.esm", "hearthfires.esm")]
    [InlineData(GameRelease.SkyrimVR, "Skyrim.esm", "DRAGONBORN.esm", "ccBGSSSE001-Fish.esm")]
    [InlineData(GameRelease.SkyrimSEGog, "Skyrim.esm", "Update.esm", "dawnguard.ESM")]
    [InlineData(GameRelease.EnderalSE, "Skyrim.esm", "Dragonborn.esm", "ccBGSSSE001-Fish.esm")]
    [InlineData(GameRelease.EnderalLE, "skyrim.esm", "Update.esm", "HearthFires.esm")]
    [InlineData(GameRelease.Oblivion, "Oblivion.esm", "KNIGHTS.ESP", "DLCShiveringIsles.esp")]
    [InlineData(GameRelease.Fallout4, "Fallout4.esm", "DLCworkshop01.esm", "dlccoast.esm")]
    [InlineData(GameRelease.Fallout4VR, "Fallout4.esm", "DLCRobot.esm", "dlcnukaworld.esm")]
    [InlineData(GameRelease.Starfield, "Starfield.esm", "OldMars.esm", "constellation.esm")]
    public async Task RefreshAsync_AdvancedModeOff_HidesRealBaseGamePluginsForRelease(
        GameRelease gameRelease,
        string firstBasePlugin,
        string secondBasePlugin,
        string thirdBasePlugin)
    {
        var discovery = new DeterministicPluginListGameLoadOrders(
            firstBasePlugin,
            "User.esp",
            secondBasePlugin,
            thirdBasePlugin);
        using var sut = new PluginList(discovery);

        await sut.RefreshAsync(
            gameRelease,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        var confirmed = Assert.IsType<ConfirmedPluginList>(sut.Current.Confirmed);
        Assert.Equal(["User.esp"], confirmed.Entries.Select(entry => entry.Name).ToArray());
    }

    /// <summary>
    ///     Verifies a GameRelease that is not in the Supported GameRelease table is rejected rather than refreshed.
    ///     Oblivion Remastered is defined by Mutagen but unsupported here; it previously produced a Plugin List that
    ///     hid nothing, because the Base Game Plugin lookup answered with an empty set (ADR-0003).
    /// </summary>
    [Fact]
    public async Task RefreshAsync_UnsupportedRelease_IsRejected()
    {
        var discovery = new DeterministicPluginListGameLoadOrders("Oblivion.esm", "User.esp");
        using var sut = new PluginList(discovery);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.RefreshAsync(
            GameRelease.OblivionRE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken));

        Assert.Null(sut.Current.Confirmed);
    }

    /// <summary>
    ///     Verifies individual intent matches case-insensitively and materializes selection in Plugin List order.
    /// </summary>
    [Fact]
    public async Task Apply_CurrentIndividualIntent_PublishesCaseInsensitiveSelectionInPluginListOrder()
    {
        var discovery = new DeterministicPluginListGameLoadOrders("First.esp", "Second.esp");
        using var sut = new PluginList(discovery);
        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var beforeSelection = sut.Current;
        var changedCount = 0;
        sut.Changed += (_, _) => changedCount++;

        sut.Apply(
            new PluginSelectionByNameIntent(
                beforeSelection.Confirmed!.MembershipVersion,
                "second.ESP",
                true));
        sut.Apply(
            new PluginSelectionByNameIntent(
                beforeSelection.Confirmed.MembershipVersion,
                "FIRST.ESP",
                true));

        Assert.Equal(beforeSelection.StateRevision + 2, sut.Current.StateRevision);
        Assert.Equal(beforeSelection.ActivityRevision, sut.Current.ActivityRevision);
        Assert.Equal(["First.esp", "Second.esp"], sut.Current.Confirmed!.SelectedPluginNames);
        Assert.Equal(2, changedCount);
    }

    /// <summary>
    ///     Verifies whole-list selection derives its target from the complete confirmed membership.
    /// </summary>
    [Fact]
    public async Task Apply_CurrentWholeListIntent_SelectsCompleteConfirmedMembershipInOrder()
    {
        var discovery = new DeterministicPluginListGameLoadOrders("First.esp", "Second.esp", "Third.esp");
        using var sut = new PluginList(discovery);
        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var membershipVersion = sut.Current.Confirmed!.MembershipVersion;

        sut.Apply(new PluginSelectionForAllIntent(membershipVersion, true));

        Assert.Equal(
            ["First.esp", "Second.esp", "Third.esp"],
            sut.Current.Confirmed!.SelectedPluginNames);
    }

    /// <summary>
    ///     Verifies whole-list deselection clears every selected Plugin from a partial selection.
    /// </summary>
    [Fact]
    public async Task Apply_WholeListDeselection_ClearsPartialSelection()
    {
        var discovery = new DeterministicPluginListGameLoadOrders("First.esp", "Second.esp");
        using var sut = new PluginList(discovery);
        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var membershipVersion = sut.Current.Confirmed!.MembershipVersion;
        sut.Apply(new PluginSelectionByNameIntent(membershipVersion, "First.esp", true));
        var partiallySelectedRevision = sut.Current.StateRevision;

        sut.Apply(new PluginSelectionForAllIntent(membershipVersion, false));

        Assert.Empty(sut.Current.Confirmed!.SelectedPluginNames);
        Assert.Equal(partiallySelectedRevision + 1, sut.Current.StateRevision);
    }

    /// <summary>
    ///     Verifies whole-list intent over empty confirmed membership does not publish a redundant state.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_WholeListIntent_EmptyMembershipDoesNotPublishRedundantState(bool isSelected)
    {
        var discovery = new DeterministicPluginListGameLoadOrders();
        using var sut = new PluginList(discovery);
        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var unchanged = sut.Current;
        var changedCount = 0;
        sut.Changed += (_, _) => changedCount++;

        sut.Apply(new PluginSelectionForAllIntent(unchanged.Confirmed!.MembershipVersion, isSelected));

        Assert.Same(unchanged, sut.Current);
        Assert.Equal(0, changedCount);
    }

    /// <summary>
    ///     Verifies same-source discovery reconciles selection without retaining removed Plugin intent.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_SameSource_ReconcilesSelectionWithNewMembershipOrderAndCasing()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var initial = discovery.Enqueue();
        var refreshed = discovery.Enqueue();
        var reappeared = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        var gameDirectory = CreateGameDirectory();

        var initialRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        initial.Complete("StayA.esp", "Removed.esp", "StayB.esp");
        await initialRefresh;
        sut.Apply(new PluginSelectionForAllIntent(sut.Current.Confirmed!.MembershipVersion, true));
        var initialSnapshot = sut.Current.Confirmed;

        var sameSourceRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        refreshed.Complete("stayb.ESP", "New.esp", "staya.ESP");
        await sameSourceRefresh;

        Assert.Equal(["stayb.ESP", "staya.ESP"], sut.Current.Confirmed!.SelectedPluginNames);
        Assert.Equal(
            ["StayA.esp", "Removed.esp", "StayB.esp"],
            initialSnapshot.SelectedPluginNames);

        var reappearanceRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        reappeared.Complete("Removed.esp", "STAYA.esp", "STAYB.esp");
        await reappearanceRefresh;

        Assert.Equal(["STAYA.esp", "STAYB.esp"], sut.Current.Confirmed!.SelectedPluginNames);
    }

    /// <summary>
    ///     Verifies rejected and already-satisfied intent does not publish another immutable state.
    /// </summary>
    [Fact]
    public async Task Apply_RejectedAndAlreadySatisfiedIntent_DoesNotPublishRedundantState()
    {
        var discovery = new DeterministicPluginListGameLoadOrders("First.esp", "Second.esp");
        using var sut = new PluginList(discovery);
        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var staleMembershipVersion = sut.Current.Confirmed!.MembershipVersion;
        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var unchanged = sut.Current;
        var membershipVersion = unchanged.Confirmed!.MembershipVersion;
        var changedCount = 0;
        sut.Changed += (_, _) => changedCount++;

        sut.Apply(new PluginSelectionByNameIntent(staleMembershipVersion, "First.esp", true));
        sut.Apply(new PluginSelectionForAllIntent(staleMembershipVersion, true));
        sut.Apply(new PluginSelectionByNameIntent(membershipVersion, "Absent.esp", true));
        sut.Apply(new PluginSelectionByNameIntent(membershipVersion, "First.esp", false));
        sut.Apply(new PluginSelectionForAllIntent(membershipVersion, false));

        Assert.Same(unchanged, sut.Current);
        Assert.Equal(0, changedCount);

        sut.Apply(new PluginSelectionByNameIntent(membershipVersion, "First.esp", true));
        var selected = sut.Current;
        sut.Apply(new PluginSelectionByNameIntent(membershipVersion, "FIRST.ESP", true));

        Assert.Same(selected, sut.Current);
        Assert.Equal(1, changedCount);
    }

    /// <summary>
    ///     Verifies later selection mutation cannot alter a previously captured selected-name snapshot.
    /// </summary>
    [Fact]
    public async Task Apply_LaterSelectionMutation_DoesNotChangeCapturedSnapshot()
    {
        var discovery = new DeterministicPluginListGameLoadOrders("First.esp", "Second.esp");
        using var sut = new PluginList(discovery);
        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var membershipVersion = sut.Current.Confirmed!.MembershipVersion;
        sut.Apply(new PluginSelectionForAllIntent(membershipVersion, true));
        var selectedSnapshot = sut.Current.Confirmed;

        sut.Apply(new PluginSelectionForAllIntent(membershipVersion, false));

        Assert.Empty(sut.Current.Confirmed!.SelectedPluginNames);
        Assert.Equal(["First.esp", "Second.esp"], selectedSnapshot.SelectedPluginNames);
    }

    /// <summary>
    ///     Verifies selection applied during same-source discovery participates in that refresh commit.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_SelectionAppliedDuringSameSourceRefresh_ParticipatesInCommit()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var initial = discovery.Enqueue();
        var refreshed = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        var gameDirectory = CreateGameDirectory();
        var initialRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        initial.Complete("First.esp", "Second.esp");
        await initialRefresh;

        var sameSourceRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var beforeSelection = Assert.IsType<PluginListRefreshingState>(sut.Current);
        sut.Apply(
            new PluginSelectionByNameIntent(
                sut.Current.Confirmed!.MembershipVersion,
                "second.ESP",
                true));
        var afterSelection = Assert.IsType<PluginListRefreshingState>(sut.Current);

        Assert.Equal(beforeSelection.StateRevision + 1, afterSelection.StateRevision);
        Assert.Equal(beforeSelection.ActivityRevision, afterSelection.ActivityRevision);
        Assert.Equal(beforeSelection.Source, afterSelection.Source);
        Assert.Equal(beforeSelection.ScannedCount, afterSelection.ScannedCount);
        Assert.Equal(beforeSelection.TotalCount, afterSelection.TotalCount);
        Assert.Equal(["Second.esp"], afterSelection.Confirmed!.SelectedPluginNames);

        refreshed.Complete("SECOND.esp", "First.esp", "New.esp");
        await sameSourceRefresh;

        Assert.Equal(["SECOND.esp"], sut.Current.Confirmed!.SelectedPluginNames);
    }

    [Fact]
    public async Task RefreshAsync_ExpectedDiscoveryFailure_PublishesUiNeutralFailureWithoutConfirmedList()
    {
        var failure = PluginListGameLoadOrdersStub.LocalAccessFailure("The local Plugin List could not be read.");
        var discovery = new FixedPluginListGameLoadOrders(failure);
        using var sut = new PluginList(discovery);
        var publishedStates = new List<PluginListState>();
        sut.Changed += (_, _) => publishedStates.Add(sut.Current);

        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        Assert.Null(sut.Current.Confirmed);
        var failed = Assert.IsType<PluginListFailedState>(sut.Current);
        Assert.Equal("The local Plugin List could not be read.", failed.ErrorMessage);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, discovery.GameDirectory), failed.Source);
        Assert.Collection(
            publishedStates,
            state =>
            {
                Assert.Equal(1, state.StateRevision);
                Assert.Equal(1, state.ActivityRevision);
                Assert.IsType<PluginListRefreshingState>(state);
            },
            state =>
            {
                Assert.Equal(2, state.StateRevision);
                Assert.Equal(2, state.ActivityRevision);
                Assert.IsType<PluginListFailedState>(state);
            });
    }

    [Fact]
    public async Task RefreshAsync_DifferentSource_SynchronouslyInvalidatesConfirmedPluginList()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var initial = discovery.Enqueue();
        var replacement = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        var firstDirectory = CreateGameDirectory();
        var secondDirectory = CreateGameDirectory();

        var initialRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            firstDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        initial.Complete("Initial.esp");
        await initialRefresh;
        Assert.NotNull(sut.Current.Confirmed);

        var replacementRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            secondDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        Assert.False(replacementRefresh.IsCompleted);
        Assert.Null(sut.Current.Confirmed);
        var refreshing = Assert.IsType<PluginListRefreshingState>(sut.Current);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, secondDirectory), refreshing.Source);

        replacement.Fail("The replacement source could not be read.");
        await replacementRefresh;

        Assert.Null(sut.Current.Confirmed);
        var failed = Assert.IsType<PluginListFailedState>(sut.Current);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, secondDirectory), failed.Source);
        Assert.Equal("The replacement source could not be read.", failed.ErrorMessage);
    }

    [Fact]
    public async Task RefreshAsync_SameSource_RetainsConfirmedPluginListWhileDiscoveryRuns()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var initial = discovery.Enqueue();
        var replacement = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        var gameDirectory = CreateGameDirectory();

        var initialRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        initial.Complete("Initial.esp");
        await initialRefresh;
        var initialConfirmed = Assert.IsType<ConfirmedPluginList>(sut.Current.Confirmed);

        var replacementRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.On,
            TestContext.Current.CancellationToken);

        Assert.False(replacementRefresh.IsCompleted);
        Assert.Same(initialConfirmed, sut.Current.Confirmed);
        var refreshing = Assert.IsType<PluginListRefreshingState>(sut.Current);
        Assert.Equal(initialConfirmed.Source, refreshing.Source);

        replacement.Complete("Initial.esp", "Replacement.esp");
        await replacementRefresh;
    }

    [Fact]
    public async Task RefreshAsync_SameSourceExpectedFailure_RetainsConfirmedPluginList()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var initial = discovery.Enqueue();
        var replacement = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        var gameDirectory = CreateGameDirectory();

        var initialRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        initial.Complete("Initial.esp");
        await initialRefresh;
        var initialConfirmed = Assert.IsType<ConfirmedPluginList>(sut.Current.Confirmed);

        var replacementRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        replacement.Fail("The refreshed Plugin List could not be read.");
        await replacementRefresh;

        Assert.Same(initialConfirmed, sut.Current.Confirmed);
        var failed = Assert.IsType<PluginListFailedState>(sut.Current);
        Assert.Equal(initialConfirmed.Source, failed.Source);
        Assert.Equal("The refreshed Plugin List could not be read.", failed.ErrorMessage);
    }

    [Fact]
    public async Task RefreshAsync_NewerRefreshOvertakesOlder_OnlyNewerResultCanPublish()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var older = discovery.Enqueue();
        var newer = discovery.Enqueue();
        using var sut = new PluginList(discovery);

        var olderRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var newerDirectory = CreateGameDirectory();
        var newerRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            newerDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        Assert.True(older.CancellationToken.IsCancellationRequested);
        newer.Complete("Newer.esp");
        await newerRefresh;
        var newerState = sut.Current;
        var newerConfirmed = Assert.IsType<ConfirmedPluginList>(newerState.Confirmed);
        Assert.Equal(["Newer.esp"], newerConfirmed.Entries.Select(entry => entry.Name).ToArray());

        older.ReportProgress(99, 100);
        older.Complete("Older.esp");
        await olderRefresh;

        Assert.Same(newerState, sut.Current);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, newerDirectory), newerConfirmed.Source);
    }

    /// <summary>
    ///     Verifies an older non-cooperative failure cannot replace a newer ready Plugin List state.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_OlderFailureAfterNewerReady_DoesNotOverwriteNewerState()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var older = discovery.Enqueue();
        var newer = discovery.Enqueue();
        using var sut = new PluginList(discovery);

        var olderRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var newerDirectory = CreateGameDirectory();
        var newerRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            newerDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        newer.Complete("Newer.esp");
        await newerRefresh;
        var newerState = sut.Current;
        var newerConfirmed = Assert.IsType<ConfirmedPluginList>(newerState.Confirmed);

        older.Fail("Older discovery failed after retirement.");
        await olderRefresh;

        Assert.Same(newerState, sut.Current);
        Assert.IsType<PluginListReadyState>(sut.Current);
        Assert.Equal(["Newer.esp"], newerConfirmed.Entries.Select(entry => entry.Name).ToArray());
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, newerDirectory), newerConfirmed.Source);
    }

    [Fact]
    public async Task RefreshAsync_CurrentCallerCancellation_PublishesCancelledAndPropagates()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var operation = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        using var callerCancellation = new CancellationTokenSource();
        var gameDirectory = CreateGameDirectory();
        var refresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            callerCancellation.Token);
        var refreshingStateRevision = sut.Current.StateRevision;
        var refreshingActivityRevision = sut.Current.ActivityRevision;

        callerCancellation.Cancel();
        operation.Complete("Ignored.esp");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Null(sut.Current.Confirmed);
        var cancelled = Assert.IsType<PluginListCancelledState>(sut.Current);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory), cancelled.Source);
        Assert.Equal(refreshingStateRevision + 1, sut.Current.StateRevision);
        Assert.Equal(refreshingActivityRevision + 1, sut.Current.ActivityRevision);
    }

    /// <summary>
    ///     Verifies caller cancellation retains membership only when it remains coherent with the refresh source.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_SameSourceCallerCancellation_RetainsConfirmedPluginList()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var initial = discovery.Enqueue();
        var cancelling = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        using var callerCancellation = new CancellationTokenSource();
        var gameDirectory = CreateGameDirectory();
        var initialRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        initial.Complete("Retained.esp");
        await initialRefresh;
        var confirmed = Assert.IsType<ConfirmedPluginList>(sut.Current.Confirmed);
        var refresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            callerCancellation.Token);

        Assert.Same(confirmed, Assert.IsType<PluginListRefreshingState>(sut.Current).Confirmed);
        callerCancellation.Cancel();
        cancelling.Complete("Ignored.esp");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        var cancelled = Assert.IsType<PluginListCancelledState>(sut.Current);
        Assert.Same(confirmed, cancelled.Confirmed);
        Assert.Equal(confirmed.Source, cancelled.Source);
    }

    [Fact]
    public async Task RefreshAsync_SupersededThenCallerCancelled_PropagatesWithoutPublishingCancellation()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var older = discovery.Enqueue();
        var newer = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        using var callerCancellation = new CancellationTokenSource();
        var olderRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            callerCancellation.Token);
        var newerRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var newerRefreshingState = sut.Current;

        callerCancellation.Cancel();
        older.Complete("Ignored.esp");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => olderRefresh);
        Assert.Same(newerRefreshingState, sut.Current);
        Assert.IsType<PluginListRefreshingState>(sut.Current);

        newer.Complete("Newer.esp");
        await newerRefresh;
    }

    /// <summary>
    ///     Verifies that a throwing cancellation callback from the retired refresh cannot leave its unstarted replacement
    ///     active or permanently expose refreshing state.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_RetirementCallbackFailure_CleansUpReplacementAndAllowsRetry()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var retired = discovery.Enqueue();
        var retry = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        var retiredRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var retirementFailure = new InvalidOperationException("Synthetic retirement callback failure.");
        var terminalNotificationFailure = new InvalidOperationException("Synthetic terminal notification failure.");
        using var registration = retired.CancellationToken.Register(
            () => throw retirementFailure);
        var terminalNotificationAttempted = false;
        sut.Changed += (_, _) =>
        {
            if (sut.Current is PluginListFaultedState)
            {
                terminalNotificationAttempted = true;
                throw terminalNotificationFailure;
            }
        };
        var replacementDirectory = CreateGameDirectory();

        var propagated = await Assert.ThrowsAsync<AggregateException>(
            () => sut.RefreshAsync(
                GameRelease.SkyrimSE,
                replacementDirectory,
                AdvancedMode.Off,
                TestContext.Current.CancellationToken));

        Assert.True(terminalNotificationAttempted);
        var primaryFailures = propagated.Flatten().InnerExceptions;
        Assert.Contains(retirementFailure, primaryFailures);
        Assert.DoesNotContain(terminalNotificationFailure, primaryFailures);
        Assert.True(retired.CancellationToken.IsCancellationRequested);
        var faulted = Assert.IsType<PluginListFaultedState>(sut.Current);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, replacementDirectory), faulted.Source);
        var faultedStateRevision = sut.Current.StateRevision;
        var faultedActivityRevision = sut.Current.ActivityRevision;

        retired.Cancel();
        await retiredRefresh;
        Assert.Equal(faultedStateRevision, sut.Current.StateRevision);
        Assert.Equal(faultedActivityRevision, sut.Current.ActivityRevision);
        var retryRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            replacementDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        retry.Complete("Retry.esp");
        await retryRefresh;

        var confirmed = Assert.IsType<ConfirmedPluginList>(sut.Current.Confirmed);
        Assert.Equal(["Retry.esp"], confirmed.Entries.Select(entry => entry.Name).ToArray());
    }

    [Fact]
    public async Task Invalidate_ActiveRefresh_RetiresWorkAndSuppressesLatePublication()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var operation = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        var refresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        sut.Invalidate();

        Assert.True(operation.CancellationToken.IsCancellationRequested);
        var invalidatedState = sut.Current;
        Assert.Null(invalidatedState.Confirmed);
        Assert.IsType<PluginListNoSourceState>(invalidatedState);

        operation.ReportProgress(50, 100);
        operation.Cancel();
        await refresh;

        Assert.Same(invalidatedState, sut.Current);
    }

    /// <summary>
    ///     Verifies that a throwing cancellation callback from the retired refresh still signals the already-authoritative
    ///     no-source state, so presentation cannot keep showing stale membership after a failed transition.
    /// </summary>
    [Fact]
    public async Task Invalidate_RetirementCallbackFailure_StillSignalsNoSourceState()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var operation = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        var refresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var retirementFailure = new InvalidOperationException("Synthetic retirement callback failure.");
        using var registration = operation.CancellationToken.Register(() => throw retirementFailure);
        PluginListState? signalledState = null;
        sut.Changed += (_, _) => signalledState = sut.Current;

        var propagated = Assert.Throws<AggregateException>(sut.Invalidate);

        Assert.Contains(retirementFailure, propagated.Flatten().InnerExceptions);
        Assert.NotNull(signalledState);
        Assert.Same(sut.Current, signalledState);
        Assert.Null(signalledState.Confirmed);
        Assert.IsType<PluginListNoSourceState>(signalledState);

        operation.Cancel();
        await refresh;
        Assert.Same(signalledState, sut.Current);
    }

    [Fact]
    public async Task Dispose_ActiveRefresh_IsIdempotentAndPreventsLaterPublication()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var operation = discovery.Enqueue();
        var sut = new PluginList(discovery);
        var refresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var stateBeforeDisposal = sut.Current;

        sut.Dispose();
        sut.Dispose();

        Assert.True(operation.CancellationToken.IsCancellationRequested);
        operation.ReportProgress(50, 100);
        operation.Cancel();
        await refresh;
        Assert.Same(stateBeforeDisposal, sut.Current);
        Assert.Throws<ObjectDisposedException>(sut.Invalidate);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sut.RefreshAsync(
                GameRelease.SkyrimSE,
                CreateGameDirectory(),
                AdvancedMode.Off,
                TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(
            () => sut.Apply(new PluginSelectionForAllIntent(1, true)));
    }

    [Fact]
    public async Task RefreshAsync_InvalidArguments_DoNotRetireCurrentRefresh()
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var operation = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        var validRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var validState = sut.Current;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.RefreshAsync(
                GameRelease.SkyrimSE,
                CreateGameDirectory(),
                (AdvancedMode)int.MaxValue,
                TestContext.Current.CancellationToken));

        Assert.False(operation.CancellationToken.IsCancellationRequested);
        Assert.Same(validState, sut.Current);
        operation.Complete("Valid.esp");
        await validRefresh;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshAsync_CurrentProgrammingAndFatalDiscoveryFailures_PublishFaultedAndPropagate(bool fatal)
    {
        var discovery = new ControlledPluginListGameLoadOrders();
        var initial = discovery.Enqueue();
        var faulting = discovery.Enqueue();
        using var sut = new PluginList(discovery);
        var gameDirectory = CreateGameDirectory();
        var initialRefresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        initial.Complete("Retained.esp");
        await initialRefresh;
        var confirmed = Assert.IsType<ConfirmedPluginList>(sut.Current.Confirmed);
        var refresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var refreshingStateRevision = sut.Current.StateRevision;
        var refreshingActivityRevision = sut.Current.ActivityRevision;
        var terminalNotificationAttempted = false;
        sut.Changed += (_, _) =>
        {
            terminalNotificationAttempted = true;
            throw new InvalidOperationException("Synthetic terminal notification failure.");
        };
        Exception exception = fatal
            ? new OutOfMemoryException("Synthetic fatal discovery failure.")
            : new InvalidOperationException("Synthetic programming failure.");

        faulting.Fault(exception);

        var propagated = await Record.ExceptionAsync(() => refresh);
        Assert.Same(exception, propagated);
        Assert.True(terminalNotificationAttempted);
        Assert.Same(confirmed, sut.Current.Confirmed);
        var faulted = Assert.IsType<PluginListFaultedState>(sut.Current);
        Assert.Equal(confirmed.Source, faulted.Source);
        Assert.Equal(refreshingStateRevision + 1, sut.Current.StateRevision);
        Assert.Equal(refreshingActivityRevision + 1, sut.Current.ActivityRevision);
    }

    /// <summary>
    ///     Verifies an exception from the synchronous raw progress observer becomes the current silent fault fact and
    ///     remains the propagated primary failure.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_CurrentProgressObserverFailure_PublishesFaultedAndPropagates()
    {
        var observerFailure = new IOException("progress observer failed");
        using var sut = new PluginList(new ProgressReportingPluginListGameLoadOrders());
        sut.Changed += (_, _) =>
        {
            if (sut.Current is PluginListRefreshingState { ScannedCount: > 0 })
            {
                throw observerFailure;
            }
        };

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken));

        Assert.Same(observerFailure, thrown);
        Assert.IsType<PluginListFaultedState>(sut.Current);
    }

    [Fact]
    public async Task Invalidate_ConfirmedPluginList_PublishesNoSourceState()
    {
        var discovery = new DeterministicPluginListGameLoadOrders("User.esp");
        using var sut = new PluginList(discovery);
        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var confirmedRevision = sut.Current.StateRevision;
        var confirmedActivityRevision = sut.Current.ActivityRevision;

        sut.Invalidate();

        Assert.Null(sut.Current.Confirmed);
        Assert.IsType<PluginListNoSourceState>(sut.Current);
        Assert.Equal(confirmedRevision + 1, sut.Current.StateRevision);
        Assert.Equal(confirmedActivityRevision + 1, sut.Current.ActivityRevision);
    }

    private sealed class DeterministicPluginListGameLoadOrders(params string[] pluginNames) : PluginListGameLoadOrdersStub
    {
        public string GameDirectory { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"deterministic-plugin-list-{Guid.NewGuid():N}");

        public string DataDirectory => System.IO.Path.Combine(GameDirectory, "Data");

        /// <inheritdoc />
        public override Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Discovered(pluginNames));
        }
    }

    private sealed class FixedPluginListGameLoadOrders(AvailablePluginsDiscoveryResult result) : PluginListGameLoadOrdersStub
    {
        public string GameDirectory { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fixed-plugin-list-{Guid.NewGuid():N}");

        /// <inheritdoc />
        public override Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class ProgressReportingPluginListGameLoadOrders : PluginListGameLoadOrdersStub
    {
        /// <inheritdoc />
        public override async Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new GameLoadOrderDiscoveryProgress(1, 1));
            return Discovered([]);
        }
    }

    private static string CreateGameDirectory()
    {
        return System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"controlled-plugin-list-{Guid.NewGuid():N}");
    }

    private sealed class ControlledPluginListGameLoadOrders : PluginListGameLoadOrdersStub
    {
        private readonly Queue<GameLoadOrdersStep> _steps = new();

        public GameLoadOrdersStep Enqueue()
        {
            var step = new GameLoadOrdersStep();
            _steps.Enqueue(step);
            return step;
        }

        /// <inheritdoc />
        public override Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var step = _steps.Dequeue();
            step.Start(gameRelease, canonicalDataDirectory, progress, cancellationToken);
            return step.Completion.Task;
        }
    }

    private sealed class GameLoadOrdersStep
    {
        private IProgress<GameLoadOrderDiscoveryProgress>? _progress;

        public TaskCompletionSource<AvailablePluginsDiscoveryResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken CancellationToken { get; private set; }

        public string? CanonicalDataDirectory { get; private set; }

        public GameRelease GameRelease { get; private set; }

        public IProgress<GameLoadOrderDiscoveryProgress>? Progress => _progress;

        public void Start(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IProgress<GameLoadOrderDiscoveryProgress>? progress,
            CancellationToken cancellationToken)
        {
            GameRelease = gameRelease;
            CanonicalDataDirectory = canonicalDataDirectory;
            _progress = progress;
            CancellationToken = cancellationToken;
        }

        public void Complete(params string[] pluginNames)
        {
            Completion.SetResult(PluginListGameLoadOrdersStub.Discovered(pluginNames));
        }

        public void Fail(string errorMessage)
        {
            Completion.SetResult(PluginListGameLoadOrdersStub.LocalAccessFailure(errorMessage));
        }

        public void Cancel()
        {
            Completion.SetCanceled(CancellationToken);
        }

        public void Fault(Exception exception)
        {
            Completion.SetException(exception);
        }

        public void ReportProgress(int scannedCount, int totalCount)
        {
            _progress?.Report(new GameLoadOrderDiscoveryProgress(scannedCount, totalCount));
        }
    }
}
