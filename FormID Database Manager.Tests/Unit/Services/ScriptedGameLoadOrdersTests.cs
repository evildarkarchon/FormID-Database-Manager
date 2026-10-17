using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Drives a real Plugin List over the shared <see cref="ScriptedGameLoadOrders" /> fake into each of its states, so
///     tests that need a Plugin List state reach it through the Plugin List rather than by building snapshots (ADR-0009).
/// </summary>
public sealed class ScriptedGameLoadOrdersTests
{
    [Fact]
    public async Task RefreshAsync_FixedDiscovery_PublishesReadyStateWithThatPluginSet()
    {
        using var sut = new PluginList(ScriptedGameLoadOrders.Fixed("UserA.esp", "UserB.esp"));
        var gameDirectory = CreateGameDirectory();

        await sut.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        var ready = Assert.IsType<PluginListReadyState>(sut.Current);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory), ready.Source);
        Assert.Equal(["UserA.esp", "UserB.esp"], EntryNames(ready.Confirmed));
    }

    [Fact]
    public async Task RefreshAsync_SequencedDiscoveries_PublishSuccessiveResults()
    {
        using var sut = new PluginList(ScriptedGameLoadOrders.Sequenced(
            ScriptedDiscovery.Completes("First.esp"),
            ScriptedDiscovery.Completes("Second.esp", "Third.esp")));
        var gameDirectory = CreateGameDirectory();

        await sut.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var first = EntryNames(sut.Current.Confirmed);
        await sut.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        Assert.Equal(["First.esp"], first);
        Assert.IsType<PluginListReadyState>(sut.Current);
        Assert.Equal(["Second.esp", "Third.esp"], EntryNames(sut.Current.Confirmed));
    }

    /// <summary>
    ///     Verifies a script that runs out of steps faults discovery with a message naming the test mistake, rather than
    ///     inventing a result.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_SequenceExhausted_FaultsWithInvalidOperation()
    {
        using var sut = new PluginList(ScriptedGameLoadOrders.Sequenced(ScriptedDiscovery.Completes("Only.esp")));
        var gameDirectory = CreateGameDirectory();
        await sut.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RefreshAsync(GameRelease.SkyrimSE,
            gameDirectory, AdvancedMode.Off, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefreshAsync_HeldFirstDiscovery_StaysLoadingUntilReleased()
    {
        var held = ScriptedDiscovery.Held("Released.esp");
        using var sut = new PluginList(ScriptedGameLoadOrders.Sequenced(held));
        var gameDirectory = CreateGameDirectory();

        var refresh = sut.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        var loading = Assert.IsType<PluginListRefreshingState>(sut.Current);
        Assert.Null(loading.Confirmed);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory), loading.Source);
        Assert.False(refresh.IsCompleted);

        held.Release();
        await refresh;

        Assert.IsType<PluginListReadyState>(sut.Current);
        Assert.Equal(["Released.esp"], EntryNames(sut.Current.Confirmed));
    }

    [Fact]
    public async Task RefreshAsync_HeldSameSourceDiscovery_RefreshesWithConfirmedPluginListUntilReleased()
    {
        var held = ScriptedDiscovery.Held("Kept.esp", "Added.esp");
        using var sut = new PluginList(ScriptedGameLoadOrders.Sequenced(
            ScriptedDiscovery.Completes("Kept.esp"),
            held));
        var gameDirectory = CreateGameDirectory();
        await sut.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var confirmed = Assert.IsType<ConfirmedPluginList>(sut.Current.Confirmed);

        var refresh = sut.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        var refreshing = Assert.IsType<PluginListRefreshingState>(sut.Current);
        Assert.Same(confirmed, refreshing.Confirmed);
        Assert.Equal(confirmed.Source, refreshing.Source);

        held.Release();
        await refresh;

        Assert.IsType<PluginListReadyState>(sut.Current);
        Assert.Equal(["Kept.esp", "Added.esp"], EntryNames(sut.Current.Confirmed));
    }

    /// <summary>
    ///     Verifies a held discovery is cooperative like the production adapter: a newer refresh retires it, so the older
    ///     refresh ends as routine retirement without the test having to release it.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_HeldDiscoverySuperseded_EndsWithoutPublishing()
    {
        var held = ScriptedDiscovery.Held("Never.esp");
        using var sut = new PluginList(ScriptedGameLoadOrders.Sequenced(
            held,
            ScriptedDiscovery.Completes("Newer.esp")));
        var olderRefresh = sut.RefreshAsync(GameRelease.SkyrimSE, CreateGameDirectory(), AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        await sut.RefreshAsync(GameRelease.SkyrimSE, CreateGameDirectory(), AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        await olderRefresh;

        Assert.IsType<PluginListReadyState>(sut.Current);
        Assert.Equal(["Newer.esp"], EntryNames(sut.Current.Confirmed));
    }

    /// <summary>
    ///     Verifies No Source is reachable both before any discovery and by invalidating a Confirmed Plugin List.
    /// </summary>
    [Fact]
    public async Task Invalidate_AfterFixedDiscovery_PublishesNoSourceState()
    {
        using var sut = new PluginList(ScriptedGameLoadOrders.Fixed("User.esp"));
        Assert.IsType<PluginListNoSourceState>(sut.Current);
        await sut.RefreshAsync(GameRelease.SkyrimSE, CreateGameDirectory(), AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        sut.Invalidate();

        Assert.IsType<PluginListNoSourceState>(sut.Current);
        Assert.Null(sut.Current.Confirmed);
    }

    [Fact]
    public async Task RefreshAsync_FailingDiscovery_PublishesFailedState()
    {
        using var sut = new PluginList(ScriptedGameLoadOrders.Sequenced(
            ScriptedDiscovery.Fails("plugins.txt could not be read.")));
        var gameDirectory = CreateGameDirectory();

        await sut.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PluginListFailedState>(sut.Current);
        Assert.Null(failed.Confirmed);
        Assert.Equal("plugins.txt could not be read.", failed.ErrorMessage);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory), failed.Source);
    }

    /// <summary>
    ///     Verifies a cancelling discovery stays in flight until the caller cancels, because the Plugin List publishes
    ///     Cancelled only for caller cancellation.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_CancellingDiscoveryCallerCancels_PublishesCancelledStateAndPropagates()
    {
        using var sut = new PluginList(ScriptedGameLoadOrders.Sequenced(ScriptedDiscovery.Cancels()));
        using var callerCancellation = new CancellationTokenSource();
        var gameDirectory = CreateGameDirectory();
        var refresh = sut.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
            callerCancellation.Token);
        Assert.IsType<PluginListRefreshingState>(sut.Current);

        await callerCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        var cancelled = Assert.IsType<PluginListCancelledState>(sut.Current);
        Assert.Null(cancelled.Confirmed);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory), cancelled.Source);
    }

    [Fact]
    public async Task RefreshAsync_CancellingDiscoveryPreCancelledCaller_PublishesCancelledState()
    {
        using var sut = new PluginList(ScriptedGameLoadOrders.Sequenced(ScriptedDiscovery.Cancels()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.RefreshAsync(GameRelease.SkyrimSE,
            CreateGameDirectory(), AdvancedMode.Off, new CancellationToken(true)));

        Assert.IsType<PluginListCancelledState>(sut.Current);
    }

    [Fact]
    public async Task RefreshAsync_FaultingDiscovery_PublishesFaultedStateAndPropagates()
    {
        var fault = new IOException("Unexpected discovery failure.");
        using var sut = new PluginList(ScriptedGameLoadOrders.Sequenced(ScriptedDiscovery.Faults(fault)));
        var gameDirectory = CreateGameDirectory();

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.RefreshAsync(GameRelease.SkyrimSE,
            gameDirectory, AdvancedMode.Off, TestContext.Current.CancellationToken));

        Assert.Same(fault, thrown);
        var faulted = Assert.IsType<PluginListFaultedState>(sut.Current);
        Assert.Null(faulted.Confirmed);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory), faulted.Source);
    }

    /// <summary>
    ///     Verifies each unsuccessful terminal step, scripted after a same-source completion, leaves the Plugin List in
    ///     that terminal state while it retains the Confirmed Plugin List.
    /// </summary>
    [Theory]
    [InlineData(nameof(PluginListFailedState))]
    [InlineData(nameof(PluginListCancelledState))]
    [InlineData(nameof(PluginListFaultedState))]
    public async Task RefreshAsync_SameSourceTerminalDiscovery_RetainsConfirmedPluginList(string terminalState)
    {
        var terminalStep = terminalState switch
        {
            nameof(PluginListFailedState) => ScriptedDiscovery.Fails("plugins.txt could not be read."),
            nameof(PluginListCancelledState) => ScriptedDiscovery.Cancels(),
            _ => ScriptedDiscovery.Faults(new IOException("Unexpected discovery failure."))
        };
        using var sut = new PluginList(ScriptedGameLoadOrders.Sequenced(
            ScriptedDiscovery.Completes("Retained.esp"),
            terminalStep));
        var gameDirectory = CreateGameDirectory();
        await sut.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var confirmed = Assert.IsType<ConfirmedPluginList>(sut.Current.Confirmed);

        try
        {
            // Cancelled is published only for caller cancellation, so that case refreshes with a cancelled token.
            await sut.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
                new CancellationToken(terminalState == nameof(PluginListCancelledState)));
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // Cancelled and Faulted refreshes propagate by contract; only the published state is under test here.
        }

        Assert.Equal(terminalState, sut.Current.GetType().Name);
        Assert.Same(confirmed, sut.Current.Confirmed);
    }

    /// <summary>
    ///     Returns a game directory under the temporary directory that is never created, so no test reads an installed
    ///     game or touches disk.
    /// </summary>
    private static string CreateGameDirectory()
    {
        return Path.Combine(Path.GetTempPath(), $"scripted-game-load-orders-{Guid.NewGuid():N}");
    }

    private static string[] EntryNames(ConfirmedPluginList? confirmed)
    {
        return Assert.IsType<ConfirmedPluginList>(confirmed).Entries.Select(entry => entry.Name).ToArray();
    }
}
