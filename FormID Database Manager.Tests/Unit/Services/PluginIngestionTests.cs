using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
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

        var sut = CreateSut(new EmptyPluginOverlayReader());

        var result = await sut.IngestAsync(
            new PluginIngestionRequest(
                gameDirectory,
                GameRelease.SkyrimSE,
                "Empty.esp",
                new GameLoadOrderSnapshot(["Empty.esp"]),
                UpdateMode.ReplacePluginRecords),
            recordStore,
            TestContext.Current.CancellationToken);

        var storedRecords = await recordStore.ReadRecordsAsync(
            FormIdRecordQuery.All,
            TestContext.Current.CancellationToken);

        Assert.Equal(PluginIngestionResultKind.Skipped, result.Kind);
        Assert.Contains("zero FormID records", result.Detail, StringComparison.Ordinal);
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
