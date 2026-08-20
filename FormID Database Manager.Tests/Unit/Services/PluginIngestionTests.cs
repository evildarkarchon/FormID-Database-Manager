using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using FormID_Database_Manager.TestUtilities.Builders;
using FormID_Database_Manager.TestUtilities.Mocks;
using Moq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class PluginIngestionTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    /// <summary>
    ///     One release per game family. Listed rather than derived, so adding a family is a deliberate edit here — the
    ///     same convention <c>PluginIngestionFixtureTests</c> follows, and re-listed rather than shared with it so
    ///     neither file's coverage can be silently narrowed by an edit to the other.
    /// </summary>
    public static TheoryData<GameRelease> GameFamilyRepresentatives =>
    [
        GameRelease.SkyrimSE,
        GameRelease.Fallout4,
        GameRelease.Oblivion,
        GameRelease.Starfield
    ];

    /// <summary>
    ///     The game families that have a record type whose display name is a required aspect rather than an optional
    ///     one. Oblivion is absent because Mutagen's Oblivion definitions have no such type, not because it is skipped.
    /// </summary>
    /// <remarks>
    ///     Listed rather than derived from <see cref="PluginFixture.CanGenerateRequiredNamedRecord" />, for the same
    ///     reason <see cref="GameFamilyRepresentatives" /> is: a derived list would narrow to nothing, and pass
    ///     vacuously, if the builder ever stopped reporting a recipe. What keeps the list honest is the guard in
    ///     <c>PluginOverlayConstructionTests</c>, which fails by name if the set of families with such a type changes.
    /// </remarks>
    public static TheoryData<GameRelease> FamiliesWithARequiredNamedRecordType =>
    [
        GameRelease.SkyrimSE,
        GameRelease.Fallout4,
        GameRelease.Starfield
    ];

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
        var recordStore = new RecordingRecordStoreSession(events);
        var overlayReader = new RecordingOverlayReader(events);
        IPluginIngestion sut = new PluginIngestion(
            gameLoadOrders,
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

        Assert.Equal(Path.Combine(gameDirectory, "Data"), gameLoadOrders.CapturedCanonicalDataDirectory);
        Assert.Equal(GameRelease.Starfield, gameLoadOrders.CapturedGameRelease);
        Assert.Equal(["First.esp", "Second.esp"], gameLoadOrders.CapturedSelection);
        Assert.Equal(1, gameLoadOrders.PrepareCallCount);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.Equal(0, recordStore.DisposeCallCount);
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
    ///     Verifies that prepared cases are authoritative: Plugin Ingestion neither rechecks membership or file
    ///     availability nor reconstructs a ready case before passing it to the overlay seam.
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
        var overlayReader = new PreparedCaseOverlayReader();
        IPluginIngestion sut = new PluginIngestion(gameLoadOrders, overlayReader, new EntryExtraction());

        var report = await sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["NotListed.esp", "Unavailable.ESP", "READY.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession([]),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, gameLoadOrders.PrepareCallCount);
        Assert.Equal(dataDirectory, gameLoadOrders.CapturedCanonicalDataDirectory);
        Assert.Equal(["NotListed.esp", "Unavailable.ESP", "READY.esp"], gameLoadOrders.CapturedSelection);
        Assert.Same(ready, Assert.Single(overlayReader.OpenedPlugins));
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
    ///     Verifies that a selected game directory ending in a separator ingests from the same canonical Data directory
    ///     as the plain form, so a pasted trailing separator cannot send a Processing Run looking in the wrong place.
    /// </summary>
    [Fact]
    public async Task IngestAsync_GameDirectoryWithTrailingSeparator_UsesTheCanonicalDataDirectory()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        var events = new List<string>();
        var gameLoadOrders = new RecordingGameLoadOrders(["First.esp"], events);
        IPluginIngestion sut = new PluginIngestion(
            gameLoadOrders,
            new RecordingOverlayReader(events),
            new EntryExtraction());

        var report = await sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory + Path.DirectorySeparatorChar,
                GameRelease.SkyrimSE,
                ["First.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(events),
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(gameDirectory, "Data"), gameLoadOrders.CapturedCanonicalDataDirectory);
        // A Plugin found under the canonical Data directory must be ingested, not reported missing.
        var outcome = Assert.Single(report.Outcomes);
        Assert.IsType<IngestedPlugin>(outcome);
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
        var gameLoadOrders = new RecordingGameLoadOrders(["Never.esp"], events);
        IPluginIngestion sut = new PluginIngestion(
            gameLoadOrders,
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
            new RecordingGameLoadOrders([], events),
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
        var gameLoadOrders = new RecordingGameLoadOrders(["Bad.esp", "Good.esp"], []);
        var overlayReader = new OpeningFailureOverlayReader(
            "Bad.esp",
            CreatePluginOverlayReadException("Invalid plugin header."));
        IPluginIngestion sut = new PluginIngestion(
            gameLoadOrders,
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
            new RecordingGameLoadOrders(["BadRecords.esp", "Good.esp"], []),
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
    ///     Verifies that cleanup cannot replace an expected record-enumeration failure that has already determined the
    ///     Plugin's Failed Plugin outcome.
    /// </summary>
    [Fact]
    public async Task IngestAsync_RecordReadingAndOverlayDisposalFail_PreservesRecordFailureOutcome()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "BadRecords.esp");
        var recordFailure = new RecordException(
            formKey: null,
            recordType: null,
            modKey: ModKey.FromNameAndExtension("BadRecords.esp"),
            edid: null,
            message: "Invalid record data.");
        var disposalFailure = new InvalidOperationException("Overlay disposal failed.");
        var overlayReader = new RecordReadingFailureOverlayReader(
            "BadRecords.esp",
            recordFailure,
            disposalFailure);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["BadRecords.esp"], []),
            overlayReader,
            new EntryExtraction());

        var report = await sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["BadRecords.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession([]),
            cancellationToken: TestContext.Current.CancellationToken);

        var failed = Assert.IsType<FailedPlugin>(Assert.Single(report.Outcomes));
        Assert.Equal(PluginReadPhase.ReadingRecords, failed.Diagnostic.Phase);
        Assert.Equal("Invalid record data.", failed.Diagnostic.Message);
        Assert.Equal(1, overlayReader.DisposeCallCount);
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
            new RecordingGameLoadOrders(["Broken.esp", "Never.esp"], []),
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
    ///     Verifies that a master a selected Plugin declares but its prepared read capability cannot resolve fails the whole
    ///     Processing Run, naming that master, rather than becoming one Failed Plugin.
    /// </summary>
    /// <remarks>
    ///     Issue #52. This is a fact about the Data directory, not about the Plugin: every selected Plugin declares its
    ///     game's main master, so on a game with separated master load orders they would all fail identically. Reporting
    ///     it per Plugin would be both misleading and repetitive, so the run stops at the first one and says what is
    ///     missing. The later selection is asserted untouched for the same reason the infrastructure-failure tests above
    ///     assert it.
    /// </remarks>
    [Fact]
    public async Task IngestAsync_DeclaredMasterMissingFromTheLookup_FailsTheRunNamingThatMaster()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Patch.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var storeEvents = new List<string>();
        var failure = new MissingModException(
            ModKey.FromNameAndExtension("Starfield.esm"),
            "Mod was missing from load order when constructing the separate mod lists needed for FormID translation.");
        var overlayReader = new OpeningFailureOverlayReader("Patch.esp", failure);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["Patch.esp", "Never.esp"], []),
            overlayReader,
            new EntryExtraction());

        var thrown = await Assert.ThrowsAsync<UnresolvableMasterException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.Starfield,
                ["Patch.esp", "Never.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Patch.esp", thrown.PluginName);
        Assert.Equal("Starfield.esm", thrown.MasterName);
        Assert.Contains("Starfield.esm", thrown.Message, StringComparison.Ordinal);
        Assert.Same(failure, thrown.InnerException);
        Assert.Equal(["Patch.esp"], overlayReader.AttemptedPlugins);
        Assert.Empty(storeEvents);
    }

    /// <summary>
    ///     Verifies the same run-level failure for a prepared capability that supplied no master-flags lookup at all,
    ///     which names no master because Mutagen reports only that the lookup was absent.
    /// </summary>
    /// <remarks>
    ///     Production Game Load Orders always supplies a lookup for a game that needs one, so this arrives only from
    ///     another prepared capability. It remains covered because an unnamed master is still a stopped run rather
    ///     than an unhandled abort.
    /// </remarks>
    [Fact]
    public async Task IngestAsync_NoMasterFlagsLookupAtAll_FailsTheRunWithoutNamingAMaster()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Patch.esp");
        var storeEvents = new List<string>();
        var failure = new MissingModMappingException("Master flag lookup was not provided.");
        var overlayReader = new OpeningFailureOverlayReader("Patch.esp", failure);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["Patch.esp"], []),
            overlayReader,
            new EntryExtraction());

        var thrown = await Assert.ThrowsAsync<UnresolvableMasterException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.Starfield,
                ["Patch.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession(storeEvents),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Patch.esp", thrown.PluginName);
        Assert.Null(thrown.MasterName);
        Assert.Contains("Patch.esp", thrown.Message, StringComparison.Ordinal);
        Assert.Same(failure, thrown.InnerException);
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
            new RecordingGameLoadOrders(["BrokenRecords.esp", "Never.esp"], []),
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
            new RecordingGameLoadOrders(["Cancelled.esp"], []),
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
            new RecordingGameLoadOrders(["CancelledRecords.esp", "Never.esp"], []),
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
            new RecordingGameLoadOrders(["Cancelled.esp"], []),
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
            new RecordingGameLoadOrders(["First.esp", "Never.esp"], []),
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
            new RecordingGameLoadOrders(["First.esp", "Never.esp"], events),
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
    ///     Verifies that cancellation requested during final overlay cleanup wins the report-completion race even when
    ///     the same cleanup also fails.
    /// </summary>
    [Fact]
    public async Task IngestAsync_FinalOverlayDisposalCancelsAndFails_PreservesCancellation()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        using var cancellationTokenSource = new CancellationTokenSource();
        var disposalFailure = new InvalidOperationException("Overlay disposal failed.");
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["First.esp"], []),
            new CancellingOnDisposeOverlayReader(
                "First.esp",
                cancellationTokenSource,
                [],
                disposalFailure),
            new EntryExtraction());

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.IngestAsync(
            new SelectedPluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                ["First.esp"],
                UpdateMode.Append),
            new RecordingRecordStoreSession([]),
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
        var recordStore = new ThrowingRecordStoreSession(failure);
        IPluginIngestion sut = new PluginIngestion(
            new RecordingGameLoadOrders(["First.esp", "Never.esp"], []),
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
            new RecordingGameLoadOrders(["Broken.esp"], []),
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
            new RecordingGameLoadOrders(["Broken.esp"], []),
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
            new RecordingGameLoadOrders(["Warned.esp", "Clean.esp"], []),
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
            new RecordingGameLoadOrders(["Unavailable.esp", "Zero.esp", "Available.esp"], events),
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
    ///     Verifies the synthesized Entry names the Mutagen record type — <c>Npc</c> — for a record carrying neither an
    ///     EditorID nor a display name, for one release of each game family.
    /// </summary>
    /// <remarks>
    ///     This is the in-memory half of the parity issue #50 reported. Its overlay half is
    ///     <c>PluginIngestionFixtureTests.IngestAsync_RecordWithNeitherAnEditorIdNorAName_NamesTheMutagenRecordType</c>,
    ///     which asserts this same literal for the same record read out of a binary overlay, over the same four
    ///     families. The two together are the claim: which label a user gets no longer depends on how Mutagen was
    ///     asked to open the file. Covering only one family here would leave the other three resting on ADR-0004's
    ///     reasoning rather than on an assertion.
    /// </remarks>
    [Theory]
    [MemberData(nameof(GameFamilyRepresentatives))]
    public void TryExtract_RecordWithNeitherAnEditorIdNorAName_NamesTheMutagenRecordType(GameRelease release)
    {
        var npc = PluginFixture.CreateRecordWithoutEditorIdOrName(release);
        var warnings = new List<string>();
        var sut = new EntryExtraction();

        var record = sut.TryExtract(npc, warnings.Add);

        Assert.NotNull(record);
        Assert.Equal($"[Npc_{npc.FormKey.ID:X6}]", record.Value.Entry);
        Assert.Empty(warnings);
    }

    /// <summary>
    ///     Verifies a record whose display name is a <em>required</em> aspect stores that name, rather than being
    ///     treated as anonymous and given a synthesized label, for every family that has such a record type.
    /// </summary>
    /// <remarks>
    ///     Mutagen splits the name aspect in two: an optional <c>INamedGetter</c> and a required
    ///     <c>INamedRequiredGetter</c> the optional one derives from. A handful of record types — Skyrim's <c>Class</c>,
    ///     <c>Key</c>, <c>Flora</c>, <c>Eyes</c> and <c>CollisionLayer</c>, Fallout 4's <c>Key</c>, <c>Flora</c> and
    ///     <c>CollisionLayer</c>, Starfield's <c>Planet</c> and <c>CollisionLayer</c> — implement only the required
    ///     aspect, so a cast to the optional one misses them. Every such record with no EditorID used to land in the
    ///     synthesized-Entry tier with its real name sitting unread on the record (issue #51, ADR-0005). The overlay
    ///     half is <c>PluginIngestionFixtureTests.IngestAsync_RecordWhoseNameAspectIsRequired_StoresThatName</c>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FamiliesWithARequiredNamedRecordType))]
    public void TryExtract_RecordWhoseNameAspectIsRequired_UsesThatNameAsEntry(GameRelease release)
    {
        var record = PluginFixture.CreateRequiredNamedRecord(release);
        var warnings = new List<string>();
        var sut = new EntryExtraction();

        var extracted = sut.TryExtract(record, warnings.Add);

        Assert.NotNull(extracted);
        Assert.Equal(PluginFixture.RequiredNamedRecordDisplayName, extracted.Value.Entry);
        Assert.Empty(warnings);
    }

    /// <summary>
    ///     Verifies a record whose registration is null still gets a synthesized Entry carrying its FormID rather than
    ///     losing its row, and raises no Processing Warning.
    /// </summary>
    /// <remarks>
    ///     ADR-0004's last-resort branch. <see cref="IMajorRecordGetter" /> inherits <c>ILoquiObject</c>, so this is a
    ///     record breaking that contract; no record Mutagen generates does, which leaves only a double like this here.
    ///     The assertion is on the label's shape rather than on the runtime type name it is built from, because the
    ///     only way to write that name down for a generated mock proxy is the very expression under test.
    /// </remarks>
    [Fact]
    public void TryExtract_RecordWithNullRegistration_StillSynthesizesAnEntryCarryingTheFormId()
    {
        var sut = new EntryExtraction();
        var warnings = new List<string>();

        var extracted = sut.TryExtract(CreateRecordWithUnusableRegistration(), warnings.Add);

        Assert.NotNull(extracted);
        Assert.StartsWith("[", extracted.Value.Entry, StringComparison.Ordinal);
        Assert.EndsWith("_000001]", extracted.Value.Entry, StringComparison.Ordinal);
        Assert.Empty(warnings);
    }

    /// <summary>
    ///     Verifies a record whose registration lookup throws is still labelled rather than losing its row, and that
    ///     the throw does not become a Processing Warning.
    /// </summary>
    /// <remarks>
    ///     A record that reached this tier has already lost its EditorID and its name, so a diagnostic about the third
    ///     missing value would report the same fact a third time; what must not happen is the throw escaping.
    /// </remarks>
    [Fact]
    public void TryExtract_RecordWhoseRegistrationThrows_StillSynthesizesAnEntryWithoutWarning()
    {
        var sut = new EntryExtraction();
        var warnings = new List<string>();

        var extracted = sut.TryExtract(
            CreateRecordWithUnusableRegistration(new InvalidOperationException("registration unavailable")),
            warnings.Add);

        Assert.NotNull(extracted);
        Assert.EndsWith("_000001]", extracted.Value.Entry, StringComparison.Ordinal);
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

    private sealed class ThrowingOverlayReader(Exception exception) : IPluginOverlayReader
    {
        public IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin)
        {
            throw exception;
        }
    }

    private sealed class EmptyPluginOverlayReader : IPluginOverlayReader
    {
        public IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin)
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
        public IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin)
        {
            var pluginName = readyPlugin.PluginName;
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
        public IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin)
        {
            var pluginName = readyPlugin.PluginName;
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
        Exception readingFailure,
        Exception? disposalFailure = null) : IPluginOverlayReader
    {
        public List<string> AttemptedPlugins { get; } = [];

        public int DisposeCallCount { get; private set; }

        /// <summary>
        ///     Returns an overlay whose configured Plugin fails during deferred record enumeration.
        /// </summary>
        public IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin)
        {
            var pluginName = readyPlugin.PluginName;
            AttemptedPlugins.Add(pluginName);
            var record = CreateRecord(pluginName);
            var records = string.Equals(pluginName, failedPluginName, StringComparison.Ordinal)
                ? YieldThenThrow(record, readingFailure)
                : [record];
            var overlay = new Mock<IModDisposeGetter>();
            overlay.Setup(plugin => plugin.EnumerateMajorRecords()).Returns(records);
            if (disposalFailure is not null)
            {
                overlay
                    .Setup(plugin => plugin.Dispose())
                    .Callback(() =>
                    {
                        DisposeCallCount++;
                        throw disposalFailure;
                    });
            }

            return overlay.Object;
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
        List<string> events,
        Exception? disposalFailure = null) : IPluginOverlayReader
    {
        /// <summary>
        ///     Cancels when the configured Plugin overlay is released, after its Store write and before the next selected
        ///     Plugin boundary.
        /// </summary>
        public IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin)
        {
            var pluginName = readyPlugin.PluginName;
            events.Add($"overlay:{pluginName}");
            var overlay = new Mock<IModDisposeGetter>();
            overlay
                .Setup(plugin => plugin.EnumerateMajorRecords())
                .Returns([CreateRecord(pluginName)]);
            if (string.Equals(pluginName, cancelledAfterPluginName, StringComparison.Ordinal))
            {
                overlay
                    .Setup(plugin => plugin.Dispose())
                    .Callback(() =>
                    {
                        cancellationTokenSource.Cancel();
                        if (disposalFailure is not null)
                        {
                            throw disposalFailure;
                        }
                    });
            }

            return overlay.Object;
        }
    }

    /// <summary>
    ///     Builds a record with no EditorID whose registration is unusable, so Entry Extraction must fall back to the
    ///     runtime type name.
    /// </summary>
    /// <param name="registrationFailure">
    ///     The failure the registration lookup should raise, or null to leave the registration null — the two ways a
    ///     record can break the <c>ILoquiObject</c> contract that <see cref="IMajorRecordGetter" /> inherits.
    /// </param>
    /// <returns>The record, carrying FormID <c>000001</c>.</returns>
    private static IMajorRecordGetter CreateRecordWithUnusableRegistration(Exception? registrationFailure = null)
    {
        var record = new Mock<IMajorRecordGetter>();
        record
            .SetupGet(candidate => candidate.FormKey)
            .Returns(new FormKey(ModKey.FromNameAndExtension("Fallback.esp"), 0x000001));

        // EditorID and Name are both left unset, so the record reaches the synthesized-Entry tier the same way a real
        // record missing both would. Registration is null by default, which is the no-registration case itself.
        if (registrationFailure is not null)
        {
            record.SetupGet(candidate => candidate.Registration).Throws(registrationFailure);
        }

        return record.Object;
    }

    private sealed class DisposalFailureOverlayReader(Exception disposalFailure) : IPluginOverlayReader
    {
        public int DisposeCallCount { get; private set; }

        /// <summary>
        ///     Returns a readable overlay whose cleanup raises the configured infrastructure failure.
        /// </summary>
        public IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin)
        {
            var pluginName = readyPlugin.PluginName;
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

    private sealed class PreparedCaseOverlayReader : IPluginOverlayReader
    {
        public List<SelectedPluginReady> OpenedPlugins { get; } = [];

        /// <inheritdoc />
        public IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin)
        {
            OpenedPlugins.Add(readyPlugin);
            return CreateOverlay([CreateRecord(readyPlugin.PluginName)]);
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

    private sealed class RecordingOverlayReader(
        List<string> events,
        string? emptyPluginName = null) : IPluginOverlayReader
    {
        public IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin)
        {
            var pluginName = readyPlugin.PluginName;
            events.Add($"overlay:{pluginName}");
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
            IProgress<FormIdStoreProgress>? progress = null,
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
            IProgress<FormIdStoreProgress>? progress = null,
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
            IProgress<FormIdStoreProgress>? progress = null,
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
            IProgress<FormIdStoreProgress>? progress = null,
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
