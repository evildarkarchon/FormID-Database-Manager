#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class ProcessingRunTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task ExecuteAsync_DryRunPluginRun_ReportsPluginStatusWithoutOpeningDatabase()
    {
        var databasePath = CreateTempFilePath("dry-run.db");
        var sut = new ProcessingRun();
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);

        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            databasePath,
            GameRelease.SkyrimSE,
            ["PluginA.esp", "PluginB.esp"],
            UpdateMode.Append,
            dryRun: true);

        await sut.ExecuteAsync(request, progress);

        Assert.False(File.Exists(databasePath));
        Assert.All(events, runEvent => Assert.Equal(ProcessingRunEventKind.Status, runEvent.Kind));
        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process PluginA.esp", StringComparison.Ordinal));
        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process PluginB.esp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_DryRunPluginRun_AllowsEmptyDatabasePath()
    {
        var sut = new ProcessingRun();
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);

        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            string.Empty,
            GameRelease.SkyrimSE,
            ["PluginA.esp"],
            UpdateMode.Append,
            dryRun: true);

        await sut.ExecuteAsync(request, progress);

        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process PluginA.esp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_FormIdTextRun_ImportsRecordsAndReportsCompletionEvents()
    {
        var directory = CreateTempDirectory();
        var databasePath = Path.Combine(directory, "formids.db");
        var textFilePath = Path.Combine(directory, "formids.txt");
        await File.WriteAllLinesAsync(
            textFilePath,
            [
                "PluginA.esp|000001|First Entry",
                "PluginB.esp|000002|Second Entry"
            ],
            TestContext.Current.CancellationToken);

        var sut = new ProcessingRun();
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new FormIdTextProcessingRunRequest(
            textFilePath,
            databasePath,
            GameRelease.SkyrimSE,
            UpdateMode.Append);

        await sut.ExecuteAsync(request, progress);

        await using var store = await FormIdRecordStore.OpenAsync(
            databasePath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);
        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new FormIdStoredRecord("PluginA.esp", "000001", "First Entry"),
                new FormIdStoredRecord("PluginB.esp", "000002", "Second Entry")
            ],
            records);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Completed processing 2 plugins", StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed successfully", StringComparison.Ordinal) &&
            runEvent.Value == 100);
    }

    [Fact]
    public async Task ExecuteAsync_PluginRunMissingSelectedPlugin_WarnsSkipsAndCompletesWithWarnings()
    {
        var gameDirectory = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(gameDirectory, "Data"));
        var databasePath = Path.Combine(gameDirectory, "plugins.db");

        var loadOrderProvider = new Mock<IGameLoadOrderProvider>();
        loadOrderProvider
            .Setup(x => x.BuildSnapshot(GameRelease.SkyrimSE, Path.Combine(gameDirectory, "Data"), true))
            .Returns(new GameLoadOrderSnapshot(["Other.esp"]));

        var sut = new ProcessingRun(loadOrderProvider.Object);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Missing.esp"],
            UpdateMode.Append);

        await sut.ExecuteAsync(request, progress);

        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Warning &&
            runEvent.Message.Contains("Missing.esp", StringComparison.Ordinal) &&
            runEvent.Message.Contains("load order", StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed with warnings", StringComparison.Ordinal) &&
            runEvent.Message.Contains("0 successful", StringComparison.Ordinal) &&
            runEvent.Message.Contains("1 skipped", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed successfully", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_PluginRunMissingPluginFile_WarnsSkipsAndCompletesWithWarnings()
    {
        var gameDirectory = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(gameDirectory, "Data"));
        var databasePath = Path.Combine(gameDirectory, "plugins.db");

        var loadOrderProvider = new Mock<IGameLoadOrderProvider>();
        loadOrderProvider
            .Setup(x => x.BuildSnapshot(GameRelease.SkyrimSE, Path.Combine(gameDirectory, "Data"), true))
            .Returns(new GameLoadOrderSnapshot(["Missing.esp"]));

        var sut = new ProcessingRun(loadOrderProvider.Object);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Missing.esp"],
            UpdateMode.Append);

        await sut.ExecuteAsync(request, progress);

        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Warning &&
            runEvent.Message.Contains("Could not find plugin file", StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed with warnings", StringComparison.Ordinal) &&
            runEvent.Message.Contains("0 successful", StringComparison.Ordinal) &&
            runEvent.Message.Contains("1 skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_PluginSpecificFatalError_ReportsFailedPluginAndContinues()
    {
        var gameDirectory = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(gameDirectory, "Data"));
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var ingestion = new RecordingPluginIngestion();
        ingestion.EnqueueResult(PluginIngestionResult.Failed("Bad.esp", "Invalid plugin header."));
        ingestion.EnqueueResult(PluginIngestionResult.Succeeded("Good.esp", recordCount: 3, []));

        var sut = new ProcessingRun(
            CreateLoadOrderProvider(gameDirectory, ["Bad.esp", "Good.esp"]).Object,
            ingestion);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Bad.esp", "Good.esp"],
            UpdateMode.Append);

        await sut.ExecuteAsync(request, progress);

        Assert.Equal(["Bad.esp", "Good.esp"], ingestion.IngestedPlugins);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Error &&
            runEvent.Message.Contains("1 failed plugin", StringComparison.Ordinal) &&
            runEvent.Message.Contains("Bad.esp: Invalid plugin header.", StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed with failures", StringComparison.Ordinal) &&
            runEvent.Message.Contains("1 successful", StringComparison.Ordinal) &&
            runEvent.Message.Contains("1 failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_StoreWriteFailure_FailsProcessingRunAndDoesNotContinue()
    {
        var gameDirectory = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(gameDirectory, "Data"));
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var ingestion = new RecordingPluginIngestion();
        ingestion.EnqueueException(new InvalidOperationException("store failed"));
        ingestion.EnqueueResult(PluginIngestionResult.Succeeded("Never.esp", recordCount: 1, []));

        var sut = new ProcessingRun(
            CreateLoadOrderProvider(gameDirectory, ["Bad.esp", "Never.esp"]).Object,
            ingestion);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Bad.esp", "Never.esp"],
            UpdateMode.Append);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync(request, progress));

        Assert.Equal("store failed", exception.Message);
        Assert.Equal(["Bad.esp"], ingestion.IngestedPlugins);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Error during processing: store failed", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Error &&
            runEvent.Message.Contains("failed plugin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_CancellationAfterPartialCounts_ReportsCancelledTerminalOutcome()
    {
        var gameDirectory = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(gameDirectory, "Data"));
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var ingestion = new RecordingPluginIngestion();
        ingestion.EnqueueResult(PluginIngestionResult.Succeeded("Done.esp", recordCount: 1, []));
        ingestion.EnqueueException(new OperationCanceledException());

        var sut = new ProcessingRun(
            CreateLoadOrderProvider(gameDirectory, ["Done.esp", "Cancelled.esp"]).Object,
            ingestion);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Done.esp", "Cancelled.esp"],
            UpdateMode.Append);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ExecuteAsync(request, progress));

        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing cancelled", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_PluginWarningsWithoutFailures_CompletesWithWarnings()
    {
        var gameDirectory = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(gameDirectory, "Data"));
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var ingestion = new RecordingPluginIngestion();
        ingestion.EnqueueResult(PluginIngestionResult.Succeeded(
            "Warned.esp",
            recordCount: 2,
            ["Warned.esp: 2 recoverable record issues. First issue; second issue"]));

        var sut = new ProcessingRun(
            CreateLoadOrderProvider(gameDirectory, ["Warned.esp"]).Object,
            ingestion);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Warned.esp"],
            UpdateMode.Append);

        await sut.ExecuteAsync(request, progress);

        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Warning &&
            runEvent.Message.Contains("recoverable record issues", StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed with warnings", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed successfully", StringComparison.Ordinal));
    }

    [Fact]
    public void PluginProcessingRunRequest_EmptyPluginNames_ThrowsRunValidationException()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new PluginProcessingRunRequest(
                @"C:\Games\Skyrim",
                @"C:\Databases\formids.db",
                GameRelease.SkyrimSE,
                [],
                UpdateMode.Append));

        Assert.Equal("No plugins selected", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringPluginIngestion_ReportsCancelledAndThrows()
    {
        var gameDirectory = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(gameDirectory, "Data"));
        var databasePath = Path.Combine(gameDirectory, "cancel.db");
        var ingestion = new BlockingPluginIngestion();
        var sut = new ProcessingRun(
            CreateLoadOrderProvider(gameDirectory, ["Plugin.esp"]).Object,
            ingestion);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Plugin.esp"],
            UpdateMode.Append);

        var processingTask = sut.ExecuteAsync(request, progress);
        var startedTask = await Task.WhenAny(ingestion.Started.Task, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.Same(ingestion.Started.Task, startedTask);

        sut.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processingTask);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_FatalInitializationError_ReportsStatusOnlyAndRethrows()
    {
        var sut = new ProcessingRun();
        var databasePath = Path.Combine(CreateTempDirectory(), "missing-parent", "fatal.db");
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            databasePath,
            GameRelease.SkyrimSE,
            ["Plugin.esp"],
            UpdateMode.Append);

        await Assert.ThrowsAnyAsync<Exception>(() => sut.ExecuteAsync(request, progress));

        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Error during processing", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Error &&
            runEvent.Message.Contains("Error during processing", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
            }
        }

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
            }
        }
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"processing_run_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }

    private string CreateTempFilePath(string fileName)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"processing_run_{Guid.NewGuid():N}_{fileName}");
        _tempFiles.Add(filePath);
        return filePath;
    }

    private static Mock<IGameLoadOrderProvider> CreateLoadOrderProvider(
        string gameDirectory,
        IReadOnlyList<string> pluginNames)
    {
        var loadOrderProvider = new Mock<IGameLoadOrderProvider>();
        loadOrderProvider
            .Setup(x => x.BuildSnapshot(GameRelease.SkyrimSE, Path.Combine(gameDirectory, "Data"), true))
            .Returns(new GameLoadOrderSnapshot(pluginNames));
        return loadOrderProvider;
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            handler(value);
        }
    }

    private sealed class BlockingPluginIngestion : PluginIngestion
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal override async Task<PluginIngestionResult> IngestAsync(
            PluginIngestionRequest request,
            FormIdRecordStore recordStore,
            CancellationToken cancellationToken)
        {
            Started.SetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new UnreachableException();
        }
    }

    private sealed class RecordingPluginIngestion : PluginIngestion
    {
        private readonly Queue<Func<PluginIngestionRequest, FormIdRecordStore, CancellationToken, Task<PluginIngestionResult>>>
            _responses = [];

        public List<string> IngestedPlugins { get; } = [];

        public void EnqueueResult(PluginIngestionResult result)
        {
            _responses.Enqueue((_, _, _) => Task.FromResult(result));
        }

        public void EnqueueException(Exception exception)
        {
            _responses.Enqueue((_, _, _) => Task.FromException<PluginIngestionResult>(exception));
        }

        internal override Task<PluginIngestionResult> IngestAsync(
            PluginIngestionRequest request,
            FormIdRecordStore recordStore,
            CancellationToken cancellationToken)
        {
            IngestedPlugins.Add(request.PluginName);
            return _responses.Dequeue()(request, recordStore, cancellationToken);
        }
    }
}
