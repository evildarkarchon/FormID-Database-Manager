#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
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
        var ingestion = new UnexpectedPluginIngestion();
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        using var sut = new ProcessingRunExecutor(ingestion, opener);
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
        Assert.Equal(0, ingestion.CallCount);
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

    /// <summary>
    ///     Verifies a selected-Plugin dry run reports planned work without opening a Store or invoking Plugin Ingestion.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DryRunPluginRun_ReportsPluginStatusWithoutOpeningDatabase()
    {
        var databasePath = CreateTempFilePath("dry-run.db");
        var ingestion = new UnexpectedPluginIngestion();
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        using var sut = new ProcessingRunExecutor(ingestion, opener);
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
        Assert.Empty(opener.OpenCalls);
        Assert.Equal(0, ingestion.CallCount);
        Assert.All(events, runEvent => Assert.Equal(ProcessingRunEventKind.Status, runEvent.Kind));
        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process PluginA.esp", StringComparison.Ordinal));
        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process PluginB.esp", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Verifies a selected-Plugin dry run does not require a database path because it invokes no Store lifecycle.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DryRunPluginRun_AllowsEmptyDatabasePath()
    {
        var ingestion = new UnexpectedPluginIngestion();
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        using var sut = new ProcessingRunExecutor(ingestion, opener);
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

        Assert.Empty(opener.OpenCalls);
        Assert.Equal(0, ingestion.CallCount);
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

        var events = new List<ProcessingRunEvent>();
        var recordStore = new RecordingRecordStoreSession
        {
            TextFileImportResult = new FormIdTextFileImportResult(2, 2),
            TextFileProgressReports =
            [
                new FormIdStoreProgress("Completed processing 2 plugins (2 total records)", 100)
            ],
            OptimizeAction = () => Assert.DoesNotContain(events, runEvent =>
                runEvent.Message.Contains("Processing completed successfully", StringComparison.Ordinal))
        };
        var opener = new RecordingRecordStoreSessionOpener(recordStore);
        var sut = new ProcessingRunExecutor(new UnexpectedPluginIngestion(), opener);
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
        Assert.Equal(ProcessingRunEventKind.Status, events[^1].Kind);
        Assert.Equal("Processing completed successfully!", events[^1].Message);
        Assert.Equal(100, events[^1].Value);
    }

    /// <summary>
    ///     Verifies that best-effort Store cleanup cannot replace the optimization failure that ended the Processing Run.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OptimizationAndCleanupFail_PreservesOptimizationFailure()
    {
        var optimizationFailure = new InvalidOperationException("optimization failed");
        var recordStore = new RecordingRecordStoreSession
        {
            OptimizeException = optimizationFailure,
            DisposeException = new InvalidOperationException("cleanup failed")
        };
        using var sut = new ProcessingRunExecutor(
            new UnexpectedPluginIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync(CreateTextFileRequest("optimization-failure.txt"), progress));

        Assert.Same(optimizationFailure, exception);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Error during processing: optimization failed", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Message.Contains("cleanup failed", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Message.Contains("Processing completed successfully", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Verifies that cancellation observed after text import ends the Processing Run without optimization or completion.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledAfterTextImport_ReportsCancellationWithoutOptimization()
    {
        ProcessingRunExecutor? sut = null;
        var recordStore = new RecordingRecordStoreSession
        {
            ImportAction = () => sut!.Cancel()
        };
        sut = new ProcessingRunExecutor(
            new UnexpectedPluginIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        using (sut)
        {
            var events = new List<ProcessingRunEvent>();
            var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                sut.ExecuteAsync(CreateTextFileRequest("cancel-after-import.txt"), progress));

            Assert.Equal(0, recordStore.OptimizeCallCount);
            Assert.True(recordStore.Disposed);
            Assert.Contains(events, runEvent =>
                runEvent.Kind == ProcessingRunEventKind.Status &&
                runEvent.Message.Contains("Processing cancelled", StringComparison.Ordinal));
            Assert.DoesNotContain(events, runEvent =>
                runEvent.Message.Contains("Processing completed", StringComparison.Ordinal));
        }
    }

    /// <summary>
    ///     Verifies that Processing Run delegates the complete immutable selection to one Plugin Ingestion operation and
    ///     adapts its structured progress into user-facing run status.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PluginRun_InvokesAggregateIngestionOnceWithCompleteSelectionAndStructuredProgress()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var events = new List<ProcessingRunEvent>();
        var recordStore = new RecordingRecordStoreSession
        {
            OptimizeAction = () => Assert.DoesNotContain(events, runEvent =>
                runEvent.Message.Contains("Processing completed", StringComparison.Ordinal))
        };
        var ingestion = new RecordingReportPluginIngestion((request, _, progress, _) =>
        {
            progress?.Report(PluginIngestionProgress.PreparingLoadOrder(request.PluginNames.Length));
            progress?.Report(PluginIngestionProgress.IngestingPlugin("Second.esp", 2, request.PluginNames.Length));
            return Task.FromResult(new PluginIngestionReport(
                request,
                [new IngestedPlugin("First.esp", 2), new IngestedPlugin("Second.esp", 3)]));
        });
        var opener = new RecordingRecordStoreSessionOpener(recordStore);
        using var sut = new ProcessingRunExecutor(
            ingestion,
            opener);
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["First.esp", "Second.esp"],
            UpdateMode.ReplacePluginRecords);

        await sut.ExecuteAsync(request, progress);

        var call = Assert.Single(ingestion.Calls);
        Assert.Equal(gameDirectory, call.Request.GameDirectory);
        Assert.Equal(GameRelease.SkyrimSE, call.Request.GameRelease);
        Assert.Equal(["First.esp", "Second.esp"], call.Request.PluginNames);
        Assert.Equal(UpdateMode.ReplacePluginRecords, call.Request.UpdateMode);
        Assert.Same(recordStore, call.RecordStore);
        Assert.NotNull(call.Progress);
        Assert.True(call.CancellationToken.CanBeCanceled);
        Assert.Equal(opener.OpenCalls.Single().CancellationToken, call.CancellationToken);
        Assert.Equal(call.CancellationToken, recordStore.OptimizeCancellationToken);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Initializing plugin ingestion", StringComparison.Ordinal) &&
            runEvent.Value == 0);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Ingesting plugin 2 of 2: Second.esp", StringComparison.Ordinal) &&
            runEvent.Value == 100);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed successfully", StringComparison.Ordinal) &&
            runEvent.Value == 100);
        Assert.Equal(ProcessingRunEventKind.Status, events[^1].Kind);
        Assert.Equal("Processing completed successfully!", events[^1].Message);
        Assert.Equal(100, events[^1].Value);
    }

    /// <summary>
    ///     Verifies the defensive post-ingestion cancellation boundary prevents a normally returned report from reaching
    ///     Store optimization or terminal report formatting.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledAsPluginReportReturns_DoesNotOptimizeOrReportCompletion()
    {
        ProcessingRunExecutor? sut = null;
        var ingestion = new RecordingReportPluginIngestion((request, _, _, _) =>
        {
            sut!.Cancel();
            return Task.FromResult(new PluginIngestionReport(
                request,
                [new IngestedPlugin("Plugin.esp", 1)]));
        });
        var recordStore = new RecordingRecordStoreSession();
        sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        using (sut)
        {
            var events = new List<ProcessingRunEvent>();
            var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
            var request = new PluginProcessingRunRequest(
                CreateTempDirectory(),
                @"C:\Databases\formids.db",
                GameRelease.SkyrimSE,
                ["Plugin.esp"],
                UpdateMode.Append);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ExecuteAsync(request, progress));

            Assert.Single(ingestion.Calls);
            Assert.Equal(0, recordStore.OptimizeCallCount);
            Assert.True(recordStore.Disposed);
            Assert.Contains(events, runEvent =>
                runEvent.Kind == ProcessingRunEventKind.Status &&
                runEvent.Message.Contains("Processing cancelled", StringComparison.Ordinal));
            Assert.DoesNotContain(events, runEvent =>
                runEvent.Kind is ProcessingRunEventKind.Warning or ProcessingRunEventKind.Error ||
                runEvent.Message.Contains("Processing completed", StringComparison.Ordinal));
        }
    }

    /// <summary>
    ///     Verifies that failed successful-run maintenance suppresses aggregate report output and remains primary even when
    ///     Store cleanup also fails.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PluginOptimizationAndCleanupFail_PreservesOptimizationAndSuppressesReportOutput()
    {
        var optimizationFailure = new InvalidOperationException("plugin optimization failed");
        var ingestion = new RecordingReportPluginIngestion((request, _, _, _) =>
            Task.FromResult(new PluginIngestionReport(
                request,
                [
                    new IngestedPlugin(
                        "Warned.esp",
                        1,
                        new ProcessingWarning(1, ["recoverable detail"])),
                    new SkippedPlugin("Skipped.esp", SkippedPluginReason.NotPresentInLoadOrder),
                    new FailedPlugin(
                        "Failed.esp",
                        new PluginReadDiagnostic(PluginReadPhase.ReadingRecords, "read failed"))
                ])));
        var recordStore = new RecordingRecordStoreSession
        {
            OptimizeException = optimizationFailure,
            DisposeException = new InvalidOperationException("cleanup failed")
        };
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            CreateTempDirectory(),
            @"C:\Databases\formids.db",
            GameRelease.SkyrimSE,
            ["Warned.esp", "Skipped.esp", "Failed.esp"],
            UpdateMode.Append);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync(request, progress));

        Assert.Same(optimizationFailure, exception);
        Assert.Single(ingestion.Calls);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Error during processing: plugin optimization failed", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Kind is ProcessingRunEventKind.Warning or ProcessingRunEventKind.Error ||
            runEvent.Message.Contains("Processing completed", StringComparison.Ordinal) ||
            runEvent.Message.Contains("Warned.esp", StringComparison.Ordinal) ||
            runEvent.Message.Contains("Skipped.esp", StringComparison.Ordinal) ||
            runEvent.Message.Contains("Failed.esp", StringComparison.Ordinal) ||
            runEvent.Message.Contains("cleanup failed", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Verifies a typed load-order skip is optimized and formatted as a Processing Warning and warning completion.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PluginRunMissingSelectedPlugin_WarnsSkipsAndCompletesWithWarnings()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var ingestion = new RecordingReportPluginIngestion((request, _, _, _) =>
            Task.FromResult(new PluginIngestionReport(
                request,
                [new SkippedPlugin("Missing.esp", SkippedPluginReason.NotPresentInLoadOrder)])));
        var recordStore = new RecordingRecordStoreSession();
        using var sut = new ProcessingRunExecutor(
            ingestion,
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

        Assert.Single(ingestion.Calls);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Warning &&
            runEvent.Message.Contains("Missing.esp", StringComparison.Ordinal) &&
            runEvent.Message.Contains("load order", StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed with warnings", StringComparison.Ordinal) &&
            runEvent.Message.Contains("0 ingested", StringComparison.Ordinal) &&
            runEvent.Message.Contains("1 skipped", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed successfully", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Verifies a typed unavailable-file skip is formatted by Processing Run after successful Store optimization.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PluginRunMissingPluginFile_WarnsSkipsAndCompletesWithWarnings()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var ingestion = new RecordingReportPluginIngestion((request, _, _, _) =>
            Task.FromResult(new PluginIngestionReport(
                request,
                [new SkippedPlugin("Missing.esp", SkippedPluginReason.PluginFileUnavailable)])));
        var recordStore = new RecordingRecordStoreSession();
        using var sut = new ProcessingRunExecutor(
            ingestion,
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

        Assert.Single(ingestion.Calls);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Warning &&
            runEvent.Message.Contains(
                $"Missing.esp: Could not find plugin file: {Path.Combine(GameReleaseHelper.ResolveDataPath(gameDirectory), "Missing.esp")}",
                StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed with warnings", StringComparison.Ordinal) &&
            runEvent.Message.Contains("0 ingested", StringComparison.Ordinal) &&
            runEvent.Message.Contains("1 skipped", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Verifies ordered aggregate outcomes drive warning, failure, and terminal counts only after optimization.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_MixedAggregateReport_FormatsOutcomesAndCountsAfterOptimization()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var ingestion = new RecordingReportPluginIngestion((request, _, _, _) =>
            Task.FromResult(new PluginIngestionReport(
                request,
                [
                    new IngestedPlugin(
                        "Warned.esp",
                        2,
                        new ProcessingWarning(1, ["Recoverable issue"])),
                    new SkippedPlugin("Skipped.esp", SkippedPluginReason.ZeroFormIdRecords),
                    new FailedPlugin(
                        "Bad.esp",
                        new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header.")),
                    new IngestedPlugin("Good.esp", 3)
                ])));

        var events = new List<ProcessingRunEvent>();
        var recordStore = new RecordingRecordStoreSession
        {
            OptimizeAction = () => Assert.DoesNotContain(events, runEvent =>
                runEvent.Kind is ProcessingRunEventKind.Warning or ProcessingRunEventKind.Error ||
                runEvent.Message.Contains("Processing completed", StringComparison.Ordinal))
        };
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Warned.esp", "Skipped.esp", "Bad.esp", "Good.esp"],
            UpdateMode.Append);

        await sut.ExecuteAsync(request, progress);

        Assert.Single(ingestion.Calls);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Warning &&
            runEvent.Message.Contains("2 processing warnings", StringComparison.Ordinal) &&
            runEvent.Message.Contains("Warned.esp: 1 recoverable record issue.", StringComparison.Ordinal) &&
            runEvent.Message.Contains("Skipped.esp produced zero FormID records", StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Error &&
            runEvent.Message.Contains("1 failed plugin", StringComparison.Ordinal) &&
            runEvent.Message.Contains(
                "Bad.esp: Error opening Bad.esp: Invalid plugin header.",
                StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed with failures", StringComparison.Ordinal) &&
            runEvent.Message.Contains("2 ingested", StringComparison.Ordinal) &&
            runEvent.Message.Contains("1 skipped", StringComparison.Ordinal) &&
            runEvent.Message.Contains("1 failed", StringComparison.Ordinal));
        var warningIndex = events.FindIndex(runEvent => runEvent.Kind == ProcessingRunEventKind.Warning);
        var errorIndex = events.FindIndex(runEvent => runEvent.Kind == ProcessingRunEventKind.Error);
        var completionIndex = events.FindIndex(runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed", StringComparison.Ordinal));
        Assert.True(warningIndex >= 0 && warningIndex < errorIndex);
        Assert.True(errorIndex < completionIndex);
        Assert.Single(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed", StringComparison.Ordinal));
        Assert.Equal(ProcessingRunEventKind.Status, events[^1].Kind);
        Assert.Contains("Processing completed with failures", events[^1].Message, StringComparison.Ordinal);
        Assert.Equal(100, events[^1].Value);
    }

    /// <summary>
    ///     Verifies a thrown Plugin Ingestion failure remains primary, skips optimization, and survives cleanup failure.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PluginIngestionFailure_PreservesFailureWithoutOptimization()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var ingestionFailure = new InvalidOperationException("store failed");
        var ingestion = new ThrowingPluginIngestion(ingestionFailure);

        var recordStore = new RecordingRecordStoreSession
        {
            DisposeException = new InvalidOperationException("cleanup failed")
        };
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Bad.esp", "Never.esp"],
            UpdateMode.Append);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync(request, progress));

        Assert.Same(ingestionFailure, exception);
        Assert.Equal(1, ingestion.CallCount);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Error during processing: store failed", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Error &&
            runEvent.Message.Contains("failed plugin", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Message.Contains("cleanup failed", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Verifies Plugin Ingestion cancellation remains cancellation and cannot be replaced by Store cleanup failure.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PluginIngestionCancellation_PreservesCancellationWithoutOptimization()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var cancellation = new OperationCanceledException("cancelled during ingestion");
        var ingestion = new ThrowingPluginIngestion(cancellation);

        var recordStore = new RecordingRecordStoreSession
        {
            DisposeException = new InvalidOperationException("cleanup failed")
        };
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Done.esp", "Cancelled.esp"],
            UpdateMode.Append);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ExecuteAsync(request, progress));

        Assert.Same(cancellation, exception);
        Assert.Equal(1, ingestion.CallCount);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing cancelled", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Message.Contains("cleanup failed", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Verifies Processing Run formats bounded warning diagnostics and completes with warnings after optimization.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PluginWarningsWithoutFailures_CompletesWithWarnings()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var ingestion = new RecordingReportPluginIngestion((request, _, _, _) =>
            Task.FromResult(new PluginIngestionReport(
                request,
                [
                    new IngestedPlugin(
                        "Warned.esp",
                        2,
                        new ProcessingWarning(
                            7,
                            [
                                "First issue",
                                "Second issue",
                                "Third issue",
                                "Fourth issue",
                                "Fifth issue",
                                "Sixth issue",
                                "Seventh issue"
                            ])),
                    new IngestedPlugin("Second.esp", 1, new ProcessingWarning(1, ["Second detail"])),
                    new IngestedPlugin("Third.esp", 1, new ProcessingWarning(1, ["Third detail"])),
                    new IngestedPlugin("Fourth.esp", 1, new ProcessingWarning(1, ["Fourth detail"])),
                    new IngestedPlugin("Fifth.esp", 1, new ProcessingWarning(1, ["Fifth detail"])),
                    new IngestedPlugin("Sixth.esp", 1, new ProcessingWarning(1, ["Sixth detail"])),
                    new IngestedPlugin("Seventh.esp", 1, new ProcessingWarning(1, ["Seventh detail"]))
                ])));
        var events = new List<ProcessingRunEvent>();
        var recordStore = new RecordingRecordStoreSession
        {
            OptimizeAction = () => Assert.DoesNotContain(events, runEvent =>
                runEvent.Kind == ProcessingRunEventKind.Warning ||
                runEvent.Message.Contains("Processing completed", StringComparison.Ordinal))
        };
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            [
                "Warned.esp",
                "Second.esp",
                "Third.esp",
                "Fourth.esp",
                "Fifth.esp",
                "Sixth.esp",
                "Seventh.esp"
            ],
            UpdateMode.Append);

        await sut.ExecuteAsync(request, progress);

        Assert.Single(ingestion.Calls);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Warning &&
            runEvent.Message.Contains("7 processing warnings", StringComparison.Ordinal) &&
            runEvent.Message.Contains("Warned.esp: 7 recoverable record issues.", StringComparison.Ordinal) &&
            runEvent.Message.Contains(
                "First issue; Second issue; Third issue; Fourth issue; Fifth issue; and 2 more.",
                StringComparison.Ordinal) &&
            !runEvent.Message.Contains("Sixth issue", StringComparison.Ordinal) &&
            !runEvent.Message.Contains("Seventh issue", StringComparison.Ordinal) &&
            !runEvent.Message.Contains("Sixth.esp", StringComparison.Ordinal) &&
            !runEvent.Message.Contains("Seventh.esp", StringComparison.Ordinal) &&
            runEvent.Message.EndsWith("and 2 more.", StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed with warnings", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed successfully", StringComparison.Ordinal));
        Assert.Equal(ProcessingRunEventKind.Status, events[^1].Kind);
        Assert.Contains("Processing completed with warnings", events[^1].Message, StringComparison.Ordinal);
        Assert.Equal(100, events[^1].Value);
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
    public void PluginProcessingRunRequest_BlankPluginName_ThrowsRunValidationException()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new PluginProcessingRunRequest(
                @"C:\Games\Skyrim",
                @"C:\Databases\formids.db",
                GameRelease.SkyrimSE,
                ["Valid.esp", " "],
                UpdateMode.Append));

        Assert.Equal("Plugin name must be specified", exception.Message);
    }

    [Fact]
    public async Task PluginProcessingRunRequest_CaseInsensitiveDuplicateNames_RejectsBeforeStoreCanOpen()
    {
        var ingestion = new UnexpectedPluginIngestion();
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        using var sut = new ProcessingRunExecutor(ingestion, opener);

        var exception = await Assert.ThrowsAsync<ProcessingRunValidationException>(() =>
            sut.ExecuteAsync(new PluginProcessingRunRequest(
                @"C:\Games\Skyrim",
                @"C:\Databases\formids.db",
                GameRelease.SkyrimSE,
                ["Duplicate.esp", "DUPLICATE.ESP"],
                UpdateMode.Append)));

        Assert.Equal("Plugin names must be unique", exception.Message);
        Assert.Empty(opener.OpenCalls);
        Assert.Equal(0, ingestion.CallCount);
    }

    [Fact]
    public void PluginProcessingRunRequest_CallerMutatesSource_PreservesCapturedNamesAndOrder()
    {
        var pluginNames = new List<string> { "First.esp", "Second.esp" };
        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            @"C:\Databases\formids.db",
            GameRelease.SkyrimSE,
            pluginNames,
            UpdateMode.Append);

        pluginNames[0] = "Changed.esp";
        pluginNames.Reverse();

        Assert.Equal(["First.esp", "Second.esp"], request.PluginNames);
    }

    /// <summary>
    ///     Verifies active-run cancellation reaches the Plugin Ingestion interface token and prevents Store optimization.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledDuringPluginIngestion_ReportsCancelledAndThrows()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "cancel.db");
        var ingestion = new BlockingPluginIngestion();
        var recordStore = new RecordingRecordStoreSession();
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
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
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
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
        using var sut = new ProcessingRunExecutor(new UnexpectedPluginIngestion(), opener);

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
        using var sut = new ProcessingRunExecutor(new UnexpectedPluginIngestion(), opener);

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
        using var sut = new ProcessingRunExecutor(new UnexpectedPluginIngestion(), opener);
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
            new UnexpectedPluginIngestion(),
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

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            handler(value);
        }
    }

    private sealed class RecordingReportPluginIngestion(
        Func<
            SelectedPluginIngestionRequest,
            IFormIdRecordStoreSession,
            IProgress<PluginIngestionProgress>?,
            CancellationToken,
            Task<PluginIngestionReport>> response) : IPluginIngestion
    {
        public List<(
            SelectedPluginIngestionRequest Request,
            IFormIdRecordStoreSession RecordStore,
            IProgress<PluginIngestionProgress>? Progress,
            CancellationToken CancellationToken)> Calls { get; } = [];

        /// <inheritdoc />
        public Task<PluginIngestionReport> IngestAsync(
            SelectedPluginIngestionRequest request,
            IFormIdRecordStoreSession recordStore,
            IProgress<PluginIngestionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((request, recordStore, progress, cancellationToken));
            return response(request, recordStore, progress, cancellationToken);
        }
    }

    private sealed class UnexpectedPluginIngestion : IPluginIngestion
    {
        public int CallCount { get; private set; }

        /// <inheritdoc />
        public Task<PluginIngestionReport> IngestAsync(
            SelectedPluginIngestionRequest request,
            IFormIdRecordStoreSession recordStore,
            IProgress<PluginIngestionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException<PluginIngestionReport>(
                new InvalidOperationException("Plugin Ingestion was not expected for this Processing Run."));
        }
    }

    private sealed class ThrowingPluginIngestion(Exception exception) : IPluginIngestion
    {
        public int CallCount { get; private set; }

        /// <inheritdoc />
        public Task<PluginIngestionReport> IngestAsync(
            SelectedPluginIngestionRequest request,
            IFormIdRecordStoreSession recordStore,
            IProgress<PluginIngestionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException<PluginIngestionReport>(exception);
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

    private sealed class BlockingPluginIngestion : IPluginIngestion
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public async Task<PluginIngestionReport> IngestAsync(
            SelectedPluginIngestionRequest request,
            IFormIdRecordStoreSession recordStore,
            IProgress<PluginIngestionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new UnreachableException();
        }
    }

    private sealed class RecordingRecordStoreSessionOpener(IFormIdRecordStoreSession recordStore)
        : IFormIdRecordStoreSessionOpener
    {
        public List<(
            string DatabasePath,
            GameRelease GameRelease,
            CancellationToken CancellationToken)> OpenCalls { get; } = [];

        public Task<IFormIdRecordStoreSession> OpenAsync(
            string databasePath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            OpenCalls.Add((databasePath, gameRelease, cancellationToken));
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

        public CancellationToken? OptimizeCancellationToken { get; private set; }

        public bool Disposed { get; private set; }

        public Exception? OptimizeException { get; init; }

        public Exception? DisposeException { get; init; }

        public Action? ImportAction { get; init; }

        public Action? OptimizeAction { get; init; }

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

        /// <inheritdoc />
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

            ImportAction?.Invoke();

            return Task.FromResult(TextFileImportResult);
        }

        /// <inheritdoc />
        public Task OptimizeAsync(CancellationToken cancellationToken = default)
        {
            OptimizeCallCount++;
            OptimizeCancellationToken = cancellationToken;
            OptimizeAction?.Invoke();
            return OptimizeException is null
                ? Task.CompletedTask
                : Task.FromException(OptimizeException);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return DisposeException is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(DisposeException);
        }
    }
}
