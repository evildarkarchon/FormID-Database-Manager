#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class PluginListTests
{
    [Fact]
    public void TypeShape_UsesOneInternalConcreteSealedModuleWithoutExternalMockInterface()
    {
        var pluginListType = typeof(PluginList);

        Assert.True(pluginListType.IsNotPublic);
        Assert.True(pluginListType.IsSealed);
        Assert.False(pluginListType.IsAbstract);
        Assert.Contains(typeof(IDisposable), pluginListType.GetInterfaces());
        Assert.True(typeof(IPluginListDiscovery).IsNotPublic);
        Assert.DoesNotContain(
            pluginListType.Assembly.GetTypes(),
            type => type.Name == "IPluginList");
    }

    [Fact]
    public async Task RefreshAsync_InitialDiscovery_PublishesImmutableConfirmedPluginListInPluginListOrder()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns(["Skyrim.esm"]);
        var discovery = new DeterministicPluginListDiscovery(
            "skyrim.ESM",
            "UserA.esp",
            "usera.ESP",
            "UserB.esp");
        using var sut = new PluginList(gameDetectionService.Object, discovery);
        var changedCount = 0;
        EventArgs? lastEventArgs = null;
        var publishedActivities = new List<PluginListActivity>();
        sut.Changed += (_, eventArgs) =>
        {
            changedCount++;
            lastEventArgs = eventArgs;
            publishedActivities.Add(sut.Current.Activity);
        };

        Assert.Equal(0, sut.Current.StateRevision);
        Assert.Null(sut.Current.Confirmed);
        Assert.IsType<PluginListNoSourceActivity>(sut.Current.Activity);

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
        Assert.IsType<PluginListReadyActivity>(current.Activity);
        Assert.True(current.StateRevision >= 2);
        Assert.True(changedCount >= 2);
        Assert.Same(EventArgs.Empty, lastEventArgs);
        Assert.Contains(publishedActivities, activity => activity is PluginListRefreshingActivity);
        Assert.Contains(publishedActivities, activity => activity is PluginListReadyActivity);
    }

    [Fact]
    public async Task RefreshAsync_AdvancedMode_IncludesBasePlugins()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns(["Skyrim.esm"]);
        var discovery = new DeterministicPluginListDiscovery("skyrim.ESM", "User.esp");
        using var sut = new PluginList(gameDetectionService.Object, discovery);

        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.On,
            TestContext.Current.CancellationToken);

        var confirmed = Assert.IsType<ConfirmedPluginList>(sut.Current.Confirmed);
        Assert.Equal(["skyrim.ESM", "User.esp"], confirmed.Entries.Select(entry => entry.Name).ToArray());
    }

    /// <summary>
    ///     Verifies individual intent matches case-insensitively and materializes selection in Plugin List order.
    /// </summary>
    [Fact]
    public async Task Apply_CurrentIndividualIntent_PublishesCaseInsensitiveSelectionInPluginListOrder()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new DeterministicPluginListDiscovery("First.esp", "Second.esp");
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        Assert.Equal(["First.esp", "Second.esp"], sut.Current.Confirmed!.SelectedPluginNames);
        Assert.Equal(2, changedCount);
    }

    /// <summary>
    ///     Verifies whole-list selection derives its target from the complete confirmed membership.
    /// </summary>
    [Fact]
    public async Task Apply_CurrentWholeListIntent_SelectsCompleteConfirmedMembershipInOrder()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new DeterministicPluginListDiscovery("First.esp", "Second.esp", "Third.esp");
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new DeterministicPluginListDiscovery("First.esp", "Second.esp");
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new DeterministicPluginListDiscovery();
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new ControlledPluginListDiscovery();
        var initial = discovery.Enqueue();
        var refreshed = discovery.Enqueue();
        var reappeared = discovery.Enqueue();
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new DeterministicPluginListDiscovery("First.esp", "Second.esp");
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new DeterministicPluginListDiscovery("First.esp", "Second.esp");
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new ControlledPluginListDiscovery();
        var initial = discovery.Enqueue();
        var refreshed = discovery.Enqueue();
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        sut.Apply(
            new PluginSelectionByNameIntent(
                sut.Current.Confirmed!.MembershipVersion,
                "second.ESP",
                true));
        refreshed.Complete("SECOND.esp", "First.esp", "New.esp");
        await sameSourceRefresh;

        Assert.Equal(["SECOND.esp"], sut.Current.Confirmed!.SelectedPluginNames);
    }

    [Fact]
    public async Task RefreshAsync_ExpectedDiscoveryFailure_PublishesUiNeutralFailureWithoutConfirmedList()
    {
        var failure = PluginListDiscoveryResult.Failed("The local Plugin List could not be read.");
        var discovery = new FixedPluginListDiscovery(failure);
        using var sut = new PluginList(new Mock<GameDetectionService>().Object, discovery);

        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        Assert.Null(sut.Current.Confirmed);
        var failed = Assert.IsType<PluginListFailedActivity>(sut.Current.Activity);
        Assert.Equal("The local Plugin List could not be read.", failed.ErrorMessage);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, discovery.GameDirectory), failed.Source);
    }

    [Fact]
    public async Task RefreshAsync_DifferentSource_SynchronouslyInvalidatesConfirmedPluginList()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new ControlledPluginListDiscovery();
        var initial = discovery.Enqueue();
        var replacement = discovery.Enqueue();
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        var refreshing = Assert.IsType<PluginListRefreshingActivity>(sut.Current.Activity);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, secondDirectory), refreshing.Source);

        replacement.Fail("The replacement source could not be read.");
        await replacementRefresh;

        Assert.Null(sut.Current.Confirmed);
        var failed = Assert.IsType<PluginListFailedActivity>(sut.Current.Activity);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, secondDirectory), failed.Source);
        Assert.Equal("The replacement source could not be read.", failed.ErrorMessage);
    }

    [Fact]
    public async Task RefreshAsync_SameSource_RetainsConfirmedPluginListWhileDiscoveryRuns()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new ControlledPluginListDiscovery();
        var initial = discovery.Enqueue();
        var replacement = discovery.Enqueue();
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        var refreshing = Assert.IsType<PluginListRefreshingActivity>(sut.Current.Activity);
        Assert.Equal(initialConfirmed.Source, refreshing.Source);

        replacement.Complete("Initial.esp", "Replacement.esp");
        await replacementRefresh;
    }

    [Fact]
    public async Task RefreshAsync_SameSourceExpectedFailure_RetainsConfirmedPluginList()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new ControlledPluginListDiscovery();
        var initial = discovery.Enqueue();
        var replacement = discovery.Enqueue();
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        var failed = Assert.IsType<PluginListFailedActivity>(sut.Current.Activity);
        Assert.Equal(initialConfirmed.Source, failed.Source);
        Assert.Equal("The refreshed Plugin List could not be read.", failed.ErrorMessage);
    }

    [Fact]
    public async Task RefreshAsync_NewerRefreshOvertakesOlder_OnlyNewerResultCanPublish()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new ControlledPluginListDiscovery();
        var older = discovery.Enqueue();
        var newer = discovery.Enqueue();
        using var sut = new PluginList(gameDetectionService.Object, discovery);

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
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new ControlledPluginListDiscovery();
        var older = discovery.Enqueue();
        var newer = discovery.Enqueue();
        using var sut = new PluginList(gameDetectionService.Object, discovery);

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
        Assert.IsType<PluginListReadyActivity>(sut.Current.Activity);
        Assert.Equal(["Newer.esp"], newerConfirmed.Entries.Select(entry => entry.Name).ToArray());
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, newerDirectory), newerConfirmed.Source);
    }

    [Fact]
    public async Task RefreshAsync_CurrentCallerCancellation_PublishesCancelledAndPropagates()
    {
        var discovery = new ControlledPluginListDiscovery();
        var operation = discovery.Enqueue();
        using var sut = new PluginList(new Mock<GameDetectionService>().Object, discovery);
        using var callerCancellation = new CancellationTokenSource();
        var gameDirectory = CreateGameDirectory();
        var refresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            gameDirectory,
            AdvancedMode.Off,
            callerCancellation.Token);

        callerCancellation.Cancel();
        operation.Complete("Ignored.esp");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Null(sut.Current.Confirmed);
        var cancelled = Assert.IsType<PluginListCancelledActivity>(sut.Current.Activity);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory), cancelled.Source);
    }

    [Fact]
    public async Task RefreshAsync_SupersededThenCallerCancelled_PropagatesWithoutPublishingCancellation()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new ControlledPluginListDiscovery();
        var older = discovery.Enqueue();
        var newer = discovery.Enqueue();
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        Assert.IsType<PluginListRefreshingActivity>(sut.Current.Activity);

        newer.Complete("Newer.esp");
        await newerRefresh;
    }

    [Fact]
    public async Task Invalidate_ActiveRefresh_RetiresWorkAndSuppressesLatePublication()
    {
        var discovery = new ControlledPluginListDiscovery();
        var operation = discovery.Enqueue();
        using var sut = new PluginList(new Mock<GameDetectionService>().Object, discovery);
        var refresh = sut.RefreshAsync(
            GameRelease.SkyrimSE,
            CreateGameDirectory(),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        sut.Invalidate();

        Assert.True(operation.CancellationToken.IsCancellationRequested);
        var invalidatedState = sut.Current;
        Assert.Null(invalidatedState.Confirmed);
        Assert.IsType<PluginListNoSourceActivity>(invalidatedState.Activity);

        operation.ReportProgress(50, 100);
        operation.Cancel();
        await refresh;

        Assert.Same(invalidatedState, sut.Current);
    }

    [Fact]
    public async Task Dispose_ActiveRefresh_IsIdempotentAndPreventsLaterPublication()
    {
        var discovery = new ControlledPluginListDiscovery();
        var operation = discovery.Enqueue();
        var sut = new PluginList(new Mock<GameDetectionService>().Object, discovery);
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
        var discovery = new ControlledPluginListDiscovery();
        var operation = discovery.Enqueue();
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new ControlledPluginListDiscovery();
        var initial = discovery.Enqueue();
        var faulting = discovery.Enqueue();
        using var sut = new PluginList(gameDetectionService.Object, discovery);
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
        var faulted = Assert.IsType<PluginListFaultedActivity>(sut.Current.Activity);
        Assert.Equal(confirmed.Source, faulted.Source);
    }

    [Fact]
    public async Task Invalidate_ConfirmedPluginList_PublishesNoSourceState()
    {
        var discovery = new DeterministicPluginListDiscovery("User.esp");
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        using var sut = new PluginList(gameDetectionService.Object, discovery);
        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var confirmedRevision = sut.Current.StateRevision;

        sut.Invalidate();

        Assert.Null(sut.Current.Confirmed);
        Assert.IsType<PluginListNoSourceActivity>(sut.Current.Activity);
        Assert.True(sut.Current.StateRevision > confirmedRevision);
    }

    private sealed class DeterministicPluginListDiscovery(params string[] pluginNames) : IPluginListDiscovery
    {
        public string GameDirectory { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"deterministic-plugin-list-{Guid.NewGuid():N}");

        public string DataDirectory => System.IO.Path.Combine(GameDirectory, "Data");

        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PluginListDiscoveryResult.Completed(pluginNames));
        }
    }

    private sealed class FixedPluginListDiscovery(PluginListDiscoveryResult result) : IPluginListDiscovery
    {
        public string GameDirectory { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fixed-plugin-list-{Guid.NewGuid():N}");

        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private static string CreateGameDirectory()
    {
        return System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"controlled-plugin-list-{Guid.NewGuid():N}");
    }

    private sealed class ControlledPluginListDiscovery : IPluginListDiscovery
    {
        private readonly Queue<DiscoveryStep> _steps = new();

        public DiscoveryStep Enqueue()
        {
            var step = new DiscoveryStep();
            _steps.Enqueue(step);
            return step;
        }

        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var step = _steps.Dequeue();
            step.Start(progress, cancellationToken);
            return step.Completion.Task;
        }
    }

    private sealed class DiscoveryStep
    {
        private IProgress<PluginListDiscoveryProgress>? _progress;

        public TaskCompletionSource<PluginListDiscoveryResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken CancellationToken { get; private set; }

        public void Start(
            IProgress<PluginListDiscoveryProgress>? progress,
            CancellationToken cancellationToken)
        {
            _progress = progress;
            CancellationToken = cancellationToken;
        }

        public void Complete(params string[] pluginNames)
        {
            Completion.SetResult(PluginListDiscoveryResult.Completed(pluginNames));
        }

        public void Fail(string errorMessage)
        {
            Completion.SetResult(PluginListDiscoveryResult.Failed(errorMessage));
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
            _progress?.Report(new PluginListDiscoveryProgress(scannedCount, totalCount));
        }
    }
}
