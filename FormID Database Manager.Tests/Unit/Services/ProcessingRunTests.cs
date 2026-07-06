#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Microsoft.Data.Sqlite;
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
        var databaseService = new Mock<DatabaseService>();
        var sut = new ProcessingRun(databaseService.Object);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);

        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            CreateTempFilePath("dry-run.db"),
            GameRelease.SkyrimSE,
            ["PluginA.esp", "PluginB.esp"],
            UpdateMode.Append,
            dryRun: true);

        await sut.ExecuteAsync(request, progress);

        databaseService.Verify(
            x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.All(events, runEvent => Assert.Equal(ProcessingRunEventKind.Status, runEvent.Kind));
        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process PluginA.esp", StringComparison.Ordinal));
        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process PluginB.esp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_DryRunPluginRun_AllowsEmptyDatabasePath()
    {
        var databaseService = new Mock<DatabaseService>();
        var sut = new ProcessingRun(databaseService.Object);
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

        databaseService.Verify(
            x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

        var sut = new ProcessingRun(new DatabaseService());
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new FormIdTextProcessingRunRequest(
            textFilePath,
            databasePath,
            GameRelease.SkyrimSE,
            UpdateMode.Append);

        await sut.ExecuteAsync(request, progress);

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT plugin, formid, entry FROM {GameRelease.SkyrimSE} ORDER BY id";

        var records = new List<(string Plugin, string FormId, string Entry)>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        Assert.Equal(
            [
                ("PluginA.esp", "000001", "First Entry"),
                ("PluginB.esp", "000002", "Second Entry")
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

        var sut = new ProcessingRun(new DatabaseService(), loadOrderProvider.Object);
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

        var sut = new ProcessingRun(new DatabaseService(), loadOrderProvider.Object);
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
            new DatabaseService(),
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
            new DatabaseService(),
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
            new DatabaseService(),
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
            new DatabaseService(),
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
    public async Task ExecuteAsync_CancelledDuringInitialization_ReportsCancelledAndThrows()
    {
        using var initializeEntered = new ManualResetEventSlim(false);
        var databaseService = new BlockingInitializeDatabaseService(initializeEntered);
        var sut = new ProcessingRun(databaseService);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            CreateTempFilePath("cancel.db"),
            GameRelease.SkyrimSE,
            ["Plugin.esp"],
            UpdateMode.Append);

        var processingTask = sut.ExecuteAsync(request, progress);
        Assert.True(initializeEntered.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        sut.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processingTask);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_FatalInitializationError_ReportsStatusOnlyAndRethrows()
    {
        var sut = new ProcessingRun(new ThrowingInitializeDatabaseService());
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            CreateTempFilePath("fatal.db"),
            GameRelease.SkyrimSE,
            ["Plugin.esp"],
            UpdateMode.Append);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync(request, progress));

        Assert.Equal("database failed", exception.Message);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Error during processing: database failed", StringComparison.Ordinal));
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

    private sealed class BlockingInitializeDatabaseService(ManualResetEventSlim initializeEntered) : DatabaseService
    {
        public override async Task InitializeDatabase(
            string dbPath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            initializeEntered.Set();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ThrowingInitializeDatabaseService : DatabaseService
    {
        public override Task InitializeDatabase(
            string dbPath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException(new InvalidOperationException("database failed"));
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
