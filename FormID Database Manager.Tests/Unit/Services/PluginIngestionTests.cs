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
        var sut = CreateSut(new ThrowingOverlayReader(new InvalidOperationException("Invalid plugin header.")));

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
        var overlayReader = new ThrowingOverlayReader(new InvalidOperationException("Overlay creation intercepted."));
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
            throw new InvalidOperationException("Plugin Ingestion should not optimize the FormID Record Store.");
        }

        public ValueTask DisposeAsync()
        {
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
}
