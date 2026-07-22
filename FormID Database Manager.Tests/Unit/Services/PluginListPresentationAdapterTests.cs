#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class PluginListPresentationAdapterTests
{
    /// <summary>
    ///     Verifies the presentation seam remains one internal concrete adapter rather than a replacement interface.
    /// </summary>
    [Fact]
    public void TypeShape_NewPresentationAdapter_IsInternalConcreteAndSealed()
    {
        var adapterType = typeof(PluginListPresentationAdapter);

        Assert.True(adapterType.IsNotPublic);
        Assert.True(adapterType.IsSealed);
        Assert.False(adapterType.IsAbstract);
        Assert.Contains(typeof(IDisposable), adapterType.GetInterfaces());
    }

    /// <summary>
    ///     Verifies a failed source transition cannot retain or display the previous source's Plugin projection.
    /// </summary>
    [Fact]
    public async Task Projection_FailedDifferentSource_ClearsItemsAndReportsErrors()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new SequencedPluginListDiscovery(
            PluginListDiscoveryResult.Completed(["Old.esp"]),
            PluginListDiscoveryResult.Failed("new source unavailable"));
        using var pluginList = new PluginList(gameDetectionService.Object, discovery);
        var dispatcher = new SynchronousThreadDispatcher();
        using var viewModel = new MainWindowViewModel(dispatcher);
        using var sut = new PluginListPresentationAdapter(pluginList, viewModel, dispatcher);
        await pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        await pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            Path.Combine(Path.GetTempPath(), $"different-plugin-source-{Guid.NewGuid():N}"),
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        Assert.Empty(viewModel.Plugins);
        Assert.Empty(viewModel.FilteredPlugins);
        Assert.Equal(
            ["Failed to load plugins: new source unavailable", "Ensure you selected the correct game Data directory"],
            viewModel.ErrorMessages);
    }

    /// <summary>
    ///     Verifies obsolete queued progress cannot restore scanning after the authoritative state reaches newer Ready.
    /// </summary>
    [Fact]
    public async Task Projection_QueuedOlderProgressAfterNewerReady_UsesLatestStateOnly()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new OvertakingPluginListDiscovery();
        using var pluginList = new PluginList(gameDetectionService.Object, discovery);
        var dispatcher = new RecordingThreadDispatcher();
        using var viewModel = new MainWindowViewModel(dispatcher);
        using var sut = new PluginListPresentationAdapter(pluginList, viewModel, dispatcher);
        Assert.True(dispatcher.RunNext());

        var olderRefresh = pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.On,
            TestContext.Current.CancellationToken);
        await discovery.OlderStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        discovery.ReportOlderProgress(4, 8);

        await pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.On,
            TestContext.Current.CancellationToken);
        discovery.CompleteOlder("Older.esp");
        await olderRefresh;

        Assert.True(dispatcher.RunNext());

        Assert.False(viewModel.IsScanning);
        Assert.Equal(string.Empty, viewModel.ProgressStatus);
        Assert.Equal("Newer.esp", Assert.Single(viewModel.Plugins).Name);

        dispatcher.Drain();
        Assert.Single(viewModel.InformationMessages);
        Assert.Equal("Loaded 1 plugins", viewModel.InformationMessages[0]);
        Assert.Empty(viewModel.ErrorMessages);
    }

    /// <summary>
    ///     Verifies disposal neutralizes already-queued projection work and unsubscribes future change signals.
    /// </summary>
    [Fact]
    public async Task Dispose_QueuedAndSubsequentSignals_PreventsViewModelMutation()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new SequencedPluginListDiscovery(
            PluginListDiscoveryResult.Completed(["Ignored.esp"]));
        using var pluginList = new PluginList(gameDetectionService.Object, discovery);
        var dispatcher = new RecordingThreadDispatcher();
        using var viewModel = new MainWindowViewModel(dispatcher)
        {
            IsScanning = true,
            ProgressValue = 42,
            ProgressStatus = "Unchanged"
        };
        var sut = new PluginListPresentationAdapter(pluginList, viewModel, dispatcher);
        Assert.Equal(1, dispatcher.PendingCount);

        sut.Dispose();
        dispatcher.Drain();

        Assert.True(viewModel.IsScanning);
        Assert.Equal(42, viewModel.ProgressValue);
        Assert.Equal("Unchanged", viewModel.ProgressStatus);
        var postCountAfterDispose = dispatcher.PostCount;

        await pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        Assert.Equal(postCountAfterDispose, dispatcher.PostCount);
        Assert.Empty(viewModel.Plugins);
        Assert.Empty(viewModel.InformationMessages);
    }

    /// <summary>
    ///     Verifies current caller cancellation clears scanning while preserving the legacy silent terminal behavior.
    /// </summary>
    [Fact]
    public async Task Projection_CurrentCallerCancellation_ClearsScanningWithoutTerminalMessage()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new ControlledPluginListDiscovery();
        using var pluginList = new PluginList(gameDetectionService.Object, discovery);
        var dispatcher = new SynchronousThreadDispatcher();
        using var viewModel = new MainWindowViewModel(dispatcher);
        using var sut = new PluginListPresentationAdapter(pluginList, viewModel, dispatcher);
        using var cancellation = new CancellationTokenSource();
        var refresh = pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            cancellation.Token);
        await discovery.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        discovery.ReportProgress(1, 4);
        Assert.True(viewModel.IsScanning);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.False(viewModel.IsScanning);
        Assert.Equal(0, viewModel.ProgressValue);
        Assert.Equal(string.Empty, viewModel.ProgressStatus);
        Assert.Empty(viewModel.InformationMessages);
        Assert.Empty(viewModel.ErrorMessages);
    }

    /// <summary>
    ///     Verifies same-source failure retains confirmed membership and reports its terminal errors only once.
    /// </summary>
    [Fact]
    public async Task Projection_FailedSameSource_RetainsItemsAndReportsErrorsOnce()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new SequencedPluginListDiscovery(
            PluginListDiscoveryResult.Completed(["Selected.esp"]),
            PluginListDiscoveryResult.Failed("load order failure"));
        using var pluginList = new PluginList(gameDetectionService.Object, discovery);
        var dispatcher = new SynchronousThreadDispatcher();
        using var viewModel = new MainWindowViewModel(dispatcher);
        using var sut = new PluginListPresentationAdapter(pluginList, viewModel, dispatcher);
        await pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var membershipVersion = pluginList.Current.Confirmed!.MembershipVersion;
        pluginList.Apply(new PluginSelectionByNameIntent(membershipVersion, "Selected.esp", true));

        await pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        var projected = Assert.Single(viewModel.Plugins);
        Assert.Equal("Selected.esp", projected.Name);
        Assert.True(projected.IsSelected);
        Assert.False(viewModel.IsScanning);
        Assert.Equal(
            ["Failed to load plugins: load order failure", "Ensure you selected the correct game Data directory"],
            viewModel.ErrorMessages);

        pluginList.Apply(new PluginSelectionByNameIntent(membershipVersion, "Selected.esp", false));

        Assert.Equal(2, viewModel.ErrorMessages.Count);
    }

    /// <summary>
    ///     Verifies versioned selection facts update projected items without rebuilding membership or echoing Ready.
    /// </summary>
    [Fact]
    public async Task Projection_SelectionOnlyRevision_UpdatesItemsWithoutDuplicatingReadyMessage()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new SequencedPluginListDiscovery(
            PluginListDiscoveryResult.Completed(["First.esp", "Second.esp"]));
        using var pluginList = new PluginList(gameDetectionService.Object, discovery);
        var dispatcher = new SynchronousThreadDispatcher();
        using var viewModel = new MainWindowViewModel(dispatcher);
        using var sut = new PluginListPresentationAdapter(pluginList, viewModel, dispatcher);
        await pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var membershipVersion = pluginList.Current.Confirmed!.MembershipVersion;
        var projectedSecond = viewModel.Plugins[1];

        pluginList.Apply(new PluginSelectionByNameIntent(membershipVersion, "second.ESP", true));

        Assert.Same(projectedSecond, viewModel.Plugins[1]);
        Assert.True(projectedSecond.IsSelected);
        Assert.Equal(membershipVersion, projectedSecond.MembershipVersion);
        Assert.Single(viewModel.InformationMessages);
        Assert.Equal("Loaded 2 non-base game plugins", viewModel.InformationMessages[0]);
    }

    /// <summary>
    ///     Verifies raw refresh counts map to legacy scanning wording and Ready clears transient progress.
    /// </summary>
    [Fact]
    public async Task Projection_RefreshingActivity_MapsCountsAndReadyClearsProgress()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new ControlledPluginListDiscovery();
        using var pluginList = new PluginList(gameDetectionService.Object, discovery);
        var dispatcher = new SynchronousThreadDispatcher();
        using var viewModel = new MainWindowViewModel(dispatcher);
        using var sut = new PluginListPresentationAdapter(pluginList, viewModel, dispatcher);

        var refresh = pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.On,
            TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsScanning);
        Assert.Equal("Scanning plugins...", viewModel.ProgressStatus);
        Assert.Equal(0, viewModel.ProgressValue);
        await discovery.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        discovery.ReportProgress(3, 12);

        Assert.True(viewModel.IsScanning);
        Assert.Equal("Scanning plugins... (3/12)", viewModel.ProgressStatus);
        Assert.Equal(25, viewModel.ProgressValue);

        discovery.Complete("User.esp");
        await refresh;

        Assert.False(viewModel.IsScanning);
        Assert.Equal(string.Empty, viewModel.ProgressStatus);
        Assert.Equal(0, viewModel.ProgressValue);
    }

    /// <summary>
    ///     Verifies queued change signals read authoritative state on the dispatcher instead of carrying obsolete snapshots.
    /// </summary>
    [Fact]
    public async Task Changed_QueuedProjection_ReadsLatestCurrentStateWhenCallbackExecutes()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        var discovery = new SequencedPluginListDiscovery(
            PluginListDiscoveryResult.Completed(["Older.esp"]),
            PluginListDiscoveryResult.Completed(["Newer.esp"]));
        using var pluginList = new PluginList(gameDetectionService.Object, discovery);
        var dispatcher = new RecordingThreadDispatcher();
        using var viewModel = new MainWindowViewModel(dispatcher);
        using var sut = new PluginListPresentationAdapter(pluginList, viewModel, dispatcher);
        Assert.True(dispatcher.RunNext());

        await pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        await pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        Assert.Empty(viewModel.Plugins);

        Assert.True(dispatcher.RunNext());

        var projected = Assert.Single(viewModel.Plugins);
        Assert.Equal("Newer.esp", projected.Name);
        Assert.False(projected.IsSelected);
        Assert.Equal(pluginList.Current.Confirmed!.MembershipVersion, projected.MembershipVersion);

        dispatcher.Drain();
        Assert.Single(viewModel.InformationMessages);
        Assert.Equal("Loaded 1 non-base game plugins", viewModel.InformationMessages[0]);
    }

    private sealed class SequencedPluginListDiscovery(params PluginListDiscoveryResult[] results)
        : IPluginListDiscovery
    {
        private readonly Queue<PluginListDiscoveryResult> _results = new(results);

        public string GameDirectory { get; } =
            Path.Combine(Path.GetTempPath(), $"plugin-list-presentation-{Guid.NewGuid():N}");

        /// <inheritdoc />
        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class ControlledPluginListDiscovery : IPluginListDiscovery
    {
        private IProgress<PluginListDiscoveryProgress>? _progress;
        private readonly TaskCompletionSource<PluginListDiscoveryResult> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string GameDirectory { get; } =
            Path.Combine(Path.GetTempPath(), $"controlled-plugin-list-presentation-{Guid.NewGuid():N}");

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _progress = progress;
            Started.SetResult(true);
            return _result.Task.WaitAsync(cancellationToken);
        }

        public void ReportProgress(int scannedCount, int totalCount)
        {
            _progress?.Report(new PluginListDiscoveryProgress(scannedCount, totalCount));
        }

        public void Complete(params string[] pluginNames)
        {
            _result.SetResult(PluginListDiscoveryResult.Completed(pluginNames));
        }
    }

    private sealed class OvertakingPluginListDiscovery : IPluginListDiscovery
    {
        private readonly TaskCompletionSource<PluginListDiscoveryResult> _olderResult =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;
        private IProgress<PluginListDiscoveryProgress>? _olderProgress;

        public string GameDirectory { get; } =
            Path.Combine(Path.GetTempPath(), $"overtaking-plugin-list-presentation-{Guid.NewGuid():N}");

        public TaskCompletionSource<bool> OlderStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                _olderProgress = progress;
                OlderStarted.SetResult(true);
                // Ignore retirement deliberately so the test can release an obsolete result after the newer terminal state.
                return _olderResult.Task;
            }

            return Task.FromResult(PluginListDiscoveryResult.Completed(["Newer.esp"]));
        }

        public void ReportOlderProgress(int scannedCount, int totalCount)
        {
            _olderProgress?.Report(new PluginListDiscoveryProgress(scannedCount, totalCount));
        }

        public void CompleteOlder(params string[] pluginNames)
        {
            _olderResult.SetResult(PluginListDiscoveryResult.Completed(pluginNames));
        }
    }

    private sealed class RecordingThreadDispatcher : IThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _postedActions = new();
        private bool _hasAccess;
        private int _postCount;

        public int PendingCount => _postedActions.Count;

        public int PostCount => Volatile.Read(ref _postCount);

        public Task InvokeAsync(Action action)
        {
            Post(action);
            return Task.CompletedTask;
        }

        public void Post(Action action)
        {
            Interlocked.Increment(ref _postCount);
            _postedActions.Enqueue(action);
        }

        public bool CheckAccess()
        {
            return _hasAccess;
        }

        public bool RunNext()
        {
            if (!_postedActions.TryDequeue(out var action))
            {
                return false;
            }

            _hasAccess = true;
            try
            {
                action();
            }
            finally
            {
                _hasAccess = false;
            }

            return true;
        }

        public void Drain()
        {
            while (RunNext())
            {
            }
        }
    }
}
