using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using FormID_Database_Manager.TestUtilities.Mocks;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class PluginIngestionTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    /// <summary>
    ///     Verifies that Plugin Ingestion prepares the complete selection once and preserves its order across every
    ///     transient and authoritative observation.
    /// </summary>
    [Fact]
    public async Task IngestAsync_SelectedPlugins_PreparesOnceAndPreservesSequentialOrder()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        await CreatePluginFileAsync(gameDirectory, "Second.esp");
        var events = new List<string>();
        var gameLoadOrders = new RecordingGameLoadOrders(["First.esp", "Second.esp"], events);
        var recordStore = new RecordingPluginRecordWriter(events);
        IPluginIngestion sut = new PluginIngestion(gameLoadOrders, new ScriptedPluginReader(events));
        var progress = new SynchronousProgress<PluginIngestionProgress>(report =>
            events.Add(report.Stage == PluginIngestionProgressStage.PreparingLoadOrder
                ? "progress:preparing"
                : $"progress:{report.PluginPosition}/{report.TotalPluginCount}:{report.PluginName}"));
        var source = PluginListSource.Create(GameRelease.Starfield, gameDirectory);

        var report = await sut.IngestAsync(
            new PluginProcessingRunRequest(
                source,
                "ingestion.db",
                ["First.esp", "Second.esp"],
                UpdateMode.Append),
            recordStore,
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal(source.DataDirectory, gameLoadOrders.CapturedCanonicalDataDirectory);
        Assert.Equal(GameRelease.Starfield, gameLoadOrders.CapturedGameRelease);
        Assert.Equal(["First.esp", "Second.esp"], gameLoadOrders.CapturedSelection);
        Assert.Equal(1, gameLoadOrders.PrepareCallCount);
        Assert.Equal(
            [
                "progress:preparing",
                "load-order",
                "progress:1/2:First.esp",
                "open:First.esp",
                "write:First.esp",
                "progress:2/2:Second.esp",
                "open:Second.esp",
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
    ///     Verifies that prepared cases are authoritative: Plugin Ingestion neither rechecks membership or file
    ///     availability nor reconstructs a ready case before passing it to the Plugin read seam.
    /// </summary>
    [Fact]
    public async Task IngestAsync_PreparedCases_MapsSkipsAndPassesReadyCaseIntactWithoutRecheckingFiles()
    {
        var gameDirectory = CreateGameDirectory();
        var dataDirectory = Path.Combine(gameDirectory, "Data");
        await CreatePluginFileAsync(gameDirectory, "NotListed.esp");
        var unavailablePath = Path.Combine(dataDirectory, "Prepared-Unavailable.esp");
        await File.WriteAllBytesAsync(
            unavailablePath,
            [0x00],
            TestContext.Current.CancellationToken);
        var ready = new SelectedPluginReady(
            "READY.esp",
            Path.Combine(gameDirectory, "prepared", "Resolved-Ready.esp"),
            new PluginReadCapability(new object()));
        var gameLoadOrders = new RecordingPreparedGameLoadOrders(
        [
            new SelectedPluginNotListed("NotListed.esp"),
            new SelectedPluginFileUnavailable("Unavailable.ESP", unavailablePath),
            ready
        ]);
        var pluginReader = new ScriptedPluginReader();
        IPluginIngestion sut = new PluginIngestion(gameLoadOrders, pluginReader);
        var source = PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory);

        var report = await sut.IngestAsync(
            new PluginProcessingRunRequest(
                source,
                "ingestion.db",
                ["NotListed.esp", "Unavailable.ESP", "READY.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter([]),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, gameLoadOrders.PrepareCallCount);
        Assert.Equal(source.DataDirectory, gameLoadOrders.CapturedCanonicalDataDirectory);
        Assert.Equal(["NotListed.esp", "Unavailable.ESP", "READY.esp"], gameLoadOrders.CapturedSelection);
        Assert.Same(ready, Assert.Single(pluginReader.OpenedCases));
        Assert.Collection(
            report.Outcomes,
            outcome => Assert.Equal(
                SkippedPluginReason.NotPresentInLoadOrder,
                Assert.IsType<SkippedPlugin>(outcome).Reason),
            outcome =>
            {
                var skipped = Assert.IsType<SkippedPlugin>(outcome);
                Assert.Equal(SkippedPluginReason.PluginFileUnavailable, skipped.Reason);
                Assert.Equal(unavailablePath, skipped.ResolvedPluginPath);
            },
            outcome => Assert.Equal("READY.esp", Assert.IsType<IngestedPlugin>(outcome).PluginName));
    }

    /// <summary>
    ///     Verifies that ingestion prepares Plugins from exactly the Data directory of the request's Plugin List Source,
    ///     which is the directory the Plugin List was discovered from.
    /// </summary>
    /// <remarks>
    ///     Deciding that directory from a game root, including one pasted with a trailing separator, is the Plugin List
    ///     Source's job and is covered by <c>PluginListSourceTests</c>; ingestion no longer canonicalizes (ADR-0010).
    /// </remarks>
    [Fact]
    public async Task IngestAsync_RequestSource_PreparesFromItsDataDirectoryVerbatim()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        var events = new List<string>();
        var gameLoadOrders = new RecordingGameLoadOrders(["First.esp"], events);
        IPluginIngestion sut = new PluginIngestion(
            gameLoadOrders,
            new ScriptedPluginReader(events));
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, Path.Combine(gameDirectory, "Data")),
            "ingestion.db",
            ["First.esp"],
            UpdateMode.Append);

        var report = await sut.IngestAsync(
            request,
            new RecordingPluginRecordWriter(events),
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(request.Source.DataDirectory, gameLoadOrders.CapturedCanonicalDataDirectory, StringComparer.Ordinal);
        // A Plugin found under the source's Data directory must be ingested, not reported missing.
        var outcome = Assert.Single(report.Outcomes);
        Assert.IsType<IngestedPlugin>(outcome);
    }

    /// <summary>
    ///     Verifies that cancellation already requested at the aggregate boundary prevents preparation progress,
    ///     load-order access, Plugin reads, Store writes, and a completed report.
    /// </summary>
    [Fact]
    public async Task IngestAsync_CancelledBeforeLoadOrderPreparation_ThrowsWithoutStartingWork()
    {
        var gameDirectory = CreateGameDirectory();
        var events = new List<string>();
        var gameLoadOrders = new RecordingGameLoadOrders(["Never.esp"], events);
        IPluginIngestion sut = new PluginIngestion(
            gameLoadOrders,
            new ScriptedPluginReader(events));
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Never.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(events),
            new SynchronousProgress<PluginIngestionProgress>(_ => events.Add("progress")),
            cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
        Assert.Equal(0, gameLoadOrders.PrepareCallCount);
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
        var gameLoadOrders = new RecordingGameLoadOrders(["Never.esp"], events);
        IPluginIngestion sut = new PluginIngestion(
            gameLoadOrders,
            new ScriptedPluginReader(events));
        using var cancellationTokenSource = new CancellationTokenSource();
        var progress = new SynchronousProgress<PluginIngestionProgress>(_ => cancellationTokenSource.Cancel());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Never.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(events),
            progress,
            cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
        Assert.Equal(0, gameLoadOrders.PrepareCallCount);
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
        var gameLoadOrders = new ThrowingGameLoadOrders(failure);
        IPluginIngestion sut = new PluginIngestion(
            gameLoadOrders,
            new ScriptedPluginReader(events));

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Never.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(events),
            new SynchronousProgress<PluginIngestionProgress>(_ => events.Add("progress:preparing")),
            TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(1, gameLoadOrders.PrepareCallCount);
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
            new RecordingGameLoadOrders(["First.esp", "Never.esp"], []),
            new ScriptedPluginReader(events));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["First.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(events),
            progress,
            TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(["open:First.esp", "write:First.esp"], events);
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
            new RecordingGameLoadOrders([], events),
            new ScriptedPluginReader(events));

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Never.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(events),
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
        var gameLoadOrders = new RecordingGameLoadOrders(["Bad.esp", "Good.esp"], []);
        var pluginReader = new ScriptedPluginReader
        {
            OpeningFailures =
            {
                ["Bad.esp"] = new PluginReadException(PluginReadPhase.OpeningPlugin, "Invalid plugin header.")
            }
        };
        IPluginIngestion sut = new PluginIngestion(gameLoadOrders, pluginReader);

        var report = await sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Bad.esp", "Good.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(storeEvents),
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
        var pluginReader = new ScriptedPluginReader
        {
            ReadingFailures =
            {
                ["BadRecords.esp"] = new PluginReadException(PluginReadPhase.ReadingRecords, "Invalid record data.")
            }
        };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["BadRecords.esp", "Good.esp"], []),
            pluginReader);

        var report = await sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["BadRecords.esp", "Good.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(storeEvents),
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
    ///     Verifies that cleanup cannot replace an expected record-enumeration failure that has already determined the
    ///     Plugin's Failed Plugin outcome.
    /// </summary>
    [Fact]
    public async Task IngestAsync_RecordReadingAndPluginDisposalFail_PreservesRecordFailureOutcome()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "BadRecords.esp");
        var pluginReader = new ScriptedPluginReader
        {
            ReadingFailures =
            {
                ["BadRecords.esp"] = new PluginReadException(PluginReadPhase.ReadingRecords, "Invalid record data.")
            },
            DisposalFailures = { ["BadRecords.esp"] = new InvalidOperationException("Plugin disposal failed.") }
        };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["BadRecords.esp"], []),
            pluginReader);

        var report = await sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["BadRecords.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter([]),
            cancellationToken: TestContext.Current.CancellationToken);

        var failed = Assert.IsType<FailedPlugin>(Assert.Single(report.Outcomes));
        Assert.Equal(PluginReadPhase.ReadingRecords, failed.Diagnostic.Phase);
        Assert.Equal("Invalid record data.", failed.Diagnostic.Message);
        Assert.Equal(["BadRecords.esp"], pluginReader.DisposedPlugins);
    }

    /// <summary>
    ///     Verifies that an unexpected Plugin-reader opening failure remains an infrastructure failure rather than being
    ///     downgraded to a completed Plugin report.
    /// </summary>
    [Fact]
    public async Task IngestAsync_UnexpectedOpeningFailure_PropagatesAndStopsSelection()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Broken.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var storeEvents = new List<string>();
        var failure = new IOException("Plugin reader is unavailable.");
        var pluginReader = new ScriptedPluginReader { OpeningFailures = { ["Broken.esp"] = failure } };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["Broken.esp", "Never.esp"], []),
            pluginReader);

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Broken.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(["Broken.esp"], pluginReader.OpenedPlugins);
        Assert.Empty(storeEvents);
    }

    /// <summary>
    ///     Verifies that an Unresolvable Master raised while opening a selected Plugin fails the whole Processing Run
    ///     unchanged, rather than becoming one Failed Plugin, and that no later Plugin is attempted.
    /// </summary>
    /// <remarks>
    ///     Issue #52. This is a fact about the Data directory, not about the Plugin: every selected Plugin declares its
    ///     game's main master, so on a game with separated master load orders they would all fail identically. Reporting
    ///     it per Plugin would be both misleading and repetitive, so the run stops at the first one and says what is
    ///     missing. The later selection is asserted untouched for the same reason the infrastructure-failure tests above
    ///     assert it. Which Mutagen failures become an Unresolvable Master, and which master each names, is the Plugin
    ///     reader's classification since ADR-0015, and is pinned in <see cref="MutagenPluginReaderTests" /> and against
    ///     real overlays in <see cref="PluginOverlayConstructionTests" />.
    /// </remarks>
    [Fact]
    public async Task IngestAsync_UnresolvableMasterWhileOpening_FailsTheRunUnchangedWithoutAStoreWrite()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Patch.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var storeEvents = new List<string>();
        var failure = new UnresolvableMasterException("Patch.esp", "Starfield.esm");
        var pluginReader = new ScriptedPluginReader { OpeningFailures = { ["Patch.esp"] = failure } };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["Patch.esp", "Never.esp"], []),
            pluginReader);

        var thrown = await Assert.ThrowsAsync<UnresolvableMasterException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.Starfield, gameDirectory),
                "ingestion.db",
                ["Patch.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(["Patch.esp"], pluginReader.OpenedPlugins);
        Assert.Empty(storeEvents);
    }

    /// <summary>
    ///     Verifies that an unrecognized Plugin-read failure raised while opening ends the Processing Run unchanged
    ///     instead of becoming a Failed Plugin, so later selected Plugins are never opened and nothing is written.
    /// </summary>
    /// <remarks>
    ///     ADR-0016 rule 7. Which Mutagen failures the reader reports this way is pinned in
    ///     <see cref="MutagenPluginReaderTests" />; this pins only that Plugin Ingestion lets it through.
    /// </remarks>
    [Fact]
    public async Task IngestAsync_UnrecognizedPluginReadFailureWhileOpening_FailsTheRunUnchangedWithoutAStoreWrite()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Broken.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var storeEvents = new List<string>();
        var failure = new UnrecognizedPluginReadFailureException(
            "Broken.esp",
            PluginReadPhase.OpeningPlugin,
            new InvalidOperationException("Adapter state is broken."));
        var pluginReader = new ScriptedPluginReader { OpeningFailures = { ["Broken.esp"] = failure } };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["Broken.esp", "Never.esp"], []),
            pluginReader);

        var thrown = await Assert.ThrowsAsync<UnrecognizedPluginReadFailureException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Broken.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(["Broken.esp"], pluginReader.OpenedPlugins);
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
        var pluginReader = new ScriptedPluginReader { ReadingFailures = { ["BrokenRecords.esp"] = failure } };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["BrokenRecords.esp", "Never.esp"], []),
            pluginReader);

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["BrokenRecords.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(["BrokenRecords.esp"], pluginReader.OpenedPlugins);
        Assert.Equal(["write:BrokenRecords.esp"], storeEvents);
    }

    /// <summary>
    ///     Verifies that cancellation raised while opening a selected Plugin remains cancellation and cannot become a
    ///     Failed Plugin outcome.
    /// </summary>
    [Fact]
    public async Task IngestAsync_OpeningCancellation_PropagatesWithoutCompletedReport()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Cancelled.esp");
        var cancellation = new OperationCanceledException("Plugin read cancelled.");
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["Cancelled.esp"], []),
            new ScriptedPluginReader { OpeningFailures = { ["Cancelled.esp"] = cancellation } });

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Cancelled.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter([]),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(cancellation, thrown);
    }

    /// <summary>
    ///     Verifies that cancellation raised during record reading propagates unchanged, rather than becoming a Failed
    ///     Plugin, and stops later selected Plugins.
    /// </summary>
    /// <remarks>
    ///     Unwrapping a cancellation Mutagen nests inside a <c>RecordException</c> is the Plugin reader's job since
    ///     ADR-0015, and is pinned in
    ///     <c>MutagenPluginReaderTests.ReadRecords_CancellationNestedInARecordException_ThrowsTheOriginalCancellation</c>.
    /// </remarks>
    [Fact]
    public async Task IngestAsync_RecordReadingCancellation_PropagatesWithoutCompletedReport()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "CancelledRecords.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var storeEvents = new List<string>();
        var cancellation = new OperationCanceledException("Record read cancelled.");
        var pluginReader = new ScriptedPluginReader { ReadingFailures = { ["CancelledRecords.esp"] = cancellation } };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["CancelledRecords.esp", "Never.esp"], []),
            pluginReader);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["CancelledRecords.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(cancellation, thrown);
        Assert.Equal(["CancelledRecords.esp"], pluginReader.OpenedPlugins);
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
        var recordStore = new CancellingPluginRecordWriter(cancellationTokenSource);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["Cancelled.esp"], []),
            new ScriptedPluginReader());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
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
        var recordStore = new ThrowingPluginRecordWriter(cancellation);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["First.esp", "Never.esp"], []),
            new ScriptedPluginReader(events));

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["First.esp", "Never.esp"],
                UpdateMode.Append),
            recordStore,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(cancellation, thrown);
        Assert.Equal(["First.esp"], recordStore.AttemptedPlugins);
        Assert.Equal(["open:First.esp"], events);
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
            new RecordingGameLoadOrders(["First.esp", "Never.esp"], events),
            // Cancels when First.esp is released, after its Store write and before the next selected Plugin boundary.
            new ScriptedPluginReader(events) { OnDisposed = _ => cancellationTokenSource.Cancel() });

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["First.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(events),
            cancellationToken: cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
        Assert.Equal(
            ["load-order", "open:First.esp", "write:First.esp"],
            events);
    }

    /// <summary>
    ///     Verifies that cancellation requested during final Plugin cleanup wins the report-completion race even when
    ///     the same cleanup also fails.
    /// </summary>
    [Fact]
    public async Task IngestAsync_FinalPluginDisposalCancelsAndFails_PreservesCancellation()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        using var cancellationTokenSource = new CancellationTokenSource();
        var disposalFailure = new InvalidOperationException("Plugin disposal failed.");
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["First.esp"], []),
            new ScriptedPluginReader
            {
                OnDisposed = _ => cancellationTokenSource.Cancel(),
                DisposalFailures = { ["First.esp"] = disposalFailure }
            });

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["First.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter([]),
            cancellationToken: cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
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
        var recordStore = new ThrowingPluginRecordWriter(failure);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["First.esp", "Never.esp"], []),
            new ScriptedPluginReader(events));

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["First.esp", "Never.esp"],
                UpdateMode.Append),
            recordStore,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(["First.esp"], recordStore.AttemptedPlugins);
        Assert.Equal(["open:First.esp"], events);
    }

    /// <summary>
    ///     Verifies that best-effort Plugin cleanup cannot replace the primary FormID Record Store failure while the
    ///     aggregate operation is unwinding.
    /// </summary>
    [Fact]
    public async Task IngestAsync_StoreAndPluginDisposalFail_PreservesStoreFailureIdentity()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Broken.esp");
        var storeFailure = new IOException("FormID Record Store write failed.");
        var disposalFailure = new InvalidOperationException("Plugin disposal failed.");
        var recordStore = new ThrowingPluginRecordWriter(storeFailure);
        var pluginReader = new ScriptedPluginReader { DisposalFailures = { ["Broken.esp"] = disposalFailure } };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["Broken.esp"], []),
            pluginReader);

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Broken.esp"],
                UpdateMode.Append),
            recordStore,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(storeFailure, thrown);
        Assert.Equal(["Broken.esp"], pluginReader.DisposedPlugins);
    }

    /// <summary>
    ///     Verifies that a Plugin cleanup failure still propagates as infrastructure failure when no primary operation
    ///     exception is already in flight.
    /// </summary>
    [Fact]
    public async Task IngestAsync_PluginDisposalFailure_PropagatesWithoutCompletedReport()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Broken.esp");
        var events = new List<string>();
        var disposalFailure = new InvalidOperationException("Plugin disposal failed.");
        var recordStore = new RecordingPluginRecordWriter(events);
        var pluginReader = new ScriptedPluginReader { DisposalFailures = { ["Broken.esp"] = disposalFailure } };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["Broken.esp"], []),
            pluginReader);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Broken.esp"],
                UpdateMode.Append),
            recordStore,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(disposalFailure, thrown);
        Assert.Equal(["Broken.esp"], pluginReader.DisposedPlugins);
        Assert.Equal(["write:Broken.esp"], events);
    }

    /// <summary>
    ///     Verifies that recoverable record issues reported through the Plugin reader's callback remain structured facts
    ///     on an Ingested Plugin, with their complete count retained and diagnostic growth bounded in observation order.
    /// </summary>
    [Fact]
    public async Task IngestAsync_RecoverableRecordIssues_RetainsBoundedWarningFactsAndReportOrder()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Warned.esp");
        await CreatePluginFileAsync(gameDirectory, "Clean.esp");
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["Warned.esp", "Clean.esp"], []),
            new ScriptedPluginReader
            {
                RecordIssues =
                {
                    ["Warned.esp"] = Enumerable.Range(1, 7).Select(issueNumber => $"detail {issueNumber}").ToArray()
                }
            });

        var report = await sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Warned.esp", "Clean.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter([]),
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
            new RecordingGameLoadOrders(["Unavailable.esp", "Zero.esp", "Available.esp"], events),
            new ScriptedPluginReader(events) { EmptyPlugins = { "Zero.esp" } });

        var report = await sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
                ["Absent.esp", "Unavailable.esp", "Zero.esp", "Available.esp"],
                UpdateMode.Append),
            new RecordingPluginRecordWriter(events),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "load-order",
                "open:Zero.esp",
                "write:Zero.esp",
                "open:Available.esp",
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
            new RecordingGameLoadOrders(["Empty.esp"], []),
            new ScriptedPluginReader { EmptyPlugins = { "Empty.esp" } });

        var report = await sut.IngestAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                "ingestion.db",
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

    private string CreateGameDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"plugin_ingestion_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Data"));
        _tempDirectories.Add(directory);
        return directory;
    }

    private async Task CreatePluginFileAsync(string gameDirectory, string pluginName)
    {
        var pluginPath = Path.Combine(gameDirectory, "Data", pluginName);
        await File.WriteAllBytesAsync(pluginPath, [0x00], TestContext.Current.CancellationToken);
    }

    private Task<FormIdRecordStore> OpenStoreAsync(string gameDirectory)
    {
        return FormIdRecordStore.OpenAsync(
            Path.Combine(gameDirectory, "plugins.db"),
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);
    }

    private sealed class RecordingGameLoadOrders(
        IReadOnlyList<string> listedPluginNames,
        List<string> events) : IGameLoadOrders
    {
        public int PrepareCallCount { get; private set; }

        public string CapturedCanonicalDataDirectory { get; private set; } = null!;

        public GameRelease CapturedGameRelease { get; private set; }

        public IReadOnlyList<string> CapturedSelection { get; private set; } = [];

        /// <inheritdoc />
        public Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public ImmutableArray<PreparedSelectedPlugin> PrepareSelectedPlugins(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IReadOnlyList<string> selectedPluginNames,
            CancellationToken cancellationToken = default)
        {
            PrepareCallCount++;
            CapturedGameRelease = gameRelease;
            CapturedCanonicalDataDirectory = canonicalDataDirectory;
            CapturedSelection = selectedPluginNames.ToArray();
            events.Add("load-order");

            var capability = new PluginReadCapability(new object());
            var prepared = ImmutableArray.CreateBuilder<PreparedSelectedPlugin>(selectedPluginNames.Count);
            foreach (var selectedPluginName in selectedPluginNames)
            {
                var listedPluginName = listedPluginNames.FirstOrDefault(listed =>
                    string.Equals(listed, selectedPluginName, StringComparison.OrdinalIgnoreCase));
                if (listedPluginName is null)
                {
                    prepared.Add(new SelectedPluginNotListed(selectedPluginName));
                    continue;
                }

                var resolvedPluginPath = Path.Combine(canonicalDataDirectory, listedPluginName);
                prepared.Add(File.Exists(resolvedPluginPath)
                    ? new SelectedPluginReady(selectedPluginName, resolvedPluginPath, capability)
                    : new SelectedPluginFileUnavailable(selectedPluginName, resolvedPluginPath));
            }

            return prepared.ToImmutable();
        }
    }

    private sealed class ThrowingGameLoadOrders(Exception failure) : IGameLoadOrders
    {
        public int PrepareCallCount { get; private set; }

        /// <inheritdoc />
        public Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        ///     Raises the configured infrastructure failure at the aggregate load-order boundary.
        /// </summary>
        public ImmutableArray<PreparedSelectedPlugin> PrepareSelectedPlugins(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IReadOnlyList<string> selectedPluginNames,
            CancellationToken cancellationToken = default)
        {
            PrepareCallCount++;
            throw failure;
        }
    }

    private sealed class RecordingPluginRecordWriter(List<string> events) : IPluginFormIdRecordWriter
    {
        public Task<FormIdPluginWriteResult> WritePluginAsync(
            string pluginName,
            IEnumerable<FormIdRecord> records,
            UpdateMode updateMode,
            CancellationToken cancellationToken = default)
        {
            events.Add($"write:{pluginName}");
            return Task.FromResult(new FormIdPluginWriteResult(records.Count()));
        }
    }

    private sealed class UnusedPluginRecordWriter : IPluginFormIdRecordWriter
    {
        public Task<FormIdPluginWriteResult> WritePluginAsync(
            string pluginName,
            IEnumerable<FormIdRecord> records,
            UpdateMode updateMode,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This test should not write Plugin records.");
        }
    }

    private sealed class ThrowingPluginRecordWriter(Exception failure) : IPluginFormIdRecordWriter
    {
        public List<string> AttemptedPlugins { get; } = [];

        public Task<FormIdPluginWriteResult> WritePluginAsync(
            string pluginName,
            IEnumerable<FormIdRecord> records,
            UpdateMode updateMode,
            CancellationToken cancellationToken = default)
        {
            AttemptedPlugins.Add(pluginName);
            return Task.FromException<FormIdPluginWriteResult>(failure);
        }
    }

    private sealed class CancellingPluginRecordWriter(CancellationTokenSource cancellationTokenSource)
        : IPluginFormIdRecordWriter
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
    }
}
