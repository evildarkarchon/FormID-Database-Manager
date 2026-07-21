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

public sealed class ProcessingRunExecutorTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];
    private readonly List<string> _tempFiles = [];

    /// <summary>
    ///     Verifies the public sealed executor contract and its internal composition seam.
    /// </summary>
    [Fact]
    public void Type_IsPublicSealedAndImplementsExecutorInterface()
    {
        var executorType = typeof(ProcessingRunExecutor);

        Assert.True(executorType.IsPublic);
        Assert.True(executorType.IsSealed);
        Assert.Contains(typeof(IProcessingRunExecutor), executorType.GetInterfaces());
    }

    /// <summary>
    ///     Verifies that a FormID text-file dry run reports its plan without opening a FormID Record Store.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DryRunFormIdTextFile_ReportsPlannedWorkWithoutOpeningRecordStore()
    {
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        using var sut = new ProcessingRunExecutor(null, new PluginIngestion(), opener);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new FormIdTextProcessingRunRequest(
            @"C:\Imports\formids.txt",
            string.Empty,
            GameRelease.SkyrimSE,
            UpdateMode.Append,
            dryRun: true);

        await sut.ExecuteAsync(request, progress);

        Assert.Empty(opener.OpenCalls);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains(@"C:\Imports\formids.txt", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Verifies that idle and between-run cancellation never carries into a later Processing Run.
    /// </summary>
    [Fact]
    public async Task Cancel_IdleAndRepeatedBetweenRuns_DoesNotCancelLaterRuns()
    {
        using var sut = new ProcessingRunExecutor();
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);

        sut.Cancel();
        await sut.ExecuteAsync(CreateDryRunRequest("First.esp"), progress);

        sut.Cancel();
        sut.Cancel();
        await sut.ExecuteAsync(CreateDryRunRequest("Second.esp"), progress);

        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process First.esp", StringComparison.Ordinal));
        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process Second.esp", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_DryRunPluginRun_ReportsPluginStatusWithoutOpeningDatabase()
    {
        var databasePath = CreateTempFilePath("dry-run.db");
        var sut = new ProcessingRunExecutor();
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
        var sut = new ProcessingRunExecutor();
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

        var recordStore = new RecordingRecordStoreSession
        {
            TextFileImportResult = new FormIdTextFileImportResult(2, 2),
            TextFileProgressReports =
            [
                new FormIdStoreProgress("Completed processing 2 plugins (2 total records)", 100)
            ]
        };
        var opener = new RecordingRecordStoreSessionOpener(recordStore);
        var sut = new ProcessingRunExecutor(null, new PluginIngestion(), opener);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new FormIdTextProcessingRunRequest(
            textFilePath,
            databasePath,
            GameRelease.SkyrimSE,
            UpdateMode.Append);

        await sut.ExecuteAsync(request, progress);

        Assert.Equal(databasePath, opener.OpenCalls.Single().DatabasePath);
        Assert.Equal(GameRelease.SkyrimSE, opener.OpenCalls.Single().GameRelease);
        Assert.Equal(textFilePath, recordStore.ImportedTextFilePath);
        Assert.Equal(UpdateMode.Append, recordStore.ImportedTextFileUpdateMode);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
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

        var recordStore = new RecordingRecordStoreSession();
        var sut = new ProcessingRunExecutor(loadOrderProvider.Object, new PluginIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
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

        var recordStore = new RecordingRecordStoreSession();
        var sut = new ProcessingRunExecutor(loadOrderProvider.Object, new PluginIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
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

        var sut = new ProcessingRunExecutor(
            CreateLoadOrderProvider(gameDirectory, ["Bad.esp", "Good.esp"]).Object,
            ingestion,
            new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession()));
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

        var sut = new ProcessingRunExecutor(
            CreateLoadOrderProvider(gameDirectory, ["Bad.esp", "Never.esp"]).Object,
            ingestion,
            new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession()));
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

        var sut = new ProcessingRunExecutor(
            CreateLoadOrderProvider(gameDirectory, ["Done.esp", "Cancelled.esp"]).Object,
            ingestion,
            new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession()));
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

        var sut = new ProcessingRunExecutor(
            CreateLoadOrderProvider(gameDirectory, ["Warned.esp"]).Object,
            ingestion,
            new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession()));
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
        var sut = new ProcessingRunExecutor(
            CreateLoadOrderProvider(gameDirectory, ["Plugin.esp"]).Object,
            ingestion,
            new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession()));
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

    /// <summary>
    ///     Verifies that supersession cancels the older run without disposing its source and keeps the newer run active.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SecondRunSupersedesFirst_OlderCompletionCannotClearNewerActiveRun()
    {
        using var opener = new SequencedBlockingRecordStoreSessionOpener();
        using var sut = new ProcessingRunExecutor(null, new PluginIngestion(), opener);

        var firstTask = sut.ExecuteAsync(CreateTextFileRequest("first.txt"));
        Assert.True(opener.FirstOpenStarted.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        var secondTask = sut.ExecuteAsync(CreateTextFileRequest("second.txt"));
        await opener.SecondImportStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        opener.AllowFirstOpenToReturn.Set();
        var firstException = await Record.ExceptionAsync(() => firstTask);

        // The older run finishes after the newer run owns the active slot; cancellation must still target run two.
        sut.Cancel();
        await opener.SecondCancellationObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        var secondException = await Record.ExceptionAsync(() => secondTask);

        Assert.IsAssignableFrom<OperationCanceledException>(firstException);
        Assert.IsNotType<ObjectDisposedException>(firstException);
        Assert.IsAssignableFrom<OperationCanceledException>(secondException);
        Assert.IsNotType<ObjectDisposedException>(secondException);
    }

    /// <summary>
    ///     Verifies that disposal cancels the active run without disposing its source out from under that run.
    /// </summary>
    [Fact]
    public async Task Dispose_ActiveRun_CancelsRunAndCanBeRepeated()
    {
        using var opener = new SequencedBlockingRecordStoreSessionOpener();
        using var sut = new ProcessingRunExecutor(null, new PluginIngestion(), opener);

        var processingTask = sut.ExecuteAsync(CreateTextFileRequest("dispose.txt"));
        Assert.True(opener.FirstOpenStarted.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        sut.Dispose();
        sut.Dispose();
        opener.AllowFirstOpenToReturn.Set();

        var exception = await Record.ExceptionAsync(() => processingTask);
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.IsNotType<ObjectDisposedException>(exception);
    }

    /// <summary>
    ///     Verifies that synchronously blocking initialization is offloaded before execution is returned to its caller.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SynchronousRecordStoreInitializationBlocks_ReturnsControlAsynchronously()
    {
        using var opener = new SequencedBlockingRecordStoreSessionOpener();
        using var sut = new ProcessingRunExecutor(null, new PluginIngestion(), opener);
        using var returnedFromExecute = new ManualResetEventSlim(false);
        Task? processingTask = null;

        var callerTask = Task.Run(() =>
        {
            processingTask = sut.ExecuteAsync(CreateTextFileRequest("blocking.txt"));
            returnedFromExecute.Set();
        }, TestContext.Current.CancellationToken);

        Assert.True(opener.FirstOpenStarted.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        var returnedBeforeInitializationCompleted = returnedFromExecute.Wait(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        opener.AllowFirstOpenToReturn.Set();
        await callerTask;
        await processingTask!;

        Assert.True(returnedBeforeInitializationCompleted);
    }

    [Fact]
    public async Task ExecuteAsync_FatalInitializationError_ReportsStatusOnlyAndRethrows()
    {
        var sut = new ProcessingRunExecutor(
            null,
            new PluginIngestion(),
            new ThrowingRecordStoreSessionOpener(new InvalidOperationException("open failed")));
        var databasePath = Path.Combine(CreateTempDirectory(), "fatal.db");
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
            runEvent.Message.Contains("Error during processing: open failed", StringComparison.Ordinal));
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

    /// <summary>
    ///     Creates a valid dry-run request that requires no filesystem or FormID Record Store access.
    /// </summary>
    /// <param name="pluginName">The selected Plugin to include in the planned run.</param>
    /// <returns>A dry-run Plugin request.</returns>
    private static PluginProcessingRunRequest CreateDryRunRequest(string pluginName)
    {
        return new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            string.Empty,
            GameRelease.SkyrimSE,
            [pluginName],
            UpdateMode.Append,
            dryRun: true);
    }

    /// <summary>
    ///     Creates a FormID text-file request for executor lifecycle tests that use an injected store opener.
    /// </summary>
    /// <param name="fileName">The illustrative FormID text-file name.</param>
    /// <returns>A non-dry-run FormID text-file request.</returns>
    private static FormIdTextProcessingRunRequest CreateTextFileRequest(string fileName)
    {
        return new FormIdTextProcessingRunRequest(
            Path.Combine(@"C:\Imports", fileName),
            @"C:\Databases\formids.db",
            GameRelease.SkyrimSE,
            UpdateMode.Append);
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

    private sealed class SequencedBlockingRecordStoreSessionOpener : IFormIdRecordStoreSessionOpener, IDisposable
    {
        private int _openCount;

        public ManualResetEventSlim FirstOpenStarted { get; } = new(false);

        public ManualResetEventSlim AllowFirstOpenToReturn { get; } = new(false);

        public TaskCompletionSource<bool> SecondImportStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> SecondCancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public Task<IFormIdRecordStoreSession> OpenAsync(
            string databasePath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            var openNumber = Interlocked.Increment(ref _openCount);
            if (openNumber == 1)
            {
                FirstOpenStarted.Set();

                // Ignore cancellation while blocked so the older initializer returns after supersession,
                // forcing the executor to use the older run's captured token without touching its source.
                AllowFirstOpenToReturn.Wait(TestContext.Current.CancellationToken);
                return Task.FromResult<IFormIdRecordStoreSession>(new CancellationAwareRecordStoreSession());
            }

            if (openNumber == 2)
            {
                return Task.FromResult<IFormIdRecordStoreSession>(new CancellationAwareRecordStoreSession(
                    SecondImportStarted,
                    SecondCancellationObserved));
            }

            throw new InvalidOperationException("The lifecycle test opener supports at most two Processing Runs.");
        }

        /// <summary>
        ///     Releases and disposes the synchronization gates owned by this test opener.
        /// </summary>
        public void Dispose()
        {
            AllowFirstOpenToReturn.Set();
            FirstOpenStarted.Dispose();
            AllowFirstOpenToReturn.Dispose();
        }
    }

    private sealed class CancellationAwareRecordStoreSession(
        TaskCompletionSource<bool>? importStarted = null,
        TaskCompletionSource<bool>? cancellationObserved = null) : IFormIdRecordStoreSession
    {
        /// <inheritdoc />
        public Task<FormIdPluginWriteResult> WritePluginAsync(
            string pluginName,
            IEnumerable<FormIdRecord> records,
            UpdateMode updateMode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new FormIdPluginWriteResult(0));
        }

        /// <inheritdoc />
        public async Task<FormIdTextFileImportResult> ImportFormIdTextFileAsync(
            string formIdTextFilePath,
            UpdateMode updateMode,
            IProgress<FormIdStoreProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (importStarted is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new FormIdTextFileImportResult(0, 0);
            }

            importStarted.SetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved!.SetResult(true);
                throw;
            }

            throw new UnreachableException();
        }

        /// <inheritdoc />
        public Task OptimizeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingPluginIngestion : PluginIngestion
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal override async Task<PluginIngestionResult> IngestAsync(
            PluginIngestionRequest request,
            IFormIdRecordStoreSession recordStore,
            CancellationToken cancellationToken)
        {
            Started.SetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new UnreachableException();
        }
    }

    private sealed class RecordingPluginIngestion : PluginIngestion
    {
        private readonly Queue<Func<PluginIngestionRequest, IFormIdRecordStoreSession, CancellationToken, Task<PluginIngestionResult>>>
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
            IFormIdRecordStoreSession recordStore,
            CancellationToken cancellationToken)
        {
            IngestedPlugins.Add(request.PluginName);
            return _responses.Dequeue()(request, recordStore, cancellationToken);
        }
    }

    private sealed class RecordingRecordStoreSessionOpener(IFormIdRecordStoreSession recordStore)
        : IFormIdRecordStoreSessionOpener
    {
        public List<(string DatabasePath, GameRelease GameRelease)> OpenCalls { get; } = [];

        public Task<IFormIdRecordStoreSession> OpenAsync(
            string databasePath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            OpenCalls.Add((databasePath, gameRelease));
            return Task.FromResult(recordStore);
        }
    }

    private sealed class ThrowingRecordStoreSessionOpener(Exception exception) : IFormIdRecordStoreSessionOpener
    {
        public Task<IFormIdRecordStoreSession> OpenAsync(
            string databasePath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IFormIdRecordStoreSession>(exception);
        }
    }

    private sealed class RecordingRecordStoreSession : IFormIdRecordStoreSession
    {
        public string? ImportedTextFilePath { get; private set; }

        public UpdateMode? ImportedTextFileUpdateMode { get; private set; }

        public int OptimizeCallCount { get; private set; }

        public bool Disposed { get; private set; }

        public FormIdTextFileImportResult TextFileImportResult { get; init; }

        public IReadOnlyList<FormIdStoreProgress> TextFileProgressReports { get; init; } = [];

        public Task<FormIdPluginWriteResult> WritePluginAsync(
            string pluginName,
            IEnumerable<FormIdRecord> records,
            UpdateMode updateMode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FormIdPluginWriteResult(0));
        }

        public Task<FormIdTextFileImportResult> ImportFormIdTextFileAsync(
            string formIdTextFilePath,
            UpdateMode updateMode,
            IProgress<FormIdStoreProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ImportedTextFilePath = formIdTextFilePath;
            ImportedTextFileUpdateMode = updateMode;

            foreach (var report in TextFileProgressReports)
            {
                progress?.Report(report);
            }

            return Task.FromResult(TextFileImportResult);
        }

        public Task OptimizeAsync(CancellationToken cancellationToken = default)
        {
            OptimizeCallCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
