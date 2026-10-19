#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers what a Processing Run does: which outcome it returns, which typed progress it reports and in what order,
///     which collaborators it reaches with which arguments, and how it ends when it is cancelled or fails.
/// </summary>
/// <remarks>
///     Issue #68, under parent #61. This suite asserts facts and no wording. A run reports typed progress and returns a
///     typed outcome now, and <see cref="ProcessingRunPresentation" /> turns both into the words the user reads — so
///     every user-facing string a run produces is pinned by <see cref="ProcessingRunPresentationTests" />, and the two
///     suites cannot fail for each other's reasons. The request validation wording a run rejects on lives with the
///     request types in <see cref="ProcessingRunContractTests" /> for the same reason.
/// </remarks>
public sealed class ProcessingRunExecutorTests : IDisposable
{
    // Set only while the offload test is synchronously inside ExecuteAsync, and cleared before that thread can return
    // to the pool, so it reads true only on the caller's own stack. A thread-id comparison could not do this: once the
    // caller awaits, its pool thread is free to pick up the queued run and would report the same id.
    [ThreadStatic] private static bool _executeCallInProgressOnThisThread;

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
    ///     Pins that neither the executor nor its seam interface holds run state a caller could reach: there is no
    ///     cancellation request and no disposal, because the caller owns the run's cancellation token.
    /// </summary>
    /// <remarks>
    ///     Issue #104, under parent #102. Processing Run Control owns whether a run is active and creates its token the
    ///     moment admission admits a press (ADR-0012); an executor that also held a cancellation slot would reopen the
    ///     startup window in which a cancel was dropped.
    /// </remarks>
    [Fact]
    public void Type_ExposesNoCancelAndNoDispose()
    {
        foreach (var type in new[] { typeof(ProcessingRunExecutor), typeof(IProcessingRunExecutor) })
        {
            Assert.False(typeof(IDisposable).IsAssignableFrom(type), $"{type.Name} must not be disposable.");
            Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(type), $"{type.Name} must not be disposable.");
            Assert.DoesNotContain(
                type.GetMethods(),
                method => method.Name is "Cancel" or "Dispose" or "DisposeAsync");
        }
    }

    /// <summary>
    ///     Verifies that a token cancelled before the run starts still enters the worker and ends the run as a
    ///     cancelled outcome, without opening a FormID Record Store.
    /// </summary>
    /// <remarks>
    ///     Issue #104, under parent #102. Returning the outcome rather than throwing is the observation that the worker
    ///     ran: had the token been handed to <see cref="Task.Run(Func{Task})" /> scheduling, the task would have been
    ///     cancelled before the worker started and awaiting it would throw instead. This is the case a cancel pressed
    ///     during startup produces, so the user still gets the normal cancellation acknowledgement.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_PreCancelledToken_ReturnsCancelledFromTheWorkerWithoutOpeningAStore()
    {
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        var textIngestion = new UnexpectedFormIdTextIngestion();
        var sut = new ProcessingRunExecutor(new UnexpectedPluginIngestion(), textIngestion, opener);
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

        var outcome = await sut.ExecuteAsync(
            CreateTextFileRequest("pre-cancelled.txt"),
            progress,
            new CancellationToken(canceled: true));

        Assert.IsType<CancelledRunOutcome>(outcome);
        Assert.Empty(opener.OpenCalls);
        Assert.Equal(0, textIngestion.CallCount);
        Assert.Empty(reports);
    }

    /// <summary>
    ///     Verifies that the token the caller supplies is the one the run cancels on: cancelling it while the run is
    ///     inside its import ends the run as a cancelled outcome without optimization.
    /// </summary>
    /// <remarks>
    ///     Issue #104, under parent #102. The cancel is issued from inside the import, so where the run is when it
    ///     arrives is fixed by construction rather than by how quickly the thread pool schedules the worker.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_CallerTokenCancelledMidRun_ReturnsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var recordStore = new RecordingRecordStoreSession();
        var opener = new RecordingRecordStoreSessionOpener(recordStore);
        var textIngestion = new RecordingFormIdTextIngestion { IngestAction = cancellation.Cancel };
        var sut = new ProcessingRunExecutor(new UnexpectedPluginIngestion(), textIngestion, opener);

        var outcome = await sut.ExecuteAsync(
            CreateTextFileRequest("cancelled-mid-run.txt"),
            null,
            cancellation.Token);

        Assert.IsType<CancelledRunOutcome>(outcome);
        Assert.Equal(cancellation.Token, opener.OpenCalls.Single().CancellationToken);
        Assert.Equal(cancellation.Token, textIngestion.IngestCalls.Single().CancellationToken);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
    }

    /// <summary>
    ///     Verifies that a FormID text-file dry run plans through FormID Text Ingestion, carrying the request and the
    ///     caller's token, and returns that plan without opening a FormID Record Store or ingesting anything.
    /// </summary>
    /// <remarks>
    ///     Issue #110. What the plan says about the file — its presence and size, and that it is never opened — is
    ///     FormID Text Ingestion's to decide and is pinned by <see cref="FormIdTextIngestionTests" />.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_DryRunFormIdTextFile_PlansThroughFormIdTextIngestionWithoutOpeningAStore()
    {
        var expectedPlan = new FormIdTextRunPlan(@"C:\Imports\formids.txt", 23);
        var pluginIngestion = new PlanningPluginIngestion();
        var textIngestion = new RecordingFormIdTextIngestion { Plan = expectedPlan };
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        using var cancellation = new CancellationTokenSource();
        var sut = new ProcessingRunExecutor(pluginIngestion, textIngestion, opener);
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new FormIdTextProcessingRunRequest(
            @"C:\Imports\formids.txt",
            string.Empty,
            GameRelease.SkyrimSE,
            UpdateMode.Append,
            dryRun: true);

        var outcome = await sut.ExecuteAsync(request, progress, cancellation.Token);

        Assert.Empty(opener.OpenCalls);
        Assert.Empty(pluginIngestion.PlanCalls);
        Assert.Empty(textIngestion.IngestCalls);
        Assert.Empty(reports);
        var planCall = Assert.Single(textIngestion.PlanCalls);
        Assert.Same(request, planCall.Request);
        Assert.Equal(cancellation.Token, planCall.CancellationToken);
        Assert.Same(expectedPlan, Assert.IsType<PlannedRunOutcome>(outcome).Plan);
    }

    /// <summary>
    ///     Verifies a selected-Plugin dry run plans through Plugin Ingestion, carrying the captured selection and the
    ///     caller's cancellation token, without opening a Store or ingesting anything.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DryRunPluginRun_PlansThroughPluginIngestionWithoutOpeningDatabase()
    {
        var databasePath = CreateTempFilePath("dry-run.db");
        var ingestion = new PlanningPluginIngestion();
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        using var cancellation = new CancellationTokenSource();
        var sut = new ProcessingRunExecutor(ingestion, new UnexpectedFormIdTextIngestion(), opener);
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, @"C:\Games\Skyrim"),
            databasePath,
            ["PluginA.esp", "PluginB.esp"],
            UpdateMode.Append,
            dryRun: true);

        var outcome = await sut.ExecuteAsync(request, progress, cancellation.Token);

        Assert.False(File.Exists(databasePath));
        Assert.Empty(opener.OpenCalls);
        Assert.Equal(0, ingestion.IngestCallCount);
        Assert.Empty(reports);
        var planCall = Assert.Single(ingestion.PlanCalls);
        Assert.Same(request, planCall.Request);
        Assert.Equal(cancellation.Token, planCall.CancellationToken);
        Assert.Equal(["PluginA.esp", "PluginB.esp"], AssertPlannedPluginNames(outcome));
    }

    /// <summary>
    ///     Verifies that a plan's run-level master failure reaches the caller unchanged, so a dry run surfaces the
    ///     ADR-0006 condition exactly as a real run does — before the user commits to one.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DryRunPluginRunWithUnresolvableMaster_PropagatesThatFailure()
    {
        var failure = new UnresolvableMasterException("Patch.esp", "Starfield.esm");
        var ingestion = new PlanningPluginIngestion { PlanFailure = failure };
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        var sut = new ProcessingRunExecutor(ingestion, new UnexpectedFormIdTextIngestion(), opener);
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

        var thrown = await Assert.ThrowsAsync<UnresolvableMasterException>(() =>
            sut.ExecuteAsync(CreateDryRunRequest("Patch.esp"), progress, CancellationToken.None));

        Assert.Same(failure, thrown);
        Assert.Empty(opener.OpenCalls);
    }

    /// <summary>
    ///     Verifies a selected-Plugin dry run does not require a database path because it invokes no Store lifecycle.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DryRunPluginRun_AllowsEmptyDatabasePath()
    {
        var ingestion = new PlanningPluginIngestion();
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        var sut = new ProcessingRunExecutor(ingestion, new UnexpectedFormIdTextIngestion(), opener);
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, @"C:\Games\Skyrim"),
            string.Empty,
            ["PluginA.esp"],
            UpdateMode.Append,
            dryRun: true);

        var outcome = await sut.ExecuteAsync(request, progress, CancellationToken.None);

        Assert.Empty(opener.OpenCalls);
        Assert.Equal(0, ingestion.IngestCallCount);
        Assert.Equal(["PluginA.esp"], AssertPlannedPluginNames(outcome));
    }

    /// <summary>
    ///     Verifies that a FormID text run opens the Store for the request and hands FormID Text Ingestion the request,
    ///     the opened session's text-row role, and the caller's token; then reports the finished import, optimizes,
    ///     disposes, and returns the counts ingestion returned.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FormIdTextRun_HandsTheStoresTextRowRoleToIngestionThenOptimizes()
    {
        var events = new List<string>();
        var recordStore = new RecordingRecordStoreSession { OptimizeAction = () => events.Add("optimize") };
        var opener = new RecordingRecordStoreSessionOpener(recordStore);
        var textIngestion = new RecordingFormIdTextIngestion
        {
            Result = new FormIdTextFileImportResult(2, 2),
            IngestAction = () => events.Add("ingest")
        };
        using var cancellation = new CancellationTokenSource();
        var sut = new ProcessingRunExecutor(new UnexpectedPluginIngestion(), textIngestion, opener);
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(report =>
        {
            reports.Add(report);
            events.Add("report");
        });
        var request = new FormIdTextProcessingRunRequest(
            @"C:\Imports\formids.txt",
            @"C:\Databases\formids.db",
            GameRelease.SkyrimSE,
            UpdateMode.Append);

        var outcome = await sut.ExecuteAsync(request, progress, cancellation.Token);

        var openCall = Assert.Single(opener.OpenCalls);
        Assert.Equal(@"C:\Databases\formids.db", openCall.DatabasePath);
        Assert.Equal(GameRelease.SkyrimSE, openCall.GameRelease);
        var ingestCall = Assert.Single(textIngestion.IngestCalls);
        Assert.Same(request, ingestCall.Request);
        Assert.Same(recordStore, ingestCall.RecordStore);
        Assert.Equal(cancellation.Token, ingestCall.CancellationToken);
        Assert.Empty(textIngestion.PlanCalls);
        // The finished import is reported before maintenance: the counts describe the import, not the run.
        Assert.Equal(["ingest", "report", "optimize"], events);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        // FormID Text Ingestion counts and the run restates them; neither writes the sentence the user finally reads.
        Assert.Equal(
            [new ImportedFormIdText(new FormIdTextFileImportResult(2, 2))],
            reports);
        Assert.Equal(
            new FormIdTextFileImportResult(2, 2),
            Assert.IsType<FormIdTextRunOutcome>(outcome).ImportResult);
    }

    /// <summary>
    ///     Verifies that an appending text run restates FormID Text Ingestion's opening and counter updates and drops
    ///     every Plugin first encounter, because append mode has never shown Plugin-named status updates.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FormIdTextRunInAppendMode_RestatesOpeningAndCountersAndDropsFirstEncounters()
    {
        var reports = await ExecuteTextRunWithIngestionProgressAsync(UpdateMode.Append);

        Assert.Equal(
            [
                new OpeningFormIdText(),
                new ImportingFormIdText(1000, 32, 64),
                new ImportedFormIdText(new FormIdTextFileImportResult(2, 1001))
            ],
            reports);
    }

    /// <summary>
    ///     Verifies that a replacing text run restates the opening as the run's opening case, each counter update as
    ///     the run's counter case, and each first encounter as the run's named-Plugin case, in the order FormID Text
    ///     Ingestion reported them.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FormIdTextRunInReplaceMode_RestatesEveryCaseAndNamesEachFirstEncounteredPlugin()
    {
        var reports = await ExecuteTextRunWithIngestionProgressAsync(UpdateMode.ReplacePluginRecords);

        Assert.Equal(
            [
                new OpeningFormIdText(),
                new ImportingFormIdTextPlugin("PluginA.esp"),
                new ImportingFormIdText(1000, 32, 64),
                new ImportingFormIdTextPlugin("PluginB.esp"),
                new ImportedFormIdText(new FormIdTextFileImportResult(2, 1001))
            ],
            reports);
    }

    /// <summary>
    ///     Executes one FormID text-file run against FormID Text Ingestion that reports a fixed sequence of facts, and
    ///     collects the progress the run restated them as.
    /// </summary>
    /// <param name="updateMode">The update mode the run applies, which decides whether Plugins are named.</param>
    /// <returns>The run's own progress reports, in report order.</returns>
    /// <remarks>
    ///     The sequence holds one of each case: the opening, a first encounter, a counter update, and a second first
    ///     encounter after it.
    /// </remarks>
    private static async Task<IReadOnlyList<ProcessingRunProgress>> ExecuteTextRunWithIngestionProgressAsync(
        UpdateMode updateMode)
    {
        var reports = new List<ProcessingRunProgress>();
        var textIngestion = new RecordingFormIdTextIngestion
        {
            Result = new FormIdTextFileImportResult(2, 1001),
            ProgressReports =
            [
                new FormIdTextIngestionOpening(64),
                new FormIdTextIngestionPluginFirstEncountered(1, 16, 64, "PluginA.esp"),
                new FormIdTextIngestionCounterUpdate(1000, 32, 64),
                new FormIdTextIngestionPluginFirstEncountered(1001, 48, 64, "PluginB.esp")
            ]
        };
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        var sut = new ProcessingRunExecutor(new UnexpectedPluginIngestion(), textIngestion, opener);
        var request = new FormIdTextProcessingRunRequest(
            @"C:\FormIds\formids.txt",
            @"C:\FormIds\formids.db",
            GameRelease.SkyrimSE,
            updateMode);

        await sut.ExecuteAsync(request, new SynchronousProgress<ProcessingRunProgress>(reports.Add), CancellationToken.None);
        return reports;
    }

    /// <summary>
    ///     Verifies that best-effort Store cleanup cannot replace the optimization failure that ended the Processing Run.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OptimizationAndCleanupFail_PreservesOptimizationFailure()
    {
        var optimizationFailure = new InvalidOperationException("optimization failed");
        var cleanupFailure = new InvalidOperationException("cleanup failed");
        var recordStore = new RecordingRecordStoreSession
        {
            OptimizeException = optimizationFailure,
            DisposeException = cleanupFailure
        };
        var sut = new ProcessingRunExecutor(
            new UnexpectedPluginIngestion(),
            new RecordingFormIdTextIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync(CreateTextFileRequest("optimization-failure.txt"), progress, CancellationToken.None));

        Assert.Same(optimizationFailure, exception);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
    }

    /// <summary>
    ///     Verifies that cancellation observed after FormID Text Ingestion returns ends the Processing Run as
    ///     cancelled, without optimization and without a completed outcome.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledAfterTextIngestion_ReturnsCancelledWithoutOptimization()
    {
        using var cancellation = new CancellationTokenSource();
        var recordStore = new RecordingRecordStoreSession();
        var sut = new ProcessingRunExecutor(
            new UnexpectedPluginIngestion(),
            new RecordingFormIdTextIngestion { IngestAction = cancellation.Cancel },
            new RecordingRecordStoreSessionOpener(recordStore));

        var outcome = await sut.ExecuteAsync(
            CreateTextFileRequest("cancel-after-import.txt"),
            null,
            cancellation.Token);

        Assert.IsType<CancelledRunOutcome>(outcome);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
    }

    /// <summary>
    ///     Verifies that cancellation accepted after successful optimization still ends the text-file run as cancelled
    ///     rather than as a completed import.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledAfterTextOptimization_ReturnsCancelledRatherThanTheImportResult()
    {
        using var cancellation = new CancellationTokenSource();
        var recordStore = new RecordingRecordStoreSession
        {
            OptimizeAction = cancellation.Cancel
        };
        var sut = new ProcessingRunExecutor(
            new UnexpectedPluginIngestion(),
            new RecordingFormIdTextIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));

        var outcome = await sut.ExecuteAsync(
            CreateTextFileRequest("cancel-after-optimize.txt"),
            null,
            cancellation.Token);

        Assert.IsType<CancelledRunOutcome>(outcome);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
    }

    /// <summary>
    ///     Verifies that Processing Run delegates the complete immutable selection to one Plugin Ingestion operation,
    ///     restates its structured progress in the run's own vocabulary, and returns the report it produced.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PluginRun_InvokesAggregateIngestionOnceWithCompleteSelectionAndStructuredProgress()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var reports = new List<ProcessingRunProgress>();
        var recordStore = new RecordingRecordStoreSession();
        var ingestion = new RecordingReportPluginIngestion((request, _, progress, _) =>
        {
            progress?.Report(PluginIngestionProgress.PreparingLoadOrder(request.PluginNames.Count));
            progress?.Report(PluginIngestionProgress.IngestingPlugin("Second.esp", 2, request.PluginNames.Count));
            return Task.FromResult(new PluginIngestionReport(
                request,
                [new IngestedPlugin("First.esp", 2), new IngestedPlugin("Second.esp", 3)]));
        });
        var opener = new RecordingRecordStoreSessionOpener(recordStore);
        var sut = new ProcessingRunExecutor(
            ingestion,
            new UnexpectedFormIdTextIngestion(),
            opener);
        using var cancellation = new CancellationTokenSource();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
            databasePath,
            ["First.esp", "Second.esp"],
            UpdateMode.ReplacePluginRecords);

        var outcome = await sut.ExecuteAsync(request, progress, cancellation.Token);

        var call = Assert.Single(ingestion.Calls);
        Assert.Same(request, call.Request);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory), call.Request.Source);
        Assert.Equal(GameRelease.SkyrimSE, call.Request.GameRelease);
        Assert.Equal(["First.esp", "Second.esp"], call.Request.PluginNames);
        Assert.Equal(UpdateMode.ReplacePluginRecords, call.Request.UpdateMode);
        Assert.Same(recordStore, call.RecordStore);
        Assert.NotNull(call.Progress);
        Assert.Equal(cancellation.Token, call.CancellationToken);
        Assert.Equal(opener.OpenCalls.Single().CancellationToken, call.CancellationToken);
        Assert.Equal(call.CancellationToken, recordStore.OptimizeCancellationToken);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        Assert.Contains(reports, reported => reported is PreparingLoadOrder(2));
        Assert.Contains(reports, reported => reported is IngestingPlugin("Second.esp", 2, 2));
        AssertNoTerminalReportOnTheProgressChannel(reports);
        Assert.Equal(
            ["First.esp", "Second.esp"],
            Assert.IsType<PluginRunOutcome>(outcome).Report.Outcomes.Select(reported => reported.PluginName));
    }

    /// <summary>
    ///     Verifies the defensive post-ingestion cancellation boundary prevents a normally returned report from reaching
    ///     Store optimization or the caller.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledAsPluginReportReturns_ReturnsCancelledWithoutOptimizing()
    {
        using var cancellation = new CancellationTokenSource();
        var ingestion = new RecordingReportPluginIngestion((request, _, _, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new PluginIngestionReport(
                request,
                [new IngestedPlugin("Plugin.esp", 1)]));
        });
        var recordStore = new RecordingRecordStoreSession();
        var sut = new ProcessingRunExecutor(
            ingestion,
            new UnexpectedFormIdTextIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, CreateTempDirectory()),
            @"C:\Databases\formids.db",
            ["Plugin.esp"],
            UpdateMode.Append);

        var outcome = await sut.ExecuteAsync(request, progress, cancellation.Token);

        Assert.IsType<CancelledRunOutcome>(outcome);
        Assert.Single(ingestion.Calls);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        AssertNoTerminalReportOnTheProgressChannel(reports);
    }

    /// <summary>
    ///     Verifies that cancellation accepted after successful optimization discards the report, so a cancelled run
    ///     cannot hand its caller warning, failure, or completion facts.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledAfterPluginOptimization_ReturnsCancelledRatherThanTheReport()
    {
        using var cancellation = new CancellationTokenSource();
        var ingestion = new RecordingReportPluginIngestion((request, _, _, _) =>
            Task.FromResult(new PluginIngestionReport(
                request,
                [
                    new IngestedPlugin("Warned.esp", 1, new ProcessingWarning(1, ["Warned detail"])),
                    new SkippedPlugin("Skipped.esp", SkippedPluginReason.ZeroFormIdRecords),
                    new FailedPlugin(
                        "Failed.esp",
                        new PluginReadDiagnostic(PluginReadPhase.ReadingRecords, "read failed"))
                ])));
        var recordStore = new RecordingRecordStoreSession
        {
            OptimizeAction = cancellation.Cancel
        };
        var sut = new ProcessingRunExecutor(
            ingestion,
            new UnexpectedFormIdTextIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, CreateTempDirectory()),
            @"C:\Databases\formids.db",
            ["Warned.esp", "Skipped.esp", "Failed.esp"],
            UpdateMode.Append);

        var outcome = await sut.ExecuteAsync(request, progress, cancellation.Token);

        Assert.IsType<CancelledRunOutcome>(outcome);
        Assert.Single(ingestion.Calls);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        AssertNoTerminalReportOnTheProgressChannel(reports);
    }

    /// <summary>
    ///     Verifies that failed successful-run maintenance withholds the aggregate report and remains primary even when
    ///     Store cleanup also fails.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PluginOptimizationAndCleanupFail_PreservesOptimizationAndWithholdsTheReport()
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
        var cleanupFailure = new InvalidOperationException("cleanup failed");
        var recordStore = new RecordingRecordStoreSession
        {
            OptimizeException = optimizationFailure,
            DisposeException = cleanupFailure
        };
        var sut = new ProcessingRunExecutor(
            ingestion,
            new UnexpectedFormIdTextIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, CreateTempDirectory()),
            @"C:\Databases\formids.db",
            ["Warned.esp", "Skipped.esp", "Failed.esp"],
            UpdateMode.Append);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync(request, progress, CancellationToken.None));

        Assert.Same(optimizationFailure, exception);
        Assert.Single(ingestion.Calls);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        AssertNoTerminalReportOnTheProgressChannel(reports);
        // No Plugin from the withheld report reached the channel.
        Assert.DoesNotContain(reports, reported => reported is IngestingPlugin);
    }

    /// <summary>
    ///     Verifies ordered aggregate outcomes reach the caller intact, and only after successful optimization.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_MixedAggregateReport_ReturnsTheOrderedOutcomesAfterOptimization()
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

        var reports = new List<ProcessingRunProgress>();
        var optimizedBeforeReturning = false;
        var recordStore = new RecordingRecordStoreSession
        {
            OptimizeAction = () => optimizedBeforeReturning = true
        };
        var sut = new ProcessingRunExecutor(
            ingestion,
            new UnexpectedFormIdTextIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
            databasePath,
            ["Warned.esp", "Skipped.esp", "Bad.esp", "Good.esp"],
            UpdateMode.Append);

        var outcome = await sut.ExecuteAsync(request, progress, CancellationToken.None);

        Assert.Single(ingestion.Calls);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(optimizedBeforeReturning);
        Assert.True(recordStore.Disposed);
        AssertNoTerminalReportOnTheProgressChannel(reports);

        var outcomes = Assert.IsType<PluginRunOutcome>(outcome).Report.Outcomes;
        Assert.Equal(["Warned.esp", "Skipped.esp", "Bad.esp", "Good.esp"], outcomes.Select(o => o.PluginName));
        Assert.NotNull(Assert.IsType<IngestedPlugin>(outcomes[0]).Warning);
        Assert.Equal(SkippedPluginReason.ZeroFormIdRecords, Assert.IsType<SkippedPlugin>(outcomes[1]).Reason);
        Assert.Equal(PluginReadPhase.OpeningPlugin, Assert.IsType<FailedPlugin>(outcomes[2]).Diagnostic.Phase);
        Assert.Null(Assert.IsType<IngestedPlugin>(outcomes[3]).Warning);
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

        var cleanupFailure = new InvalidOperationException("cleanup failed");
        var recordStore = new RecordingRecordStoreSession
        {
            DisposeException = cleanupFailure
        };
        var sut = new ProcessingRunExecutor(
            ingestion,
            new UnexpectedFormIdTextIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
            databasePath,
            ["Bad.esp", "Never.esp"],
            UpdateMode.Append);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync(request, progress, CancellationToken.None));

        Assert.Same(ingestionFailure, exception);
        Assert.Equal(1, ingestion.CallCount);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        AssertNoTerminalReportOnTheProgressChannel(reports);
    }

    /// <summary>
    ///     Verifies a cancellation raised while the caller's token is not cancelled still propagates unchanged, and
    ///     cannot be replaced by Store cleanup failure.
    /// </summary>
    /// <remarks>
    ///     Only the cancellation the caller asked for becomes an outcome, and this one was not: the token the caller
    ///     handed the run is live and was never cancelled (ADR-0007). Reinterpreting a collaborator's unexplained
    ///     cancellation as a cancelled run would claim the user pressed something they did not.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_CancellationWhileTheGivenTokenIsNotCancelled_PropagatesWithoutBecomingACancelledOutcome()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "plugins.db");
        var cancellation = new OperationCanceledException("cancelled during ingestion");
        var ingestion = new ThrowingPluginIngestion(cancellation);

        var cleanupFailure = new InvalidOperationException("cleanup failed");
        var recordStore = new RecordingRecordStoreSession
        {
            DisposeException = cleanupFailure
        };
        var sut = new ProcessingRunExecutor(
            ingestion,
            new UnexpectedFormIdTextIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        // A live token that could be cancelled but is not, so the propagation is decided by its state rather than by
        // the token being uncancellable.
        using var callerCancellation = new CancellationTokenSource();
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
            databasePath,
            ["Done.esp", "Cancelled.esp"],
            UpdateMode.Append);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.ExecuteAsync(request, progress, callerCancellation.Token));

        Assert.Same(cancellation, exception);
        Assert.False(callerCancellation.IsCancellationRequested);
        Assert.Equal(1, ingestion.CallCount);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        AssertNoTerminalReportOnTheProgressChannel(reports);
    }

    /// <summary>
    ///     Verifies that cancelling the caller's token reaches the Plugin Ingestion interface token, prevents Store
    ///     optimization, and ends the run as a cancelled outcome.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledDuringPluginIngestion_StopsBeforeOptimizationAndReturnsCancelled()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "cancel.db");
        var ingestion = new CancellationObservingPluginIngestion();
        var recordStore = new RecordingRecordStoreSession();
        var sut = new ProcessingRunExecutor(
            ingestion,
            new UnexpectedFormIdTextIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        using var cancellation = new CancellationTokenSource();

        // Cancelling from inside ingestion pins where the run is when cancellation arrives, with no wait on the test
        // thread for the worker to get there.
        ingestion.OnIngestionStarted = cancellation.Cancel;
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
            databasePath,
            ["Plugin.esp"],
            UpdateMode.Append);

        Assert.IsType<CancelledRunOutcome>(await sut.ExecuteAsync(request, null, cancellation.Token));
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
    }

    /// <summary>
    ///     Verifies that synchronous Store initialization is offloaded rather than run on the caller's stack, so a
    ///     blocking open cannot hold up the thread that started the run.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SynchronousRecordStoreInitialization_DoesNotRunOnCallersStack()
    {
        var opener = new InterceptingRecordStoreSessionOpener();
        var sut = new ProcessingRunExecutor(new UnexpectedPluginIngestion(), new RecordingFormIdTextIngestion(), opener);
        var openedOnCallersStack = true;
        opener.OnOpen = _ =>
        {
            openedOnCallersStack = _executeCallInProgressOnThisThread;
            return new RecordingRecordStoreSession();
        };

        Task<ProcessingRunOutcome> processingTask;
        _executeCallInProgressOnThisThread = true;
        try
        {
            processingTask = sut.ExecuteAsync(CreateTextFileRequest("offloaded.txt"), null, CancellationToken.None);
        }
        finally
        {
            _executeCallInProgressOnThisThread = false;
        }

        // The completed text outcome proves the open ran, so the flag it captured is a real observation.
        Assert.IsType<FormIdTextRunOutcome>(await processingTask);
        Assert.False(openedOnCallersStack);
    }

    /// <summary>
    ///     Verifies that a Store that cannot be opened fails the run by propagating that failure unchanged.
    /// </summary>
    /// <remarks>
    ///     Issue #104, under parent #102. The run no longer restates a failure on the progress channel before
    ///     rethrowing it: a failure reaches the user once, through the caller's failure renderer (ADR-0007).
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_FatalInitializationError_PropagatesTheFailure()
    {
        var initializationFailure = new InvalidOperationException("open failed");
        var sut = new ProcessingRunExecutor(
            new UnexpectedPluginIngestion(),
            new UnexpectedFormIdTextIngestion(),
            new ThrowingRecordStoreSessionOpener(initializationFailure));
        var databasePath = Path.Combine(CreateTempDirectory(), "fatal.db");
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, @"C:\Games\Skyrim"),
            databasePath,
            ["Plugin.esp"],
            UpdateMode.Append);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            sut.ExecuteAsync(request, progress, CancellationToken.None));

        Assert.Same(initializationFailure, thrown);
        AssertNoTerminalReportOnTheProgressChannel(reports);
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
    ///     Asserts that a run said nothing about how it ended on the progress channel.
    /// </summary>
    /// <param name="reports">The complete progress reported by the executor.</param>
    /// <remarks>
    ///     Issue #64. Terminal facts are rendered from the returned outcome by <c>ProcessingRunPresentation</c>, and
    ///     Processing Warnings and Failed Plugins have no progress case at all, so what remains to check is that a
    ///     selected-Plugin run reported no completed import either — the one completion fact the vocabulary still
    ///     carries, and one that belongs to a text run rather than this one.
    /// </remarks>
    private static void AssertNoTerminalReportOnTheProgressChannel(IReadOnlyList<ProcessingRunProgress> reports)
    {
        Assert.DoesNotContain(reports, reported => reported is ImportedFormIdText);
    }

    /// <summary>
    ///     Asserts that an outcome is a selected-Plugin plan and returns the Plugin names it planned.
    /// </summary>
    /// <param name="outcome">The outcome returned by a dry run.</param>
    /// <returns>The planned Plugin names, in selection order.</returns>
    private static IReadOnlyList<string> AssertPlannedPluginNames(ProcessingRunOutcome outcome)
    {
        var pluginPlan = Assert.IsType<PluginRunPlan>(Assert.IsType<PlannedRunOutcome>(outcome).Plan).Plan;

        return pluginPlan.Plugins.Select(planned => planned.PluginName).ToArray();
    }

    /// <summary>
    ///     Creates a valid dry-run request that requires no filesystem or FormID Record Store access.
    /// </summary>
    /// <param name="pluginName">The selected Plugin to include in the planned run.</param>
    /// <returns>A dry-run Plugin request.</returns>
    private static PluginProcessingRunRequest CreateDryRunRequest(string pluginName)
    {
        return new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, @"C:\Games\Skyrim"),
            string.Empty,
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
            PluginProcessingRunRequest,
            IPluginFormIdRecordWriter,
            IProgress<PluginIngestionProgress>?,
            CancellationToken,
            Task<PluginIngestionReport>> response) : IPluginIngestion
    {
        public List<(
            PluginProcessingRunRequest Request,
            IPluginFormIdRecordWriter RecordStore,
            IProgress<PluginIngestionProgress>? Progress,
            CancellationToken CancellationToken)> Calls { get; } = [];

        /// <inheritdoc />
        public Task<PluginIngestionReport> IngestAsync(
            PluginProcessingRunRequest request,
            IPluginFormIdRecordWriter recordStore,
            IProgress<PluginIngestionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((request, recordStore, progress, cancellationToken));
            return response(request, recordStore, progress, cancellationToken);
        }

        /// <inheritdoc />
        public Task<PluginIngestionPlan> PlanAsync(
            PluginProcessingRunRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<PluginIngestionPlan>(
                new InvalidOperationException("Planning was not expected for this Processing Run."));
        }
    }

    /// <summary>
    ///     Plugin Ingestion that only plans: it reports every selected Plugin as one that would be ingested, or raises
    ///     the configured planning failure, and fails loudly if a run reaches its ingestion path.
    /// </summary>
    private sealed class PlanningPluginIngestion : IPluginIngestion
    {
        public List<(PluginProcessingRunRequest Request, CancellationToken CancellationToken)> PlanCalls { get; } =
            [];

        public int IngestCallCount { get; private set; }

        /// <summary>
        ///     The failure planning raises instead of returning a plan, when one is configured.
        /// </summary>
        public Exception? PlanFailure { get; init; }

        /// <inheritdoc />
        public Task<PluginIngestionReport> IngestAsync(
            PluginProcessingRunRequest request,
            IPluginFormIdRecordWriter recordStore,
            IProgress<PluginIngestionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            IngestCallCount++;
            return Task.FromException<PluginIngestionReport>(
                new InvalidOperationException("Plugin Ingestion was not expected for this Processing Run."));
        }

        /// <inheritdoc />
        public Task<PluginIngestionPlan> PlanAsync(
            PluginProcessingRunRequest request,
            CancellationToken cancellationToken = default)
        {
            PlanCalls.Add((request, cancellationToken));
            if (PlanFailure is not null)
            {
                return Task.FromException<PluginIngestionPlan>(PlanFailure);
            }

            return Task.FromResult(new PluginIngestionPlan(
                request,
                request.PluginNames.Select(pluginName => new PlannedPluginIngestion(pluginName))));
        }
    }

    private sealed class ThrowingPluginIngestion(Exception exception) : IPluginIngestion
    {
        public int CallCount { get; private set; }

        /// <inheritdoc />
        public Task<PluginIngestionReport> IngestAsync(
            PluginProcessingRunRequest request,
            IPluginFormIdRecordWriter recordStore,
            IProgress<PluginIngestionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException<PluginIngestionReport>(exception);
        }

        /// <inheritdoc />
        public Task<PluginIngestionPlan> PlanAsync(
            PluginProcessingRunRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException<PluginIngestionPlan>(exception);
        }
    }

    /// <summary>
    ///     Opens Store sessions by running a test-supplied callback on the run's worker, from inside initialization.
    /// </summary>
    /// <remarks>
    ///     Lifecycle tests act from inside the callback (cancel, dispose, start another run) instead of waiting on the
    ///     test thread for the worker to reach initialization, so where the run is when they act is fixed by
    ///     construction rather than by how quickly the thread pool schedules it.
    /// </remarks>
    private sealed class InterceptingRecordStoreSessionOpener : IFormIdRecordStoreSessionOpener
    {
        private int _openCount;

        /// <summary>
        ///     Receives the 1-based open number and returns the session for that run.
        /// </summary>
        public Func<int, IFormIdRecordStoreSession>? OnOpen { get; set; }

        /// <inheritdoc />
        public Task<IFormIdRecordStoreSession> OpenAsync(
            string databasePath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            var onOpen = OnOpen ?? throw new InvalidOperationException("The test did not configure OnOpen.");
            return Task.FromResult(onOpen(Interlocked.Increment(ref _openCount)));
        }
    }

    /// <summary>
    ///     Plugin Ingestion that runs a test-supplied callback when ingestion starts, then reports whether the token it
    ///     was handed has been cancelled by then.
    /// </summary>
    private sealed class CancellationObservingPluginIngestion : IPluginIngestion
    {
        public Action? OnIngestionStarted { get; set; }

        /// <inheritdoc />
        public Task<PluginIngestionReport> IngestAsync(
            PluginProcessingRunRequest request,
            IPluginFormIdRecordWriter recordStore,
            IProgress<PluginIngestionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            OnIngestionStarted?.Invoke();

            // Failing rather than blocking when the token was never cancelled turns a cancellation that missed this
            // interface into a test failure instead of a hang. An ordinary report is not used for that, because the
            // executor's own token check before optimization could still turn it into a cancelled outcome.
            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<PluginIngestionReport>(cancellationToken)
                : Task.FromException<PluginIngestionReport>(
                    new InvalidOperationException("Cancellation did not reach the Plugin Ingestion token."));
        }

        /// <inheritdoc />
        public Task<PluginIngestionPlan> PlanAsync(
            PluginProcessingRunRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<PluginIngestionPlan>(
                new InvalidOperationException("Planning was not expected for this Processing Run."));
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
}
