using System.Security;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class GameLoadOrdersTests
{
    private static readonly string DataDirectory = Path.Combine(
        Path.GetTempPath(),
        "FormIdManager-GameLoadOrders-InMemory",
        "Data");

    /// <summary>
    ///     Verifies that discovery filters unavailable files without disturbing Game Load Order order.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_MixedAvailability_ReturnsAvailablePluginsInGameLoadOrderOrder()
    {
        var environment = new InMemoryGameLoadOrderEnvironment()
            .WithLoadOrder(
                GameRelease.SkyrimSE,
                DataDirectory,
                "First.esp",
                "Unavailable.esp",
                "Third.esp")
            .WithAvailablePlugin(DataDirectory, "First.esp")
            .WithAvailablePlugin(DataDirectory, "Third.esp");
        IGameLoadOrders sut = new GameLoadOrders(environment);

        var result = await sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            cancellationToken: TestContext.Current.CancellationToken);

        var completed = Assert.IsType<AvailablePluginsDiscovered>(result);
        Assert.Equal(["First.esp", "Third.esp"], completed.PluginNames);
    }

    /// <summary>
    ///     Verifies bounded raw progress at startup, every tenth listing, and completion.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_LargeGameLoadOrder_ReportsBoundedRawProgress()
    {
        var pluginNames = Enumerable.Range(1, 25).Select(index => $"Plugin{index}.esp").ToArray();
        var environment = new InMemoryGameLoadOrderEnvironment()
            .WithLoadOrder(GameRelease.SkyrimSE, DataDirectory, pluginNames);
        var reports = new List<GameLoadOrderDiscoveryProgress>();
        var progress = new InlineProgress<GameLoadOrderDiscoveryProgress>(reports.Add);
        IGameLoadOrders sut = new GameLoadOrders(environment);

        await sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [new(0, 25), new(10, 25), new(20, 25), new(25, 25)],
            reports);
    }

    /// <summary>
    ///     Verifies synchronous startup cancellation prevents the first Plugin-file observation.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_ProgressObserverCancelsAtStartup_PerformsNoFileObservation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var environment = new InMemoryGameLoadOrderEnvironment()
            .WithLoadOrder(GameRelease.SkyrimSE, DataDirectory, "NeverObserved.esp");
        var progress = new InlineProgress<GameLoadOrderDiscoveryProgress>(_ => cancellation.Cancel());
        IGameLoadOrders sut = new GameLoadOrders(environment);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            progress,
            cancellation.Token));

        Assert.Empty(environment.ObservedPluginPaths);
    }

    /// <summary>
    ///     Verifies synchronous interval cancellation prevents every later Plugin-file observation.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_ProgressObserverCancelsAtTenthEntry_PerformsNoLaterFileObservation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var pluginNames = Enumerable.Range(1, 20).Select(index => $"Plugin{index}.esp").ToArray();
        var environment = new InMemoryGameLoadOrderEnvironment()
            .WithLoadOrder(GameRelease.SkyrimSE, DataDirectory, pluginNames);
        var progress = new InlineProgress<GameLoadOrderDiscoveryProgress>(report =>
        {
            if (report.ScannedCount == 10)
            {
                cancellation.Cancel();
            }
        });
        IGameLoadOrders sut = new GameLoadOrders(environment);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            progress,
            cancellation.Token));

        Assert.Equal(10, environment.ObservedPluginPaths.Count);
    }

    /// <summary>
    ///     Verifies completion-observer cancellation cannot yield a misleading completed list.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_ProgressObserverCancelsAtCompletion_DoesNotReturnCompletedList()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var environment = new InMemoryGameLoadOrderEnvironment()
            .WithLoadOrder(GameRelease.SkyrimSE, DataDirectory, "Only.esp")
            .WithAvailablePlugin(DataDirectory, "Only.esp");
        var progress = new InlineProgress<GameLoadOrderDiscoveryProgress>(report =>
        {
            if (report.ScannedCount == report.TotalCount)
            {
                cancellation.Cancel();
            }
        });
        IGameLoadOrders sut = new GameLoadOrders(environment);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            progress,
            cancellation.Token));
    }

    /// <summary>
    ///     Verifies cancellation raised during one file observation prevents the next observation.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_CancellationDuringFileObservation_DoesNotObserveLaterFiles()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var environment = new InMemoryGameLoadOrderEnvironment()
            .WithLoadOrder(GameRelease.SkyrimSE, DataDirectory, "First.esp", "Second.esp");
        environment.BeforeFileObservation = _ => cancellation.Cancel();
        IGameLoadOrders sut = new GameLoadOrders(environment);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            cancellationToken: cancellation.Token));

        Assert.Single(environment.ObservedPluginPaths);
    }

    /// <summary>
    ///     Verifies pre-cancellation prevents Game Load Order and filesystem access.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_PreCancelled_PerformsNoEnvironmentWork()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var environment = new InMemoryGameLoadOrderEnvironment();
        IGameLoadOrders sut = new GameLoadOrders(environment);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            cancellationToken: cancellation.Token));

        Assert.Empty(environment.ReadRequests);
    }

    /// <summary>
    ///     Verifies each expected listing-access exception becomes a typed local-access failure.
    /// </summary>
    /// <param name="failureKind">Selects IOException, UnauthorizedAccessException, or SecurityException.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DiscoverAvailablePluginsAsync_ExpectedListingAccessFailure_ReturnsTypedLocalAccessFailure(
        int failureKind)
    {
        Exception failure = failureKind switch
        {
            0 => new IOException("load order unavailable"),
            1 => new UnauthorizedAccessException("load order denied"),
            2 => new SecurityException("load order blocked"),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind))
        };
        var environment = new InMemoryGameLoadOrderEnvironment { ReadFailure = failure };
        IGameLoadOrders sut = new GameLoadOrders(environment);

        var result = await sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            cancellationToken: TestContext.Current.CancellationToken);

        var localAccessFailure = Assert.IsType<GameLoadOrderLocalAccessFailure>(result);
        Assert.Equal(failure.Message, localAccessFailure.ErrorMessage);
    }

    /// <summary>
    ///     Verifies expected availability-observation failures become typed local-access failures.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_ExpectedFileObservationFailure_ReturnsTypedLocalAccessFailure()
    {
        var environment = new InMemoryGameLoadOrderEnvironment
        {
            FileObservationFailure = new IOException("Plugin path unavailable")
        }.WithLoadOrder(GameRelease.SkyrimSE, DataDirectory, "Unreadable.esp");
        IGameLoadOrders sut = new GameLoadOrders(environment);

        var result = await sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<GameLoadOrderLocalAccessFailure>(result);
    }

    /// <summary>
    ///     Verifies cancellation wins when an in-flight file observation also raises an expected access failure.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_CancelledFileObservationAlsoFails_PropagatesCancellation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var environment = new InMemoryGameLoadOrderEnvironment
        {
            BeforeFileObservation = _ => cancellation.Cancel(),
            FileObservationFailure = new IOException("Plugin path unavailable")
        }.WithLoadOrder(GameRelease.SkyrimSE, DataDirectory, "Unreadable.esp");
        IGameLoadOrders sut = new GameLoadOrders(environment);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            cancellationToken: cancellation.Token));
    }

    /// <summary>
    ///     Verifies programming failures remain exceptions rather than ordinary discovery facts.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_ProgrammingFailure_Propagates()
    {
        var environment = new InMemoryGameLoadOrderEnvironment
        {
            ReadFailure = new InvalidOperationException("broken adapter")
        };
        IGameLoadOrders sut = new GameLoadOrders(environment);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Verifies observer failures propagate even when their exception type normally denotes local access.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_ObserverThrowsExpectedAccessException_PropagatesObserverFailure()
    {
        var observerFailure = new IOException("observer failed");
        var environment = new InMemoryGameLoadOrderEnvironment()
            .WithLoadOrder(GameRelease.SkyrimSE, DataDirectory, "NeverObserved.esp");
        var progress = new InlineProgress<GameLoadOrderDiscoveryProgress>(_ => throw observerFailure);
        IGameLoadOrders sut = new GameLoadOrders(environment);

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.DiscoverAvailablePluginsAsync(
            GameRelease.SkyrimSE,
            DataDirectory,
            progress,
            TestContext.Current.CancellationToken));

        Assert.Same(observerFailure, thrown);
    }

    /// <summary>
    ///     Verifies synchronous environment work is offloaded before discovery returns its Task.
    /// </summary>
    [Fact]
    public async Task DiscoverAvailablePluginsAsync_SynchronousEnvironmentWork_ReturnsBeforeWorkerCompletes()
    {
        using var readStarted = new ManualResetEventSlim();
        using var releaseRead = new ManualResetEventSlim();
        var environment = new InMemoryGameLoadOrderEnvironment
        {
            BeforeRead = () =>
            {
                readStarted.Set();
                releaseRead.Wait(TestContext.Current.CancellationToken);
            }
        };
        IGameLoadOrders sut = new GameLoadOrders(environment);
        Task<AvailablePluginsDiscoveryResult>? discovery = null;

        try
        {
            var invocation = Task.Factory.StartNew(
                () => sut.DiscoverAvailablePluginsAsync(
                    GameRelease.SkyrimSE,
                    DataDirectory,
                    cancellationToken: TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);

            Assert.True(readStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            discovery = await invocation.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.False(discovery.IsCompleted);
        }
        finally
        {
            releaseRead.Set();
        }

        await discovery!;
    }

    /// <summary>
    ///     Verifies selection order, casing, membership, availability, and listing-resolved paths in one mixed result.
    /// </summary>
    [Fact]
    public void PrepareSelectedPlugins_MixedSelection_ReturnsOneOrderedCasePerSelectionPreservingCasing()
    {
        var environment = new InMemoryGameLoadOrderEnvironment()
            .WithLoadOrder(
                GameRelease.SkyrimSE,
                DataDirectory,
                "LISTED.esp",
                "Unavailable.esp")
            .WithAvailablePlugin(DataDirectory, "LISTED.esp");
        IGameLoadOrders sut = new GameLoadOrders(environment);

        var prepared = sut.PrepareSelectedPlugins(
            GameRelease.SkyrimSE,
            DataDirectory,
            ["listed.ESP", "Missing.esp", "UNAVAILABLE.ESP"],
            TestContext.Current.CancellationToken);

        var ready = Assert.IsType<SelectedPluginReady>(prepared[0]);
        Assert.Equal("listed.ESP", ready.PluginName);
        Assert.Equal(Path.Combine(DataDirectory, "LISTED.esp"), ready.ResolvedPluginPath);
        Assert.Equal("Missing.esp", Assert.IsType<SelectedPluginNotListed>(prepared[1]).PluginName);
        var unavailable = Assert.IsType<SelectedPluginFileUnavailable>(prepared[2]);
        Assert.Equal("UNAVAILABLE.ESP", unavailable.PluginName);
        Assert.Equal(Path.Combine(DataDirectory, "Unavailable.esp"), unavailable.ResolvedPluginPath);
    }

    /// <summary>
    ///     Verifies one listing read and eager preparation produce a capability shared by every ready case.
    /// </summary>
    [Fact]
    public void PrepareSelectedPlugins_MultipleReadySelections_PreparesOnceAndSharesCapabilityIdentity()
    {
        var environment = new InMemoryGameLoadOrderEnvironment()
            .WithLoadOrder(GameRelease.SkyrimSE, DataDirectory, "First.esp", "Second.esp", "Unavailable.esp")
            .WithAvailablePlugin(DataDirectory, "First.esp")
            .WithAvailablePlugin(DataDirectory, "Second.esp");
        IGameLoadOrders sut = new GameLoadOrders(environment);

        var prepared = sut.PrepareSelectedPlugins(
            GameRelease.SkyrimSE,
            DataDirectory,
            ["First.esp", "Second.esp"],
            TestContext.Current.CancellationToken);

        Assert.Single(environment.ReadRequests);
        Assert.Equal(
            ["First.esp", "Second.esp", "Unavailable.esp"],
            environment.PreparedPluginFiles.Select(pluginFile => pluginFile.PluginName));
        Assert.Equal(1, environment.PreparationCount);
        Assert.Same(
            Assert.IsType<SelectedPluginReady>(prepared[0]).ReadCapability,
            Assert.IsType<SelectedPluginReady>(prepared[1]).ReadCapability);
    }

    /// <summary>
    ///     Verifies pre-cancellation prevents all selected-Plugin preparation environment work.
    /// </summary>
    [Fact]
    public void PrepareSelectedPlugins_PreCancelled_PerformsNoEnvironmentWork()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var environment = new InMemoryGameLoadOrderEnvironment();
        IGameLoadOrders sut = new GameLoadOrders(environment);

        Assert.ThrowsAny<OperationCanceledException>(() => sut.PrepareSelectedPlugins(
            GameRelease.SkyrimSE,
            DataDirectory,
            ["Never.esp"],
            cancellation.Token));

        Assert.Empty(environment.ReadRequests);
        Assert.Equal(0, environment.PreparationCount);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        /// <inheritdoc />
        public void Report(T value)
        {
            report(value);
        }
    }
}
