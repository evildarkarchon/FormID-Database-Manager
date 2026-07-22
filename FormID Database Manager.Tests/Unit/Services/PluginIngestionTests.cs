using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using Moq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class PluginIngestionTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    /// <summary>
    ///     Verifies that aggregate Plugin Ingestion prepares one shared snapshot and preserves selection order across every
    ///     transient and authoritative observation.
    /// </summary>
    [Fact]
    public async Task IngestAsync_SelectedPlugins_PreparesOnceAndPreservesSequentialOrder()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        await CreatePluginFileAsync(gameDirectory, "Second.esp");
        var events = new List<string>();
        var loadOrderProvider = new RecordingLoadOrderProvider(
            new GameLoadOrderSnapshot(
                ["First.esp", "Second.esp"],
                [
                    new KeyedMasterStyle(ModKey.FromNameAndExtension("First.esp"), MasterStyle.Full),
                    new KeyedMasterStyle(ModKey.FromNameAndExtension("Second.esp"), MasterStyle.Full)
                ]),
            events);
        var recordStore = new RecordingRecordStoreSession(events);
        var overlayReader = new RecordingOverlayReader(events);
        IPluginIngestion sut = new PluginIngestion(
            loadOrderProvider,
            overlayReader,
            new EntryExtraction());
        var progress = new SynchronousProgress<PluginIngestionProgress>(report =>
            events.Add(report.Stage == PluginIngestionProgressStage.PreparingLoadOrder
                ? "progress:preparing"
                : $"progress:{report.PluginPosition}/{report.TotalPluginCount}:{report.PluginName}"));

        var report = await sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.Starfield,
                ["First.esp", "Second.esp"],
                UpdateMode.Append),
            recordStore,
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal(GameReleaseHelper.ResolveDataPath(gameDirectory), loadOrderProvider.CapturedDataPath);
        Assert.Equal(GameRelease.Starfield, loadOrderProvider.CapturedGameRelease);
        Assert.True(loadOrderProvider.CapturedIncludeMasterFlagsLookup);
        Assert.Equal(1, loadOrderProvider.BuildSnapshotCallCount);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.Equal(0, recordStore.DisposeCallCount);
        Assert.All(overlayReader.CapturedReadParameters, parameters => Assert.NotNull(parameters.MasterFlagsLookup));
        Assert.Equal(
            [
                "progress:preparing",
                "load-order",
                "progress:1/2:First.esp",
                "overlay:First.esp",
                "write:First.esp",
                "progress:2/2:Second.esp",
                "overlay:Second.esp",
                "write:Second.esp"
            ],
            events);
        Assert.Collection(
            report.Outcomes,
            outcome =>
            {
                var ingested = Assert.IsType<IngestedPlugin>(outcome);
                Assert.Equal("First.esp", ingested.PluginName);
                Assert.Equal(1, ingested.FormIdCount);
            },
            outcome =>
            {
                var ingested = Assert.IsType<IngestedPlugin>(outcome);
                Assert.Equal("Second.esp", ingested.PluginName);
                Assert.Equal(1, ingested.FormIdCount);
            });
    }

    /// <summary>
    ///     Verifies that cancellation already requested at the aggregate boundary prevents preparation progress,
    ///     load-order access, overlay reads, Store writes, and a completed report.
    /// </summary>
    [Fact]
    public async Task IngestAsync_CancelledBeforeLoadOrderPreparation_ThrowsWithoutStartingWork()
    {
        var gameDirectory = CreateGameDirectory();
        var events = new List<string>();
        var loadOrderProvider = new RecordingLoadOrderProvider(
            new GameLoadOrderSnapshot(["Never.esp"]),
            events);
        IPluginIngestion sut = new PluginIngestion(
            loadOrderProvider,
            new RecordingOverlayReader(events),
            new EntryExtraction());
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Never.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(events),
            new SynchronousProgress<PluginIngestionProgress>(_ => events.Add("progress")),
            cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
        Assert.Equal(0, loadOrderProvider.BuildSnapshotCallCount);
        Assert.Empty(events);
    }

    /// <summary>
    ///     Verifies that cancellation requested synchronously by preparation progress stops before load-order
    ///     initialization and returns no completed report.
    /// </summary>
    [Fact]
    public async Task IngestAsync_PreparationProgressRequestsCancellation_StopsBeforeLoadOrderInitialization()
    {
        var gameDirectory = CreateGameDirectory();
        var events = new List<string>();
        var loadOrderProvider = new RecordingLoadOrderProvider(
            new GameLoadOrderSnapshot(["Never.esp"]),
            events);
        IPluginIngestion sut = new PluginIngestion(
            loadOrderProvider,
            new RecordingOverlayReader(events),
            new EntryExtraction());
        using var cancellationTokenSource = new CancellationTokenSource();
        var progress = new SynchronousProgress<PluginIngestionProgress>(_ => cancellationTokenSource.Cancel());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Never.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(events),
            progress,
            cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
        Assert.Equal(0, loadOrderProvider.BuildSnapshotCallCount);
        Assert.Empty(events);
    }

    /// <summary>
    ///     Verifies that load-order initialization failures retain their identity and abort before any selected Plugin is
    ///     opened or written.
    /// </summary>
    [Fact]
    public async Task IngestAsync_LoadOrderInitializationFailure_PropagatesWithoutPluginAttempts()
    {
        var gameDirectory = CreateGameDirectory();
        var events = new List<string>();
        var failure = new IOException("Load-order initialization failed.");
        var loadOrderProvider = new ThrowingLoadOrderProvider(failure);
        IPluginIngestion sut = new PluginIngestion(
            loadOrderProvider,
            new RecordingOverlayReader(events),
            new EntryExtraction());

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Never.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(events),
            new SynchronousProgress<PluginIngestionProgress>(_ => events.Add("progress:preparing")),
            TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(1, loadOrderProvider.BuildSnapshotCallCount);
        Assert.Equal(["progress:preparing"], events);
    }

    /// <summary>
    ///     Verifies that a structured progress reporter failure retains its identity and aborts the selected set before
    ///     the announced Plugin is opened or written.
    /// </summary>
    [Fact]
    public async Task IngestAsync_CurrentPluginProgressFailure_PropagatesAndStopsSelection()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var events = new List<string>();
        var failure = new InvalidOperationException("Structured progress reporter failed.");
        var progress = new SynchronousProgress<PluginIngestionProgress>(report =>
        {
            if (report is { Stage: PluginIngestionProgressStage.IngestingPlugin, PluginPosition: 2 })
            {
                throw failure;
            }
        });
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(
                new GameLoadOrderSnapshot(["First.esp", "Never.esp"]),
                []),
            new RecordingOverlayReader(events),
            new EntryExtraction());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["First.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(events),
            progress,
            TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(["overlay:First.esp", "write:First.esp"], events);
    }

    /// <summary>
    ///     Verifies that cancellation requested synchronously while reporting the current Plugin is observed before even
    ///     a nonfatal skip can be classified into a completed report.
    /// </summary>
    [Fact]
    public async Task IngestAsync_CurrentPluginProgressRequestsCancellation_StopsBeforePluginAttempt()
    {
        var gameDirectory = CreateGameDirectory();
        var events = new List<string>();
        using var cancellationTokenSource = new CancellationTokenSource();
        var progress = new SynchronousProgress<PluginIngestionProgress>(report =>
        {
            if (report.Stage == PluginIngestionProgressStage.IngestingPlugin)
            {
                cancellationTokenSource.Cancel();
            }
        });
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(new GameLoadOrderSnapshot([]), events),
            new RecordingOverlayReader(events),
            new EntryExtraction());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Never.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(events),
            progress,
            cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
        Assert.Equal(["load-order"], events);
    }

    /// <summary>
    ///     Verifies that an expected Plugin-opening failure becomes one typed outcome without preventing later selected
    ///     Plugins from being ingested in selection order.
    /// </summary>
    [Fact]
    public async Task IngestAsync_OpeningFailure_ReportsFailedPluginAndContinuesInSelectionOrder()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Bad.esp");
        await CreatePluginFileAsync(gameDirectory, "Good.esp");
        var storeEvents = new List<string>();
        var loadOrderProvider = new RecordingLoadOrderProvider(
            new GameLoadOrderSnapshot(["Bad.esp", "Good.esp"]),
            []);
        var overlayReader = new OpeningFailureOverlayReader(
            "Bad.esp",
            CreatePluginOverlayReadException("Invalid plugin header."));
        IPluginIngestion sut = new PluginIngestion(
            loadOrderProvider,
            overlayReader,
            new EntryExtraction());

        var report = await sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Bad.esp", "Good.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Collection(
            report.Outcomes,
            outcome =>
            {
                var failed = Assert.IsType<FailedPlugin>(outcome);
                Assert.Equal(FailedPluginReason.PluginReadFailed, failed.Reason);
                Assert.Equal(PluginReadPhase.OpeningPlugin, failed.Diagnostic.Phase);
                Assert.Equal("Invalid plugin header.", failed.Diagnostic.Message);
            },
            outcome => Assert.Equal(1, Assert.IsType<IngestedPlugin>(outcome).FormIdCount));
        Assert.Equal(["write:Good.esp"], storeEvents);
    }

    /// <summary>
    ///     Verifies that an expected record-enumeration failure retains its reading phase and does not prevent the next
    ///     selected Plugin from being ingested.
    /// </summary>
    [Fact]
    public async Task IngestAsync_RecordReadingFailure_ReportsFailedPluginAndContinuesInSelectionOrder()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "BadRecords.esp");
        await CreatePluginFileAsync(gameDirectory, "Good.esp");
        var storeEvents = new List<string>();
        var failure = new RecordException(
            formKey: null,
            recordType: null,
            modKey: ModKey.FromNameAndExtension("BadRecords.esp"),
            edid: null,
            message: "Invalid record data.");
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(
                new GameLoadOrderSnapshot(["BadRecords.esp", "Good.esp"]),
                []),
            new RecordReadingFailureOverlayReader("BadRecords.esp", failure),
            new EntryExtraction());

        var report = await sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["BadRecords.esp", "Good.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Collection(
            report.Outcomes,
            outcome =>
            {
                var failed = Assert.IsType<FailedPlugin>(outcome);
                Assert.Equal(FailedPluginReason.PluginReadFailed, failed.Reason);
                Assert.Equal(PluginReadPhase.ReadingRecords, failed.Diagnostic.Phase);
                Assert.Equal("Invalid record data.", failed.Diagnostic.Message);
            },
            outcome => Assert.Equal(1, Assert.IsType<IngestedPlugin>(outcome).FormIdCount));
        Assert.Equal(["write:BadRecords.esp", "write:Good.esp"], storeEvents);
    }

    /// <summary>
    ///     Verifies that an unexpected overlay-adapter failure remains an infrastructure failure rather than being
    ///     downgraded to a completed Plugin report.
    /// </summary>
    [Fact]
    public async Task IngestAsync_UnexpectedOverlayFailure_PropagatesAndStopsSelection()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Broken.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var storeEvents = new List<string>();
        var failure = new IOException("Overlay adapter is unavailable.");
        var overlayReader = new OpeningFailureOverlayReader("Broken.esp", failure);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(
                new GameLoadOrderSnapshot(["Broken.esp", "Never.esp"]),
                []),
            overlayReader,
            new EntryExtraction());

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Broken.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(["Broken.esp"], overlayReader.AttemptedPlugins);
        Assert.Empty(storeEvents);
    }

    /// <summary>
    ///     Verifies that an unexpected record-enumerator failure remains an infrastructure failure and stops later
    ///     selected Plugins instead of becoming a Failed Plugin outcome.
    /// </summary>
    [Fact]
    public async Task IngestAsync_UnexpectedRecordEnumerationFailure_PropagatesAndStopsSelection()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "BrokenRecords.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var storeEvents = new List<string>();
        var failure = new IOException("Record enumeration infrastructure failed.");
        var overlayReader = new RecordReadingFailureOverlayReader("BrokenRecords.esp", failure);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(
                new GameLoadOrderSnapshot(["BrokenRecords.esp", "Never.esp"]),
                []),
            overlayReader,
            new EntryExtraction());

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["BrokenRecords.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(["BrokenRecords.esp"], overlayReader.AttemptedPlugins);
        Assert.Equal(["write:BrokenRecords.esp"], storeEvents);
    }

    /// <summary>
    ///     Verifies that cancellation raised while opening a selected Plugin remains cancellation and cannot become a
    ///     Failed Plugin outcome.
    /// </summary>
    [Fact]
    public async Task IngestAsync_OverlayCancellation_PropagatesWithoutCompletedReport()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Cancelled.esp");
        var cancellation = new OperationCanceledException("Plugin read cancelled.");
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(new GameLoadOrderSnapshot(["Cancelled.esp"]), []),
            new OpeningFailureOverlayReader("Cancelled.esp", cancellation),
            new EntryExtraction());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Cancelled.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession([]),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(cancellation, thrown);
    }

    /// <summary>
    ///     Verifies that Mutagen-wrapped cancellation during record enumeration is unwrapped as cancellation and stops
    ///     later selected Plugins.
    /// </summary>
    [Fact]
    public async Task IngestAsync_RecordReadingCancellation_PropagatesWithoutCompletedReport()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "CancelledRecords.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var storeEvents = new List<string>();
        var cancellation = new OperationCanceledException("Record read cancelled.");
        var wrappedCancellation = new RecordException(
            formKey: null,
            recordType: null,
            modKey: ModKey.FromNameAndExtension("CancelledRecords.esp"),
            edid: null,
            innerException: cancellation);
        var overlayReader = new RecordReadingFailureOverlayReader("CancelledRecords.esp", wrappedCancellation);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(
                new GameLoadOrderSnapshot(["CancelledRecords.esp", "Never.esp"]),
                []),
            overlayReader,
            new EntryExtraction());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["CancelledRecords.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(cancellation, thrown);
        Assert.Equal(["CancelledRecords.esp"], overlayReader.AttemptedPlugins);
        Assert.Equal(["write:CancelledRecords.esp"], storeEvents);
    }

    /// <summary>
    ///     Verifies that cancellation requested during the final Store write is observed before Plugin Ingestion can
    ///     classify the write or return a completed report.
    /// </summary>
    [Fact]
    public async Task IngestAsync_FinalStoreWriteRequestsCancellation_PropagatesWithoutCompletedReport()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Cancelled.esp");
        using var cancellationTokenSource = new CancellationTokenSource();
        var recordStore = new CancellingRecordStoreSession(cancellationTokenSource);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(new GameLoadOrderSnapshot(["Cancelled.esp"]), []),
            new RecordingOverlayReader([]),
            new EntryExtraction());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Cancelled.esp"],
                UpdateMode.Append),
            recordStore,
            cancellationToken: cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
        Assert.Equal(["Cancelled.esp"], recordStore.AttemptedPlugins);
    }

    /// <summary>
    ///     Verifies that cancellation thrown by the Store retains its exception identity and stops every later Plugin
    ///     attempt instead of becoming a Failed Plugin outcome.
    /// </summary>
    [Fact]
    public async Task IngestAsync_FormIdRecordStoreCancellation_PropagatesAndStopsLaterPlugins()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var events = new List<string>();
        var cancellation = new OperationCanceledException("FormID Record Store write cancelled.");
        var recordStore = new ThrowingRecordStoreSession(cancellation);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(
                new GameLoadOrderSnapshot(["First.esp", "Never.esp"]),
                []),
            new RecordingOverlayReader(events),
            new EntryExtraction());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["First.esp", "Never.esp"],
                UpdateMode.Append),
            recordStore,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(cancellation, thrown);
        Assert.Equal(["First.esp"], recordStore.AttemptedPlugins);
        Assert.Equal(["overlay:First.esp"], events);
    }

    /// <summary>
    ///     Verifies that cancellation arriving after one Plugin attempt completes prevents every later Plugin from being
    ///     announced, opened, or written and returns no completed report.
    /// </summary>
    [Fact]
    public async Task IngestAsync_CancelledBetweenSelectedPlugins_StopsBeforeNextAttempt()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        using var cancellationTokenSource = new CancellationTokenSource();
        var events = new List<string>();
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(
                new GameLoadOrderSnapshot(["First.esp", "Never.esp"]),
                events),
            new CancellingOnDisposeOverlayReader("First.esp", cancellationTokenSource, events),
            new EntryExtraction());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["First.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(events),
            cancellationToken: cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
        Assert.Equal(
            ["load-order", "overlay:First.esp", "write:First.esp"],
            events);
    }

    /// <summary>
    ///     Verifies that a FormID Record Store failure preserves its identity and aborts the selected set instead of being
    ///     downgraded to a Failed Plugin.
    /// </summary>
    [Fact]
    public async Task IngestAsync_FormIdRecordStoreFailure_PropagatesAndStopsLaterPlugins()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var events = new List<string>();
        var failure = new IOException("FormID Record Store write failed.");
        var recordStore = new ThrowingRecordStoreSession(failure);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(
                new GameLoadOrderSnapshot(["First.esp", "Never.esp"]),
                []),
            new RecordingOverlayReader(events),
            new EntryExtraction());

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["First.esp", "Never.esp"],
                UpdateMode.Append),
            recordStore,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(["First.esp"], recordStore.AttemptedPlugins);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.Equal(0, recordStore.DisposeCallCount);
        Assert.Equal(["overlay:First.esp"], events);
    }

    /// <summary>
    ///     Verifies that best-effort overlay cleanup cannot replace the primary FormID Record Store failure while the
    ///     aggregate operation is unwinding.
    /// </summary>
    [Fact]
    public async Task IngestAsync_StoreAndOverlayDisposalFail_PreservesStoreFailureIdentity()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Broken.esp");
        var storeFailure = new IOException("FormID Record Store write failed.");
        var disposalFailure = new InvalidOperationException("Overlay disposal failed.");
        var recordStore = new ThrowingRecordStoreSession(storeFailure);
        var overlayReader = new DisposalFailureOverlayReader(disposalFailure);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(new GameLoadOrderSnapshot(["Broken.esp"]), []),
            overlayReader,
            new EntryExtraction());

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Broken.esp"],
                UpdateMode.Append),
            recordStore,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(storeFailure, thrown);
        Assert.Equal(1, overlayReader.DisposeCallCount);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.Equal(0, recordStore.DisposeCallCount);
    }

    /// <summary>
    ///     Verifies that an overlay cleanup failure still propagates as infrastructure failure when no primary operation
    ///     exception is already in flight.
    /// </summary>
    [Fact]
    public async Task IngestAsync_OverlayDisposalFailure_PropagatesWithoutCompletedReport()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Broken.esp");
        var events = new List<string>();
        var disposalFailure = new InvalidOperationException("Overlay disposal failed.");
        var recordStore = new RecordingRecordStoreSession(events);
        var overlayReader = new DisposalFailureOverlayReader(disposalFailure);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(new GameLoadOrderSnapshot(["Broken.esp"]), []),
            overlayReader,
            new EntryExtraction());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Broken.esp"],
                UpdateMode.Append),
            recordStore,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(disposalFailure, thrown);
        Assert.Equal(1, overlayReader.DisposeCallCount);
        Assert.Equal(["write:Broken.esp"], events);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.Equal(0, recordStore.DisposeCallCount);
    }

    /// <summary>
    ///     Verifies that recoverable Entry Extraction issues remain structured facts on an Ingested Plugin, with their
    ///     complete count retained and diagnostic growth bounded in observation order.
    /// </summary>
    [Fact]
    public async Task IngestAsync_RecoverableEntryIssues_RetainsBoundedWarningFactsAndReportOrder()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Warned.esp");
        await CreatePluginFileAsync(gameDirectory, "Clean.esp");
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(
                new GameLoadOrderSnapshot(["Warned.esp", "Clean.esp"]),
                []),
            new RecoverableIssueOverlayReader("Warned.esp", issueCount: 7),
            new EntryExtraction());

        var report = await sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Warned.esp", "Clean.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession([]),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Collection(
            report.Outcomes,
            outcome =>
            {
                var ingested = Assert.IsType<IngestedPlugin>(outcome);
                var warning = Assert.IsType<ProcessingWarning>(ingested.Warning);
                Assert.Equal(1, ingested.FormIdCount);
                Assert.Equal(7, warning.TotalIssueCount);
                Assert.Equal(
                    ["detail 1", "detail 2", "detail 3", "detail 4", "detail 5"],
                    warning.DiagnosticDetails);
                Assert.Equal(2, warning.OmittedDetailCount);
            },
            outcome =>
            {
                var ingested = Assert.IsType<IngestedPlugin>(outcome);
                Assert.Equal(1, ingested.FormIdCount);
                Assert.Null(ingested.Warning);
            });
    }

    /// <summary>
    ///     Verifies that absent, unavailable, and zero-record selections produce typed skips and each allows a later Plugin
    ///     to be ingested.
    /// </summary>
    [Fact]
    public async Task IngestAsync_NonfatalSkips_ContinueWithOneOutcomePerSelection()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Zero.esp");
        await CreatePluginFileAsync(gameDirectory, "Available.esp");
        var events = new List<string>();
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(
                new GameLoadOrderSnapshot(["Unavailable.esp", "Zero.esp", "Available.esp"]),
                events),
            new RecordingOverlayReader(events, "Zero.esp"),
            new EntryExtraction());

        var report = await sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Absent.esp", "Unavailable.esp", "Zero.esp", "Available.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(events),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "load-order",
                "overlay:Zero.esp",
                "write:Zero.esp",
                "overlay:Available.esp",
                "write:Available.esp"
            ],
            events);
        Assert.Collection(
            report.Outcomes,
            outcome => Assert.Equal(
                SkippedPluginReason.NotPresentInLoadOrder,
                Assert.IsType<SkippedPlugin>(outcome).Reason),
            outcome => Assert.Equal(
                SkippedPluginReason.PluginFileUnavailable,
                Assert.IsType<SkippedPlugin>(outcome).Reason),
            outcome => Assert.Equal(
                SkippedPluginReason.ZeroFormIdRecords,
                Assert.IsType<SkippedPlugin>(outcome).Reason),
            outcome => Assert.Equal(1, Assert.IsType<IngestedPlugin>(outcome).FormIdCount));
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                /* Test cleanup is best-effort; SQLite can hold temp handles briefly. */
            }
        }
    }

    [Fact]
    public async Task IngestAsync_PluginNotInLoadOrder_ReturnsSkippedOutcome()
    {
        var gameDirectory = CreateGameDirectory();
        var recordStore = new UnusedRecordStoreSession();
        var sut = CreateSut(new ThrowingOverlayReader(new InvalidOperationException("Should not read overlay.")));

        var result = await sut.IngestAsync(
            new PluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                "Missing.esp",
                new GameLoadOrderSnapshot(["Other.esp"]),
                UpdateMode.Append),
            recordStore,
            TestContext.Current.CancellationToken);

        Assert.Equal(PluginIngestionResultKind.Skipped, result.Kind);
        Assert.Contains("load order", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestAsync_PluginFileMissing_ReturnsSkippedOutcome()
    {
        var gameDirectory = CreateGameDirectory();
        var recordStore = new UnusedRecordStoreSession();
        var sut = CreateSut(new ThrowingOverlayReader(new InvalidOperationException("Should not read overlay.")));

        var result = await sut.IngestAsync(
            new PluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                "Missing.esp",
                new GameLoadOrderSnapshot(["Missing.esp"]),
                UpdateMode.Append),
            recordStore,
            TestContext.Current.CancellationToken);

        Assert.Equal(PluginIngestionResultKind.Skipped, result.Kind);
        Assert.Contains("Could not find plugin file", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IngestAsync_CancelledBeforeOverlay_ThrowsOperationCanceledException()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Plugin.esp");
        var recordStore = new UnusedRecordStoreSession();
        var sut = CreateSut(new ThrowingOverlayReader(new InvalidOperationException("Should not read overlay.")));
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new PluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                "Plugin.esp",
                new GameLoadOrderSnapshot(["Plugin.esp"]),
                UpdateMode.Append),
            recordStore,
            cancellationTokenSource.Token));
    }

    [Fact]
    public async Task IngestAsync_OverlayReaderFailure_ReturnsFailedOutcome()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Bad.esp");
        var recordStore = new UnusedRecordStoreSession();
        var sut = CreateSut(new ThrowingOverlayReader(CreatePluginOverlayReadException("Invalid plugin header.")));

        var result = await sut.IngestAsync(
            new PluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                "Bad.esp",
                new GameLoadOrderSnapshot(["Bad.esp"]),
                UpdateMode.Append),
            recordStore,
            TestContext.Current.CancellationToken);

        Assert.Equal(PluginIngestionResultKind.Failed, result.Kind);
        Assert.Contains("Error opening Bad.esp", result.Detail, StringComparison.Ordinal);
        Assert.Contains("Invalid plugin header", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IngestAsync_StarfieldSnapshot_PassesMasterFlagsLookupToOverlayReader()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "TestPlugin.esm");
        var recordStore = new UnusedRecordStoreSession();
        var overlayReader = new ThrowingOverlayReader(
            CreatePluginOverlayReadException("Overlay creation intercepted."));
        var sut = CreateSut(overlayReader);

        var result = await sut.IngestAsync(
            new PluginIngestionRequest(
                gameDirectory,
                GameRelease.Starfield,
                "TestPlugin.esm",
                new GameLoadOrderSnapshot(
                    ["TestPlugin.esm"],
                    [new KeyedMasterStyle(ModKey.FromNameAndExtension("TestPlugin.esm"), MasterStyle.Full)]),
                UpdateMode.Append),
            recordStore,
            TestContext.Current.CancellationToken);

        Assert.Equal(PluginIngestionResultKind.Failed, result.Kind);
        Assert.NotNull(overlayReader.CapturedReadParameters);
        Assert.NotNull(overlayReader.CapturedReadParameters.MasterFlagsLookup);
    }

    /// <summary>
    ///     Verifies through the aggregate ingestion and production Store-opening seams that Update Mode never replaces
    ///     existing rows for a zero-record Skipped Plugin.
    /// </summary>
    [Fact]
    public async Task IngestAsync_ZeroRecordPlugin_ReturnsSkippedAndPreservesExistingRows()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Empty.esp");
        await using var recordStore = await OpenStoreAsync(gameDirectory);
        await recordStore.WritePluginAsync(
            "Empty.esp",
            [new FormIdRecord("000001", "OldEntry")],
            UpdateMode.Append,
            TestContext.Current.CancellationToken);

        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(new GameLoadOrderSnapshot(["Empty.esp"]), []),
            new EmptyPluginOverlayReader(),
            new EntryExtraction());

        var report = await sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["Empty.esp"],
                UpdateMode.ReplacePluginRecords),
            recordStore,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedRecords = await recordStore.ReadRecordsAsync(
            FormIdRecordQuery.All,
            TestContext.Current.CancellationToken);

        var skipped = Assert.IsType<SkippedPlugin>(Assert.Single(report.Outcomes));
        Assert.Equal(SkippedPluginReason.ZeroFormIdRecords, skipped.Reason);
        Assert.Single(storedRecords);
        Assert.Contains(storedRecords, record => record is { Plugin: "Empty.esp", FormId: "000001", Entry: "OldEntry" });
    }

    [Fact]
    public void TryExtract_RecordWithEditorId_UsesEditorIdAsEntry()
    {
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Entry.esp"), SkyrimRelease.SkyrimSE);
        var npc = mod.Npcs.AddNew("NPC_TEST");
        var warnings = new List<string>();
        var sut = new EntryExtraction();

        var record = sut.TryExtract(npc, warnings.Add);

        Assert.NotNull(record);
        Assert.Equal("NPC_TEST", record.Value.Entry);
        Assert.Empty(warnings);
    }

    /// <summary>
    ///     Verifies that reflection-style exception wrapping cannot turn record-getter cancellation into a recoverable
    ///     Entry Extraction issue.
    /// </summary>
    [Fact]
    public void TryExtract_NestedCancellation_ThrowsOriginalOperationCanceledException()
    {
        var cancellation = new OperationCanceledException("Entry extraction cancelled.");
        var record = new Mock<IMajorRecordGetter>();
        record
            .SetupGet(candidate => candidate.FormKey)
            .Throws(new System.Reflection.TargetInvocationException(cancellation));
        var sut = new EntryExtraction();

        var thrown = Assert.Throws<OperationCanceledException>(() => sut.TryExtract(record.Object, _ => { }));

        Assert.Same(cancellation, thrown);
    }

    private PluginIngestion CreateSut(IPluginOverlayReader overlayReader)
    {
        return new PluginIngestion(overlayReader, new EntryExtraction());
    }

    private string CreateGameDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"plugin_ingestion_{Guid.NewGuid():N}");
        Directory.CreateDirectory(GameReleaseHelper.ResolveDataPath(directory));
        _tempDirectories.Add(directory);
        return directory;
    }

    private async Task CreatePluginFileAsync(string gameDirectory, string pluginName)
    {
        var pluginPath = Path.Combine(GameReleaseHelper.ResolveDataPath(gameDirectory), pluginName);
        await File.WriteAllBytesAsync(pluginPath, [0x00], TestContext.Current.CancellationToken);
    }

    private Task<FormIdRecordStore> OpenStoreAsync(string gameDirectory)
    {
        return FormIdRecordStore.OpenAsync(
            Path.Combine(gameDirectory, "plugins.db"),
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);
    }

    private sealed class ThrowingOverlayReader(Exception exception) : IPluginOverlayReader
    {
        public BinaryReadParameters CapturedReadParameters { get; private set; } = null!;

        public IModDisposeGetter ReadOverlay(
            string pluginPath,
            GameRelease gameRelease,
            BinaryReadParameters readParameters)
        {
            CapturedReadParameters = readParameters;
            throw exception;
        }
    }

    private sealed class EmptyPluginOverlayReader : IPluginOverlayReader
    {
        public IModDisposeGetter ReadOverlay(
            string pluginPath,
            GameRelease gameRelease,
            BinaryReadParameters readParameters)
        {
            var plugin = new Mock<IModDisposeGetter>();
            plugin.Setup(x => x.EnumerateMajorRecords()).Returns([]);
            return plugin.Object;
        }
    }

    private sealed class OpeningFailureOverlayReader(
        string failedPluginName,
        Exception openingFailure) : IPluginOverlayReader
    {
        public List<string> AttemptedPlugins { get; } = [];

        /// <summary>
        ///     Returns a one-record overlay except for the configured Plugin-opening failure.
        /// </summary>
        public IModDisposeGetter ReadOverlay(
            string pluginPath,
            GameRelease gameRelease,
            BinaryReadParameters readParameters)
        {
            var pluginName = Path.GetFileName(pluginPath);
            AttemptedPlugins.Add(pluginName);
            if (string.Equals(pluginName, failedPluginName, StringComparison.Ordinal))
            {
                throw openingFailure;
            }

            return CreateOverlay([CreateRecord(pluginName)]);
        }
    }

    private sealed class RecoverableIssueOverlayReader(
        string warnedPluginName,
        int issueCount) : IPluginOverlayReader
    {
        /// <summary>
        ///     Returns ordered unreadable records followed by one valid record for the configured warned Plugin.
        /// </summary>
        public IModDisposeGetter ReadOverlay(
            string pluginPath,
            GameRelease gameRelease,
            BinaryReadParameters readParameters)
        {
            var pluginName = Path.GetFileName(pluginPath);
            var records = new List<IMajorRecordGetter>();
            if (string.Equals(pluginName, warnedPluginName, StringComparison.Ordinal))
            {
                for (var issueNumber = 1; issueNumber <= issueCount; issueNumber++)
                {
                    var detail = $"detail {issueNumber}";
                    var unreadableRecord = new Mock<IMajorRecordGetter>();
                    unreadableRecord
                        .SetupGet(record => record.FormKey)
                        .Throws(new InvalidDataException(detail));
                    records.Add(unreadableRecord.Object);
                }
            }

            records.Add(CreateRecord(pluginName));
            return CreateOverlay(records);
        }
    }

    private sealed class RecordReadingFailureOverlayReader(
        string failedPluginName,
        Exception readingFailure) : IPluginOverlayReader
    {
        public List<string> AttemptedPlugins { get; } = [];

        /// <summary>
        ///     Returns an overlay whose configured Plugin fails during deferred record enumeration.
        /// </summary>
        public IModDisposeGetter ReadOverlay(
            string pluginPath,
            GameRelease gameRelease,
            BinaryReadParameters readParameters)
        {
            var pluginName = Path.GetFileName(pluginPath);
            AttemptedPlugins.Add(pluginName);
            var record = CreateRecord(pluginName);
            var records = string.Equals(pluginName, failedPluginName, StringComparison.Ordinal)
                ? YieldThenThrow(record, readingFailure)
                : [record];
            return CreateOverlay(records);
        }

        private static IEnumerable<IMajorRecordGetter> YieldThenThrow(
            IMajorRecordGetter record,
            Exception exception)
        {
            yield return record;
            throw exception;
        }
    }

    private sealed class CancellingOnDisposeOverlayReader(
        string cancelledAfterPluginName,
        CancellationTokenSource cancellationTokenSource,
        List<string> events) : IPluginOverlayReader
    {
        /// <summary>
        ///     Cancels when the configured Plugin overlay is released, after its Store write and before the next selected
        ///     Plugin boundary.
        /// </summary>
        public IModDisposeGetter ReadOverlay(
            string pluginPath,
            GameRelease gameRelease,
            BinaryReadParameters readParameters)
        {
            var pluginName = Path.GetFileName(pluginPath);
            events.Add($"overlay:{pluginName}");
            var overlay = new Mock<IModDisposeGetter>();
            overlay
                .Setup(plugin => plugin.EnumerateMajorRecords())
                .Returns([CreateRecord(pluginName)]);
            if (string.Equals(pluginName, cancelledAfterPluginName, StringComparison.Ordinal))
            {
                overlay.Setup(plugin => plugin.Dispose()).Callback(cancellationTokenSource.Cancel);
            }

            return overlay.Object;
        }
    }

    private sealed class DisposalFailureOverlayReader(Exception disposalFailure) : IPluginOverlayReader
    {
        public int DisposeCallCount { get; private set; }

        /// <summary>
        ///     Returns a readable overlay whose cleanup raises the configured infrastructure failure.
        /// </summary>
        public IModDisposeGetter ReadOverlay(
            string pluginPath,
            GameRelease gameRelease,
            BinaryReadParameters readParameters)
        {
            var pluginName = Path.GetFileName(pluginPath);
            var overlay = new Mock<IModDisposeGetter>();
            overlay
                .Setup(plugin => plugin.EnumerateMajorRecords())
                .Returns([CreateRecord(pluginName)]);
            overlay
                .Setup(plugin => plugin.Dispose())
                .Callback(() =>
                {
                    DisposeCallCount++;
                    throw disposalFailure;
                });
            return overlay.Object;
        }
    }

    private static IMajorRecordGetter CreateRecord(string pluginName)
    {
        var sourcePlugin = new SkyrimMod(ModKey.FromNameAndExtension(pluginName), SkyrimRelease.SkyrimSE);
        return sourcePlugin.Npcs.AddNew($"NPC_{Path.GetFileNameWithoutExtension(pluginName)}");
    }

    private static IModDisposeGetter CreateOverlay(IEnumerable<IMajorRecordGetter> records)
    {
        var overlay = new Mock<IModDisposeGetter>();
        overlay.Setup(plugin => plugin.EnumerateMajorRecords()).Returns(records);
        return overlay.Object;
    }

    private static PluginOverlayReadException CreatePluginOverlayReadException(string message)
    {
        return new PluginOverlayReadException(message, new MalformedDataException(message));
    }

    private sealed class RecordingLoadOrderProvider(
        GameLoadOrderSnapshot snapshot,
        List<string> events) : IGameLoadOrderProvider
    {
        public int BuildSnapshotCallCount { get; private set; }

        public string CapturedDataPath { get; private set; } = null!;

        public GameRelease CapturedGameRelease { get; private set; }

        public bool CapturedIncludeMasterFlagsLookup { get; private set; }

        public GameLoadOrderSnapshot BuildSnapshot(
            GameRelease gameRelease,
            string dataPath,
            bool includeMasterFlagsLookup = false)
        {
            BuildSnapshotCallCount++;
            CapturedGameRelease = gameRelease;
            CapturedDataPath = dataPath;
            CapturedIncludeMasterFlagsLookup = includeMasterFlagsLookup;
            events.Add("load-order");
            return snapshot;
        }

        public IReadOnlyList<string> GetListedPluginNames(GameRelease gameRelease, string dataPath)
        {
            throw new InvalidOperationException("Aggregate Plugin Ingestion should build one complete snapshot.");
        }
    }

    private sealed class ThrowingLoadOrderProvider(Exception failure) : IGameLoadOrderProvider
    {
        public int BuildSnapshotCallCount { get; private set; }

        /// <summary>
        ///     Raises the configured infrastructure failure at the aggregate load-order boundary.
        /// </summary>
        public GameLoadOrderSnapshot BuildSnapshot(
            GameRelease gameRelease,
            string dataPath,
            bool includeMasterFlagsLookup = false)
        {
            BuildSnapshotCallCount++;
            throw failure;
        }

        public IReadOnlyList<string> GetListedPluginNames(GameRelease gameRelease, string dataPath)
        {
            throw new InvalidOperationException("Aggregate Plugin Ingestion should build one complete snapshot.");
        }
    }

    private sealed class RecordingOverlayReader(
        List<string> events,
        string emptyPluginName = null) : IPluginOverlayReader
    {
        public List<BinaryReadParameters> CapturedReadParameters { get; } = [];

        public IModDisposeGetter ReadOverlay(
            string pluginPath,
            GameRelease gameRelease,
            BinaryReadParameters readParameters)
        {
            var pluginName = Path.GetFileName(pluginPath);
            events.Add($"overlay:{pluginName}");
            CapturedReadParameters.Add(readParameters);
            var overlay = new Mock<IModDisposeGetter>();
            if (string.Equals(pluginName, emptyPluginName, StringComparison.Ordinal))
            {
                overlay.Setup(x => x.EnumerateMajorRecords()).Returns([]);
            }
            else
            {
                var sourcePlugin = new SkyrimMod(ModKey.FromNameAndExtension(pluginName), SkyrimRelease.SkyrimSE);
                var record = sourcePlugin.Npcs.AddNew($"NPC_{Path.GetFileNameWithoutExtension(pluginName)}");
                overlay.Setup(x => x.EnumerateMajorRecords()).Returns([record]);
            }

            return overlay.Object;
        }
    }

    private sealed class RecordingRecordStoreSession(List<string> events) : IFormIdRecordStoreSession
    {
        public int OptimizeCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public Task<FormIdPluginWriteResult> WritePluginAsync(
            string pluginName,
            IEnumerable<FormIdRecord> records,
            UpdateMode updateMode,
            CancellationToken cancellationToken = default)
        {
            events.Add($"write:{pluginName}");
            return Task.FromResult(new FormIdPluginWriteResult(records.Count()));
        }

        public Task<FormIdTextFileImportResult> ImportFormIdTextFileAsync(
            string formIdTextFilePath,
            UpdateMode updateMode,
            IProgress<FormIdStoreProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Plugin Ingestion should not import a FormID text file.");
        }

        public Task OptimizeAsync(CancellationToken cancellationToken = default)
        {
            OptimizeCallCount++;
            throw new InvalidOperationException("Plugin Ingestion should not optimize the FormID Record Store.");
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            throw new InvalidOperationException("Plugin Ingestion should not dispose the FormID Record Store.");
        }
    }

    private sealed class UnusedRecordStoreSession : IFormIdRecordStoreSession
    {
        public Task<FormIdPluginWriteResult> WritePluginAsync(
            string pluginName,
            IEnumerable<FormIdRecord> records,
            UpdateMode updateMode,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This test should not write Plugin records.");
        }

        public Task<FormIdTextFileImportResult> ImportFormIdTextFileAsync(
            string formIdTextFilePath,
            UpdateMode updateMode,
            IProgress<FormIdStoreProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This test should not import a FormID text file.");
        }

        public Task OptimizeAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This test should not optimize the FormID Record Store.");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingRecordStoreSession(Exception failure) : IFormIdRecordStoreSession
    {
        public List<string> AttemptedPlugins { get; } = [];

        public int OptimizeCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public Task<FormIdPluginWriteResult> WritePluginAsync(
            string pluginName,
            IEnumerable<FormIdRecord> records,
            UpdateMode updateMode,
            CancellationToken cancellationToken = default)
        {
            AttemptedPlugins.Add(pluginName);
            return Task.FromException<FormIdPluginWriteResult>(failure);
        }

        public Task<FormIdTextFileImportResult> ImportFormIdTextFileAsync(
            string formIdTextFilePath,
            UpdateMode updateMode,
            IProgress<FormIdStoreProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Plugin Ingestion should not import a FormID text file.");
        }

        public Task OptimizeAsync(CancellationToken cancellationToken = default)
        {
            OptimizeCallCount++;
            throw new InvalidOperationException("Plugin Ingestion should not optimize the FormID Record Store.");
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            throw new InvalidOperationException("Plugin Ingestion should not dispose the FormID Record Store.");
        }
    }

    private sealed class CancellingRecordStoreSession(CancellationTokenSource cancellationTokenSource)
        : IFormIdRecordStoreSession
    {
        public List<string> AttemptedPlugins { get; } = [];

        /// <summary>
        ///     Completes one write after requesting cancellation so the aggregate operation must enforce its own
        ///     post-write abort boundary.
        /// </summary>
        public Task<FormIdPluginWriteResult> WritePluginAsync(
            string pluginName,
            IEnumerable<FormIdRecord> records,
            UpdateMode updateMode,
            CancellationToken cancellationToken = default)
        {
            AttemptedPlugins.Add(pluginName);
            var result = new FormIdPluginWriteResult(records.Count());
            cancellationTokenSource.Cancel();
            return Task.FromResult(result);
        }

        public Task<FormIdTextFileImportResult> ImportFormIdTextFileAsync(
            string formIdTextFilePath,
            UpdateMode updateMode,
            IProgress<FormIdStoreProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Plugin Ingestion should not import a FormID text file.");
        }

        public Task OptimizeAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Plugin Ingestion should not optimize the FormID Record Store.");
        }

        public ValueTask DisposeAsync()
        {
            throw new InvalidOperationException("Plugin Ingestion should not dispose the FormID Record Store.");
        }
    }
}
