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
    ///     Verifies that a FormID text-file dry run reports the file's presence and size without opening a FormID
    ///     Record Store and without reading a single row.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DryRunFormIdTextFile_ReportsFilePresenceAndSizeWithoutOpeningRecordStore()
    {
        var formIdListPath = CreateTempFilePath("formids.txt");
        await File.WriteAllTextAsync(
            formIdListPath,
            "Skyrim.esm|000001|Entry",
            TestContext.Current.CancellationToken);
        var ingestion = new PlanningPluginIngestion();
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        using var sut = new ProcessingRunExecutor(ingestion, opener);
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new FormIdTextProcessingRunRequest(
            formIdListPath,
            string.Empty,
            GameRelease.SkyrimSE,
            UpdateMode.Append,
            dryRun: true);

        var outcome = await sut.ExecuteAsync(request, progress);

        Assert.Empty(opener.OpenCalls);
        Assert.Empty(ingestion.PlanCalls);
        Assert.Empty(reports);
        var plan = Assert.IsType<FormIdTextRunPlan>(Assert.IsType<PlannedRunOutcome>(outcome).Plan);
        Assert.Equal(formIdListPath, plan.FormIdListPath);
        Assert.Equal(new FileInfo(formIdListPath).Length, plan.SizeInBytes);
    }

    /// <summary>
    ///     Verifies that a FormID text-file dry run reports absence rather than failing when no file is there.
    /// </summary>
    /// <remarks>
    ///     Telling the user the file is missing is the point of looking it up, so absence is a reportable fact and not
    ///     a failure the run raises.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_DryRunFormIdTextFileThatDoesNotExist_ReportsAbsenceWithoutASize()
    {
        var formIdListPath = CreateTempFilePath("missing-formids.txt");
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        using var sut = new ProcessingRunExecutor(new PlanningPluginIngestion(), opener);
        var request = new FormIdTextProcessingRunRequest(
            formIdListPath,
            string.Empty,
            GameRelease.SkyrimSE,
            UpdateMode.Append,
            dryRun: true);

        var outcome = await sut.ExecuteAsync(request);

        Assert.Empty(opener.OpenCalls);
        var plan = Assert.IsType<FormIdTextRunPlan>(Assert.IsType<PlannedRunOutcome>(outcome).Plan);
        Assert.Equal(formIdListPath, plan.FormIdListPath);
        Assert.Null(plan.SizeInBytes);
    }

    /// <summary>
    ///     Verifies that idle and between-run cancellation never carries into a later Processing Run.
    /// </summary>
    [Fact]
    public async Task Cancel_IdleAndRepeatedBetweenRuns_DoesNotCancelLaterRuns()
    {
        var ingestion = new PlanningPluginIngestion();
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession()));

        sut.Cancel();
        var firstOutcome = await sut.ExecuteAsync(CreateDryRunRequest("First.esp"));

        sut.Cancel();
        sut.Cancel();
        var secondOutcome = await sut.ExecuteAsync(CreateDryRunRequest("Second.esp"));

        Assert.Equal(["First.esp"], AssertPlannedPluginNames(firstOutcome));
        Assert.Equal(["Second.esp"], AssertPlannedPluginNames(secondOutcome));
    }

    /// <summary>
    ///     Verifies a selected-Plugin dry run plans through Plugin Ingestion, carrying the captured selection and the
    ///     run's own cancellation token, without opening a Store or ingesting anything.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DryRunPluginRun_PlansThroughPluginIngestionWithoutOpeningDatabase()
    {
        var databasePath = CreateTempFilePath("dry-run.db");
        var ingestion = new PlanningPluginIngestion();
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        using var sut = new ProcessingRunExecutor(ingestion, opener);
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            databasePath,
            GameRelease.SkyrimSE,
            ["PluginA.esp", "PluginB.esp"],
            UpdateMode.Append,
            dryRun: true);

        var outcome = await sut.ExecuteAsync(request, progress);

        Assert.False(File.Exists(databasePath));
        Assert.Empty(opener.OpenCalls);
        Assert.Equal(0, ingestion.IngestCallCount);
        Assert.Empty(reports);
        var planCall = Assert.Single(ingestion.PlanCalls);
        Assert.Equal(@"C:\Games\Skyrim", planCall.Request.GameDirectory);
        Assert.Equal(GameRelease.SkyrimSE, planCall.Request.GameRelease);
        Assert.Equal(["PluginA.esp", "PluginB.esp"], planCall.Request.PluginNames);
        Assert.Equal(UpdateMode.Append, planCall.Request.UpdateMode);
        Assert.True(planCall.CancellationToken.CanBeCanceled);
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
        using var sut = new ProcessingRunExecutor(ingestion, opener);
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

        var thrown = await Assert.ThrowsAsync<UnresolvableMasterException>(() =>
            sut.ExecuteAsync(CreateDryRunRequest("Patch.esp"), progress));

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
        using var sut = new ProcessingRunExecutor(ingestion, opener);
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            string.Empty,
            GameRelease.SkyrimSE,
            ["PluginA.esp"],
            UpdateMode.Append,
            dryRun: true);

        var outcome = await sut.ExecuteAsync(request, progress);

        Assert.Empty(opener.OpenCalls);
        Assert.Equal(0, ingestion.IngestCallCount);
        Assert.Equal(["PluginA.esp"], AssertPlannedPluginNames(outcome));
    }

    [Fact]
    public async Task ExecuteAsync_FormIdTextRun_ImportsRecordsAndReturnsTheStoreCounts()
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

        var reports = new List<ProcessingRunProgress>();
        var recordStore = new RecordingRecordStoreSession
        {
            TextFileImportResult = new FormIdTextFileImportResult(2, 2)
        };
        var opener = new RecordingRecordStoreSessionOpener(recordStore);
        var sut = new ProcessingRunExecutor(new UnexpectedPluginIngestion(), opener);
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new FormIdTextProcessingRunRequest(
            textFilePath,
            databasePath,
            GameRelease.SkyrimSE,
            UpdateMode.Append);

        var outcome = await sut.ExecuteAsync(request, progress);

        Assert.Equal(databasePath, opener.OpenCalls.Single().DatabasePath);
        Assert.Equal(GameRelease.SkyrimSE, opener.OpenCalls.Single().GameRelease);
        Assert.Equal(textFilePath, recordStore.ImportedTextFilePath);
        Assert.Equal(UpdateMode.Append, recordStore.ImportedTextFileUpdateMode);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        // The Store reports counts and the run restates them; neither writes the sentence the user finally reads.
        Assert.Equal(
            [new ImportedFormIdText(new FormIdTextFileImportResult(2, 2))],
            reports);
        Assert.Equal(
            new FormIdTextFileImportResult(2, 2),
            Assert.IsType<FormIdTextRunOutcome>(outcome).ImportResult);
    }

    /// <summary>
    ///     Verifies that an appending text run restates the Store's counters without naming any Plugin, which is the
    ///     mode that has never shown Plugin-named status updates.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FormIdTextRunInAppendMode_RestatesStoreCountersWithoutNamingAPlugin()
    {
        var reports = await ExecuteTextRunWithStoreProgressAsync(UpdateMode.Append);

        Assert.Equal(
            [
                new ImportingFormIdText(0, 0, 64, null),
                new ImportedFormIdText(new FormIdTextFileImportResult(2, 2))
            ],
            reports);
    }

    /// <summary>
    ///     Verifies that a replacing text run names each Plugin the Store reports seeing for the first time, in the
    ///     order the Store reported them.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FormIdTextRunInReplaceMode_NamesEachPluginTheStoreReportsSeeing()
    {
        var reports = await ExecuteTextRunWithStoreProgressAsync(UpdateMode.ReplacePluginRecords);

        Assert.Equal(
            [
                new ImportingFormIdText(0, 0, 64, null),
                new ImportingFormIdText(1, 16, 64, "PluginA.esp"),
                new ImportingFormIdText(2, 32, 64, "PluginB.esp"),
                new ImportedFormIdText(new FormIdTextFileImportResult(2, 2))
            ],
            reports);
    }

    /// <summary>
    ///     Verifies that a Plugin the Store reports twice is named once: the second report is a record count that
    ///     happens to carry the same name, not a newly seen Plugin.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FormIdTextRunSeeingTheSamePluginTwice_NamesItOnlyOnTheFirstReport()
    {
        var reports = await ExecuteTextRunWithStoreProgressAsync(
            UpdateMode.ReplacePluginRecords,
            [
                new FormIdStoreProgress(0, 0, 64, null),
                new FormIdStoreProgress(1, 16, 64, "PluginA.esp"),
                new FormIdStoreProgress(500, 32, 64, "PluginA.esp")
            ]);

        Assert.Equal(
            [
                new ImportingFormIdText(0, 0, 64, null),
                new ImportingFormIdText(1, 16, 64, "PluginA.esp"),
                new ImportingFormIdText(500, 32, 64, null),
                new ImportedFormIdText(new FormIdTextFileImportResult(2, 2))
            ],
            reports);
    }

    /// <summary>
    ///     Executes one FormID text-file run against a Store that reports a fixed sequence of counters, and collects
    ///     the progress the run restated them as.
    /// </summary>
    /// <param name="updateMode">The update mode the run applies, which decides whether Plugins are named.</param>
    /// <param name="storeProgressReports">
    ///     The counters the Store reports, defaulting to an opening report followed by two newly seen Plugins.
    /// </param>
    /// <returns>The run's own progress reports, in report order.</returns>
    private static async Task<IReadOnlyList<ProcessingRunProgress>> ExecuteTextRunWithStoreProgressAsync(
        UpdateMode updateMode,
        IReadOnlyList<FormIdStoreProgress>? storeProgressReports = null)
    {
        var reports = new List<ProcessingRunProgress>();
        var recordStore = new RecordingRecordStoreSession
        {
            TextFileImportResult = new FormIdTextFileImportResult(2, 2),
            TextFileProgressReports = storeProgressReports ??
            [
                new FormIdStoreProgress(0, 0, 64, null),
                new FormIdStoreProgress(1, 16, 64, "PluginA.esp"),
                new FormIdStoreProgress(2, 32, 64, "PluginB.esp")
            ]
        };
        var opener = new RecordingRecordStoreSessionOpener(recordStore);
        using var sut = new ProcessingRunExecutor(new UnexpectedPluginIngestion(), opener);
        var request = new FormIdTextProcessingRunRequest(
            @"C:\FormIds\formids.txt",
            @"C:\FormIds\formids.db",
            GameRelease.SkyrimSE,
            updateMode);

        await sut.ExecuteAsync(request, new SynchronousProgress<ProcessingRunProgress>(reports.Add));
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
        using var sut = new ProcessingRunExecutor(
            new UnexpectedPluginIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync(CreateTextFileRequest("optimization-failure.txt"), progress));

        Assert.Same(optimizationFailure, exception);
        Assert.Equal(1, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        AssertReportedFailure(reports, optimizationFailure);
        AssertDidNotReportFailure(reports, cleanupFailure);
    }

    /// <summary>
    ///     Verifies that cancellation observed after text import ends the Processing Run as cancelled, without
    ///     optimization and without a completed outcome.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledAfterTextImport_ReturnsCancelledWithoutOptimization()
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
            var reports = new List<ProcessingRunProgress>();
            var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

            var outcome = await sut.ExecuteAsync(CreateTextFileRequest("cancel-after-import.txt"), progress);

            Assert.IsType<CancelledRunOutcome>(outcome);
            Assert.Equal(0, recordStore.OptimizeCallCount);
            Assert.True(recordStore.Disposed);
            AssertNoCancellationReportedOnTheProgressChannel(reports);
        }
    }

    /// <summary>
    ///     Verifies that cancellation accepted after successful optimization still ends the text-file run as cancelled
    ///     rather than as a completed import.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledAfterTextOptimization_ReturnsCancelledRatherThanTheImportResult()
    {
        ProcessingRunExecutor? sut = null;
        var recordStore = new RecordingRecordStoreSession
        {
            OptimizeAction = () => sut!.Cancel()
        };
        sut = new ProcessingRunExecutor(
            new UnexpectedPluginIngestion(),
            new RecordingRecordStoreSessionOpener(recordStore));
        using (sut)
        {
            var reports = new List<ProcessingRunProgress>();
            var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

            var outcome = await sut.ExecuteAsync(CreateTextFileRequest("cancel-after-optimize.txt"), progress);

            Assert.IsType<CancelledRunOutcome>(outcome);
            Assert.Equal(1, recordStore.OptimizeCallCount);
            Assert.True(recordStore.Disposed);
            AssertNoCancellationReportedOnTheProgressChannel(reports);
        }
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
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["First.esp", "Second.esp"],
            UpdateMode.ReplacePluginRecords);

        var outcome = await sut.ExecuteAsync(request, progress);

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
            var reports = new List<ProcessingRunProgress>();
            var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
            var request = new PluginProcessingRunRequest(
                CreateTempDirectory(),
                @"C:\Databases\formids.db",
                GameRelease.SkyrimSE,
                ["Plugin.esp"],
                UpdateMode.Append);

            var outcome = await sut.ExecuteAsync(request, progress);

            Assert.IsType<CancelledRunOutcome>(outcome);
            Assert.Single(ingestion.Calls);
            Assert.Equal(0, recordStore.OptimizeCallCount);
            Assert.True(recordStore.Disposed);
            AssertNoCancellationReportedOnTheProgressChannel(reports);
            AssertNoTerminalReportOnTheProgressChannel(reports);
        }
    }

    /// <summary>
    ///     Verifies that cancellation accepted after successful optimization discards the report, so a cancelled run
    ///     cannot hand its caller warning, failure, or completion facts.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledAfterPluginOptimization_ReturnsCancelledRatherThanTheReport()
    {
        ProcessingRunExecutor? sut = null;
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
            OptimizeAction = () => sut!.Cancel()
        };
        sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        using (sut)
        {
            var reports = new List<ProcessingRunProgress>();
            var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
            var request = new PluginProcessingRunRequest(
                CreateTempDirectory(),
                @"C:\Databases\formids.db",
                GameRelease.SkyrimSE,
                ["Warned.esp", "Skipped.esp", "Failed.esp"],
                UpdateMode.Append);

            var outcome = await sut.ExecuteAsync(request, progress);

            Assert.IsType<CancelledRunOutcome>(outcome);
            Assert.Single(ingestion.Calls);
            Assert.Equal(1, recordStore.OptimizeCallCount);
            Assert.True(recordStore.Disposed);
            AssertNoCancellationReportedOnTheProgressChannel(reports);
            AssertNoTerminalReportOnTheProgressChannel(reports);
        }
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
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
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
        AssertReportedFailure(reports, optimizationFailure);
        AssertNoTerminalReportOnTheProgressChannel(reports);
        // No Plugin from the withheld report reached the channel, and neither did the cleanup failure.
        Assert.DoesNotContain(reports, reported => reported is IngestingPlugin);
        AssertDidNotReportFailure(reports, cleanupFailure);
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
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Warned.esp", "Skipped.esp", "Bad.esp", "Good.esp"],
            UpdateMode.Append);

        var outcome = await sut.ExecuteAsync(request, progress);

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
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
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
        AssertReportedFailure(reports, ingestionFailure);
        AssertNoTerminalReportOnTheProgressChannel(reports);
        AssertDidNotReportFailure(reports, cleanupFailure);
    }

    /// <summary>
    ///     Verifies a cancellation this executor never requested still propagates unchanged, and cannot be replaced by
    ///     Store cleanup failure.
    /// </summary>
    /// <remarks>
    ///     Only the cancellation this executor asked for becomes an outcome, and this one was not: the run's own token
    ///     was never cancelled. Reinterpreting a collaborator's unexplained cancellation as a cancelled run would
    ///     claim the user pressed something they did not.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_CancellationNobodyRequested_PropagatesWithoutBecomingACancelledOutcome()
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
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
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
        AssertNoCancellationReportedOnTheProgressChannel(reports);
        AssertNoTerminalReportOnTheProgressChannel(reports);
        AssertDidNotReportFailure(reports, cleanupFailure);
    }

    /// <summary>
    ///     Verifies active-run cancellation reaches the Plugin Ingestion interface token, prevents Store optimization,
    ///     and ends the run as a cancelled outcome.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledDuringPluginIngestion_StopsBeforeOptimizationAndReturnsCancelled()
    {
        var gameDirectory = CreateTempDirectory();
        var databasePath = Path.Combine(gameDirectory, "cancel.db");
        var ingestion = new BlockingPluginIngestion();
        var recordStore = new RecordingRecordStoreSession();
        using var sut = new ProcessingRunExecutor(
            ingestion,
            new RecordingRecordStoreSessionOpener(recordStore));
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
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

        Assert.IsType<CancelledRunOutcome>(await processingTask);
        Assert.Equal(0, recordStore.OptimizeCallCount);
        Assert.True(recordStore.Disposed);
        AssertNoCancellationReportedOnTheProgressChannel(reports);
    }

    /// <summary>
    ///     Verifies that supersession cancels the older run without disposing its source and keeps the newer run active.
    /// </summary>
    /// <remarks>
    ///     Both runs ending as a cancelled outcome rather than an <see cref="ObjectDisposedException" /> is the whole
    ///     assertion: a source disposed out from under a still-running execution would surface as the latter.
    /// </remarks>
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
        var firstOutcome = await firstTask;

        // The older run finishes after the newer run owns the active slot; cancellation must still target run two.
        sut.Cancel();
        await opener.SecondCancellationObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        var secondOutcome = await secondTask;

        Assert.IsType<CancelledRunOutcome>(firstOutcome);
        Assert.IsType<CancelledRunOutcome>(secondOutcome);
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

        Assert.IsType<CancelledRunOutcome>(await processingTask);
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
        Task<ProcessingRunOutcome>? processingTask = null;

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
        var initializationFailure = new InvalidOperationException("open failed");
        var sut = new ProcessingRunExecutor(
            new UnexpectedPluginIngestion(),
            new ThrowingRecordStoreSessionOpener(initializationFailure));
        var databasePath = Path.Combine(CreateTempDirectory(), "fatal.db");
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            databasePath,
            GameRelease.SkyrimSE,
            ["Plugin.esp"],
            UpdateMode.Append);

        await Assert.ThrowsAnyAsync<Exception>(() => sut.ExecuteAsync(request, progress));

        AssertReportedFailure(reports, initializationFailure);
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
    ///     Asserts that a cancelled Processing Run reported nothing about the cancellation on the progress channel.
    /// </summary>
    /// <param name="reports">The complete progress reported by the executor.</param>
    /// <remarks>
    ///     Issue #60. The progress channel is transient and is cleared as soon as the run ends, so an acknowledgement
    ///     written here is erased before the user can read it. The cancellation acknowledgement is an information
    ///     message rendered from the outcome instead, and the run's progress vocabulary cannot express it at all now —
    ///     which leaves this to check what remains expressible and wrong: a cancelled run reporting itself as failing.
    /// </remarks>
    private static void AssertNoCancellationReportedOnTheProgressChannel(IReadOnlyList<ProcessingRunProgress> reports)
    {
        Assert.DoesNotContain(reports, reported => reported is ProcessingRunFailure);
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
    ///     Asserts that the run restated one failure on the progress channel before rethrowing it.
    /// </summary>
    /// <param name="reports">The complete progress reported by the executor.</param>
    /// <param name="failure">The failure the run was expected to report.</param>
    /// <remarks>
    ///     Matched against the failure's own message rather than a literal, because what is being asserted is
    ///     <em>which</em> failure survived, not how it reads: the run reports the message unprefixed, and the
    ///     "Error during processing" wording belongs to <c>ProcessingRunPresentation</c> and its own suite.
    /// </remarks>
    private static void AssertReportedFailure(IReadOnlyList<ProcessingRunProgress> reports, Exception failure)
    {
        Assert.Contains(reports, reported => reported is ProcessingRunFailure(var message) &&
                                             message == failure.Message);
    }

    /// <summary>
    ///     Asserts that the run did not restate one failure on the progress channel.
    /// </summary>
    /// <param name="reports">The complete progress reported by the executor.</param>
    /// <param name="failure">The failure the run was expected to keep to itself.</param>
    private static void AssertDidNotReportFailure(IReadOnlyList<ProcessingRunProgress> reports, Exception failure)
    {
        Assert.DoesNotContain(reports, reported => reported is ProcessingRunFailure(var message) &&
                                                   message == failure.Message);
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

        /// <inheritdoc />
        public Task<PluginIngestionPlan> PlanAsync(
            SelectedPluginIngestionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<PluginIngestionPlan>(
                new InvalidOperationException("Planning was not expected for this Processing Run."));
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

        /// <inheritdoc />
        public Task<PluginIngestionPlan> PlanAsync(
            SelectedPluginIngestionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
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
        public List<(SelectedPluginIngestionRequest Request, CancellationToken CancellationToken)> PlanCalls { get; } =
            [];

        public int IngestCallCount { get; private set; }

        /// <summary>
        ///     The failure planning raises instead of returning a plan, when one is configured.
        /// </summary>
        public Exception? PlanFailure { get; init; }

        /// <inheritdoc />
        public Task<PluginIngestionReport> IngestAsync(
            SelectedPluginIngestionRequest request,
            IFormIdRecordStoreSession recordStore,
            IProgress<PluginIngestionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            IngestCallCount++;
            return Task.FromException<PluginIngestionReport>(
                new InvalidOperationException("Plugin Ingestion was not expected for this Processing Run."));
        }

        /// <inheritdoc />
        public Task<PluginIngestionPlan> PlanAsync(
            SelectedPluginIngestionRequest request,
            CancellationToken cancellationToken = default)
        {
            PlanCalls.Add((request, cancellationToken));
            if (PlanFailure is not null)
            {
                return Task.FromException<PluginIngestionPlan>(PlanFailure);
            }

            return Task.FromResult(new PluginIngestionPlan(
                request.PluginNames.Select(pluginName => new PlannedPluginIngestion(pluginName))));
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

        /// <inheritdoc />
        public Task<PluginIngestionPlan> PlanAsync(
            SelectedPluginIngestionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException<PluginIngestionPlan>(exception);
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

        /// <inheritdoc />
        public Task<PluginIngestionPlan> PlanAsync(
            SelectedPluginIngestionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<PluginIngestionPlan>(
                new InvalidOperationException("Planning was not expected for this Processing Run."));
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
