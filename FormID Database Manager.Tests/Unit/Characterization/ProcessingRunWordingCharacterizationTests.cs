#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Characterization;

/// <summary>
///     Pins every user-facing string, progress value, and report ordering a selected-Plugin Processing Run and a dry
///     run produce.
/// </summary>
/// <remarks>
///     <para>
///         Issue #63, under parent #61. These are characterization tests: they record what the run says, not what it
///         ought to say. They exist so that splitting the run's reporting out of <see cref="ProcessingRunExecutor" />
///         can be shown to change no wording except the two changes that refactor deliberately makes.
///     </para>
///     <para>
///         A run now says two separate things: transient status while it runs, still reported as
///         <see cref="ProcessingRunEvent" /> through the progress channel, and how it ended, rendered from the returned
///         outcome by <see cref="ProcessingRunPresentation" />. Both halves are asserted for every run — the transient
///         events as a complete ordered sequence by value, and the rendered report across all four of its channels —
///         so a string that moved between them cannot pass unnoticed, which a <c>Contains</c> over individual messages
///         could not do.
///     </para>
///     <para>
///         The test doubles below deliberately duplicate ones held privately by
///         <c>Unit.Services.ProcessingRunExecutorTests</c>. Parent #61 rewrites that suite — its executor tests become
///         outcome tests — and a safety net that borrowed its scaffolding would move along with the code it is meant
///         to hold still. Only genuinely shared infrastructure such as <see cref="SynchronousProgress{T}" /> is reused.
///     </para>
/// </remarks>
public sealed class ProcessingRunWordingCharacterizationTests
{
    private const string GameDirectory = @"C:\Games\Skyrim";
    private const string DatabasePath = @"C:\Databases\formids.db";
    private const string FormIdListPath = @"C:\Imports\formids.txt";

    /// <summary>
    ///     Pins the terminal status of a run in which every selected Plugin became an Ingested Plugin without a
    ///     Processing Warning.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_EveryPluginIngestedWithoutWarnings_ReportsOnlyTheSuccessfulCompletionStatus()
    {
        var report = await ExecutePluginRunAsync(
            ["First.esp", "Second.esp"],
            _ => [new IngestedPlugin("First.esp", 2), new IngestedPlugin("Second.esp", 3)]);

        Assert.Empty(report.Events);
        AssertTerminalReport(report.Rendered, "Processing completed successfully!");
    }

    /// <summary>
    ///     Pins the two transient statuses a run renders from Plugin Ingestion's structured progress, and the progress
    ///     value each one carries.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PluginIngestionProgress_ReportsPreparationAndCurrentPluginStatusesWithValues()
    {
        var report = await ExecutePluginRunAsync(
            ["First.esp", "Second.esp", "Third.esp", "Fourth.esp"],
            request => request.PluginNames.Select(name => new IngestedPlugin(name, 1)),
            progress =>
            {
                progress?.Report(PluginIngestionProgress.PreparingLoadOrder(4));
                progress?.Report(PluginIngestionProgress.IngestingPlugin("Second.esp", 2, 4));
                progress?.Report(PluginIngestionProgress.IngestingPlugin("Fourth.esp", 4, 4));
            });

        Assert.Equal(
            [
                ProcessingRunEvent.Status("Initializing plugin ingestion...", 0),
                ProcessingRunEvent.Status("Ingesting plugin 2 of 4: Second.esp", 50),
                ProcessingRunEvent.Status("Ingesting plugin 4 of 4: Fourth.esp", 100)
            ],
            report.Events);
        AssertTerminalReport(report.Rendered, "Processing completed successfully!");
    }

    /// <summary>
    ///     Pins the warning report and warning completion status of a run whose Ingested Plugins carried recoverable
    ///     record issues, including the singular and plural forms of both the summary and the per-Plugin detail.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PluginRunWithProcessingWarnings_ReportsWarningDetailsThenWarningCompletion()
    {
        var report = await ExecutePluginRunAsync(
            ["Warned.esp", "Noisy.esp", "Quiet.esp"],
            _ =>
            [
                new IngestedPlugin("Warned.esp", 2, new ProcessingWarning(1, ["Recoverable issue"])),
                new IngestedPlugin("Noisy.esp", 3, new ProcessingWarning(3, ["First issue", "Second issue"])),
                new IngestedPlugin("Quiet.esp", 1)
            ]);

        Assert.Empty(report.Events);
        AssertTerminalReport(
            report.Rendered,
            "Processing completed with warnings: 3 ingested, 0 skipped, and 0 failed Plugins.",
            Lines(
                "2 processing warnings.",
                "Warned.esp: 1 recoverable record issue. Recoverable issue",
                "Noisy.esp: 3 recoverable record issues. First issue; Second issue; and 1 more."));
    }

    /// <summary>
    ///     Pins the singular warning summary, which a run with exactly one warning detail renders differently.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SingleProcessingWarning_ReportsTheSingularWarningSummary()
    {
        var report = await ExecutePluginRunAsync(
            ["Warned.esp"],
            _ => [new IngestedPlugin("Warned.esp", 2, new ProcessingWarning(1, ["Recoverable issue"]))]);

        Assert.Empty(report.Events);
        AssertTerminalReport(
            report.Rendered,
            "Processing completed with warnings: 1 ingested, 0 skipped, and 0 failed Plugins.",
            Lines(
                "1 processing warning.",
                "Warned.esp: 1 recoverable record issue. Recoverable issue"));
    }

    /// <summary>
    ///     Pins the detail wording of the one skip reason whose Plugin was absent from the prepared load order.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SkippedPluginNotPresentInLoadOrder_ReportsTheLoadOrderSkipDetail()
    {
        var report = await ExecutePluginRunAsync(
            ["Missing.esp"],
            _ => [new SkippedPlugin("Missing.esp", SkippedPluginReason.NotPresentInLoadOrder)]);

        AssertSingleSkipReport(report, "Missing.esp: Could not find plugin in load order: Missing.esp");
    }

    /// <summary>
    ///     Pins the detail wording of the skip reason that names the Plugin Ingestion-resolved file path.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SkippedPluginFileUnavailable_ReportsTheResolvedPathSkipDetail()
    {
        const string resolvedPluginPath = @"C:\Games\Skyrim\Data\Missing.esp";

        var report = await ExecutePluginRunAsync(
            ["Missing.esp"],
            _ =>
            [
                new SkippedPlugin("Missing.esp", SkippedPluginReason.PluginFileUnavailable, resolvedPluginPath)
            ]);

        AssertSingleSkipReport(report, $"Missing.esp: Could not find plugin file: {resolvedPluginPath}");
    }

    /// <summary>
    ///     Pins the detail wording of the skip reason for a Plugin that stored no FormID records, whose detail repeats
    ///     the Plugin name after the outcome prefix.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SkippedPluginWithZeroFormIdRecords_ReportsTheZeroRecordSkipDetail()
    {
        var report = await ExecutePluginRunAsync(
            ["Empty.esp"],
            _ => [new SkippedPlugin("Empty.esp", SkippedPluginReason.ZeroFormIdRecords)]);

        AssertSingleSkipReport(report, "Empty.esp: Empty.esp produced zero FormID records.");
    }

    /// <summary>
    ///     Pins the failure detail for a Plugin whose read failed before any record could be enumerated.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FailedPluginWhileOpening_ReportsTheOpeningFailureDetail()
    {
        var report = await ExecutePluginRunAsync(
            ["Bad.esp"],
            _ =>
            [
                new FailedPlugin(
                    "Bad.esp",
                    new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header."))
            ]);

        AssertSingleFailureReport(report, "Bad.esp: Error opening Bad.esp: Invalid plugin header.");
    }

    /// <summary>
    ///     Pins the failure detail for a Plugin that opened and then failed during record enumeration.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FailedPluginWhileReadingRecords_ReportsTheEnumerationFailureDetail()
    {
        var report = await ExecutePluginRunAsync(
            ["Bad.esp"],
            _ =>
            [
                new FailedPlugin(
                    "Bad.esp",
                    new PluginReadDiagnostic(PluginReadPhase.ReadingRecords, "Unexpected end of record."))
            ]);

        AssertSingleFailureReport(report, "Bad.esp: Error enumerating records in Bad.esp: Unexpected end of record.");
    }

    /// <summary>
    ///     Pins the bounded diagnostic detail one warned Plugin renders when it carries more issues than the warning
    ///     facts retain, which is a per-Plugin truncation separate from the per-report one below.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WarnedPluginWithMoreIssuesThanRetainedDetails_AppendsTheOmittedIssueCount()
    {
        var report = await ExecutePluginRunAsync(
            ["Warned.esp"],
            _ =>
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
                        ]))
            ]);

        Assert.Empty(report.Events);
        AssertTerminalReport(
            report.Rendered,
            "Processing completed with warnings: 1 ingested, 0 skipped, and 0 failed Plugins.",
            Lines(
                "1 processing warning.",
                "Warned.esp: 7 recoverable record issues. First issue; Second issue; Third issue; " +
                "Fourth issue; Fifth issue; and 2 more."));
    }

    /// <summary>
    ///     Pins the detail wording of a warned Plugin whose warning facts retained no diagnostic detail at all, which
    ///     runs the omitted-count clause straight onto the summary sentence's full stop.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WarnedPluginWithoutDiagnosticDetails_AppendsTheOmittedCountToTheIssueSentence()
    {
        var report = await ExecutePluginRunAsync(
            ["Warned.esp"],
            _ => [new IngestedPlugin("Warned.esp", 2, new ProcessingWarning(3, []))]);

        Assert.Empty(report.Events);
        AssertTerminalReport(
            report.Rendered,
            "Processing completed with warnings: 1 ingested, 0 skipped, and 0 failed Plugins.",
            Lines(
                "1 processing warning.",
                "Warned.esp: 3 recoverable record issues.; and 3 more."));
    }

    /// <summary>
    ///     Pins the report-level truncation of warning details: five details survive and the rest become one trailing
    ///     omitted-count line.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_MoreThanFiveWarningDetails_TruncatesToFiveAndReportsTheOmittedCount()
    {
        var pluginNames = Enumerable.Range(1, 7).Select(index => $"Warned{index}.esp").ToArray();

        var report = await ExecutePluginRunAsync(
            pluginNames,
            request => request.PluginNames.Select(name =>
                new IngestedPlugin(name, 1, new ProcessingWarning(1, [$"{name} detail"]))));

        Assert.Empty(report.Events);
        AssertTerminalReport(
            report.Rendered,
            "Processing completed with warnings: 7 ingested, 0 skipped, and 0 failed Plugins.",
            Lines(
                "7 processing warnings.",
                "Warned1.esp: 1 recoverable record issue. Warned1.esp detail",
                "Warned2.esp: 1 recoverable record issue. Warned2.esp detail",
                "Warned3.esp: 1 recoverable record issue. Warned3.esp detail",
                "Warned4.esp: 1 recoverable record issue. Warned4.esp detail",
                "Warned5.esp: 1 recoverable record issue. Warned5.esp detail",
                "and 2 more."));
    }

    /// <summary>
    ///     Pins the report-level truncation of failure details, which uses the same five-detail bound and the same
    ///     trailing omitted-count line as the warning report.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_MoreThanFiveFailureDetails_TruncatesToFiveAndReportsTheOmittedCount()
    {
        var pluginNames = Enumerable.Range(1, 7).Select(index => $"Bad{index}.esp").ToArray();

        var report = await ExecutePluginRunAsync(
            pluginNames,
            request => request.PluginNames.Select(name => new FailedPlugin(
                name,
                new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header."))));

        Assert.Empty(report.Events);
        AssertTerminalReport(
            report.Rendered,
            "Processing completed with failures: 0 ingested, 0 skipped, and 7 failed Plugins.",
            expectedError: Lines(
                "7 failed plugins.",
                "Bad1.esp: Error opening Bad1.esp: Invalid plugin header.",
                "Bad2.esp: Error opening Bad2.esp: Invalid plugin header.",
                "Bad3.esp: Error opening Bad3.esp: Invalid plugin header.",
                "Bad4.esp: Error opening Bad4.esp: Invalid plugin header.",
                "Bad5.esp: Error opening Bad5.esp: Invalid plugin header.",
                "and 2 more."));
    }

    /// <summary>
    ///     Pins the complete report of a run that produced Ingested, Skipped and Failed Plugins at once: the warning
    ///     message, the failure message, and the failure completion status with all three counts.
    /// </summary>
    [Fact]
    public async Task
        ExecuteAsync_IngestedSkippedAndFailedPlugins_ReportsWarningsThenFailuresThenTheFailureCompletionStatus()
    {
        var report = await ExecutePluginRunAsync(
            ["Warned.esp", "Skipped.esp", "Bad.esp", "Good.esp"],
            _ =>
            [
                new IngestedPlugin("Warned.esp", 2, new ProcessingWarning(1, ["Recoverable issue"])),
                new SkippedPlugin("Skipped.esp", SkippedPluginReason.ZeroFormIdRecords),
                new FailedPlugin(
                    "Bad.esp",
                    new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header.")),
                new IngestedPlugin("Good.esp", 3)
            ]);

        Assert.Empty(report.Events);
        AssertTerminalReport(
            report.Rendered,
            "Processing completed with failures: 2 ingested, 1 skipped, and 1 failed Plugins.",
            Lines(
                "2 processing warnings.",
                "Warned.esp: 1 recoverable record issue. Recoverable issue",
                "Skipped.esp: Skipped.esp produced zero FormID records."),
            Lines(
                "1 failed plugin.",
                "Bad.esp: Error opening Bad.esp: Invalid plugin header."));
    }

    /// <summary>
    ///     Pins the status a run reports for any terminal failure that is not cancellation, before it rethrows.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TerminalFailure_ReportsThePrefixedErrorStatusOnTheProgressChannel()
    {
        using var sut = new ProcessingRunExecutor(
            new UnusedPluginIngestion(),
            new ThrowingRecordStoreSessionOpener(new InvalidOperationException("store unavailable")));
        var events = new List<ProcessingRunEvent>();
        var request = new PluginProcessingRunRequest(
            GameDirectory,
            DatabasePath,
            GameRelease.SkyrimSE,
            ["First.esp"],
            UpdateMode.Append);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync(request, new SynchronousProgress<ProcessingRunEvent>(events.Add)));

        Assert.Equal(
            [ProcessingRunEvent.Status("Error during processing: store unavailable")],
            events);
    }

    /// <summary>
    ///     Pins the acknowledgement a cancelled run renders, the list it belongs in, and the fact that it says nothing
    ///     on the transient progress channel (#60).
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledRun_RendersTheAcknowledgementAsAnInformationMessageOnly()
    {
        ProcessingRunExecutor? sut = null;
        var ingestion = new StubPluginIngestion((request, _) =>
        {
            sut!.Cancel();
            return new PluginIngestionReport(request, [new IngestedPlugin("First.esp", 1)]);
        });
        sut = new ProcessingRunExecutor(ingestion, new StubRecordStoreSessionOpener());
        using (sut)
        {
            var events = new List<ProcessingRunEvent>();
            var request = new PluginProcessingRunRequest(
                GameDirectory,
                DatabasePath,
                GameRelease.SkyrimSE,
                ["First.esp"],
                UpdateMode.Append);

            var outcome = await sut.ExecuteAsync(request, new SynchronousProgress<ProcessingRunEvent>(events.Add));

            Assert.IsType<CancelledRunOutcome>(outcome);
            Assert.Empty(events);
            var rendered = ProcessingRunPresentation.Render(outcome);
            Assert.Equal(ActivityProjection.None, rendered.Activity);
            Assert.Equal(["Processing cancelled by user."], rendered.InformationMessages);
            Assert.Empty(rendered.WarningMessages);
            Assert.Empty(rendered.ErrorMessages);
        }
    }

    /// <summary>
    ///     Pins the dry-run report for a selected-Plugin request: one line per selected Plugin, in selection order.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Parent issue #61 changes this wording deliberately — a dry run is to report would-ingest and would-skip
    ///         per Plugin instead. That has not happened yet, so this test is the record of what will be replaced.
    ///     </para>
    ///     <para>
    ///         The lines are unchanged but the channel is not: a dry run used to write one transient status per Plugin
    ///         and now renders information messages instead. That follows from the plan being the dry run's entire
    ///         output and therefore terminal — the transient channel is handed back as the run ends and would erase it
    ///         (#60) — and from a single activity projection being unable to carry more than one line. It is a real
    ///         departure from "dry runs keep today's behaviour", recorded here rather than hidden.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_PluginDryRun_ReportsOneWouldProcessLinePerSelectedPlugin()
    {
        using var sut = new ProcessingRunExecutor(new UnusedPluginIngestion(), new UnusedRecordStoreSessionOpener());
        var events = new List<ProcessingRunEvent>();
        var request = new PluginProcessingRunRequest(
            GameDirectory,
            DatabasePath,
            GameRelease.SkyrimSE,
            ["First.esp", "Second.esp"],
            UpdateMode.Append,
            dryRun: true);

        var outcome = await sut.ExecuteAsync(request, new SynchronousProgress<ProcessingRunEvent>(events.Add));

        Assert.Empty(events);
        AssertPlanReport(outcome, "Would process First.esp", "Would process Second.esp");
    }

    /// <summary>
    ///     Pins the dry-run report for a FormID text-file request, which names the file rather than its contents.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FormIdTextDryRun_ReportsTheWouldProcessFileLine()
    {
        using var sut = new ProcessingRunExecutor(new UnusedPluginIngestion(), new UnusedRecordStoreSessionOpener());
        var events = new List<ProcessingRunEvent>();
        var request = new FormIdTextProcessingRunRequest(
            FormIdListPath,
            DatabasePath,
            GameRelease.SkyrimSE,
            UpdateMode.Append,
            dryRun: true);

        var outcome = await sut.ExecuteAsync(request, new SynchronousProgress<ProcessingRunEvent>(events.Add));

        Assert.Empty(events);
        AssertPlanReport(outcome, $"Would process FormID list file: {FormIdListPath}");
    }

    /// <summary>
    ///     Pins the validation message a Plugin request raises when it is given no game directory, which is the
    ///     wording the User Workflow shows unwrapped.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_BlankGameDirectory_UsesTheGameDirectoryValidationWording()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new PluginProcessingRunRequest(
                string.Empty,
                DatabasePath,
                GameRelease.SkyrimSE,
                ["First.esp"],
                UpdateMode.Append));

        Assert.Equal("Game directory must be specified when processing plugins", exception.Message);
    }

    /// <summary>
    ///     Pins the validation message a request raises when it is given no Store path.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_BlankDatabasePath_UsesTheDatabasePathValidationWording()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new PluginProcessingRunRequest(
                GameDirectory,
                string.Empty,
                GameRelease.SkyrimSE,
                ["First.esp"],
                UpdateMode.Append));

        Assert.Equal("Database path must be specified", exception.Message);
    }

    /// <summary>
    ///     Pins the validation message a FormID text request raises when it names no text file.
    /// </summary>
    [Fact]
    public void FormIdTextProcessingRunRequest_BlankFormIdListPath_UsesTheTextFileValidationWording()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new FormIdTextProcessingRunRequest(
                string.Empty,
                DatabasePath,
                GameRelease.SkyrimSE,
                UpdateMode.Append));

        Assert.Equal("FormID text file must be specified", exception.Message);
    }

    /// <summary>
    ///     Asserts the complete rendered report of one run across all four of its channels.
    /// </summary>
    /// <param name="rendered">The report rendered from how the run ended.</param>
    /// <param name="expectedStatus">The exact completion status, which always carries a full progress value.</param>
    /// <param name="expectedWarning">The exact warning message, or null when the run rendered none.</param>
    /// <param name="expectedError">The exact failure message, or null when the run rendered none.</param>
    /// <remarks>
    ///     The information messages are asserted empty on every completed run, because that is the list a cancelled or
    ///     dry run uses: a completed run leaving something there would be wording that moved channels.
    /// </remarks>
    private static void AssertTerminalReport(
        RenderedRunReport rendered,
        string expectedStatus,
        string? expectedWarning = null,
        string? expectedError = null)
    {
        string[] expectedWarnings = expectedWarning is null ? [] : [expectedWarning];
        string[] expectedErrors = expectedError is null ? [] : [expectedError];

        Assert.Equal(new ActivityProjection(true, expectedStatus, 100), rendered.Activity);
        Assert.Equal(expectedWarnings, rendered.WarningMessages);
        Assert.Equal(expectedErrors, rendered.ErrorMessages);
        Assert.Empty(rendered.InformationMessages);
    }

    /// <summary>
    ///     Asserts the complete rendered report of one dry run.
    /// </summary>
    /// <param name="outcome">The outcome the dry run returned.</param>
    /// <param name="expectedLines">The exact planned-work lines, in order.</param>
    private static void AssertPlanReport(ProcessingRunOutcome outcome, params string[] expectedLines)
    {
        Assert.IsType<PlannedRunOutcome>(outcome);

        var rendered = ProcessingRunPresentation.Render(outcome);
        Assert.Equal(ActivityProjection.None, rendered.Activity);
        Assert.Equal(expectedLines, rendered.InformationMessages);
        Assert.Empty(rendered.WarningMessages);
        Assert.Empty(rendered.ErrorMessages);
    }

    /// <summary>
    ///     Asserts the complete report of a run whose single selected Plugin was skipped for one reason.
    /// </summary>
    /// <param name="report">The complete report of the run.</param>
    /// <param name="expectedDetail">The exact skip detail line expected under the warning summary.</param>
    private static void AssertSingleSkipReport(RunReport report, string expectedDetail)
    {
        Assert.Empty(report.Events);
        AssertTerminalReport(
            report.Rendered,
            "Processing completed with warnings: 0 ingested, 1 skipped, and 0 failed Plugins.",
            Lines("1 processing warning.", expectedDetail));
    }

    /// <summary>
    ///     Asserts the complete report of a run whose single selected Plugin failed to be read.
    /// </summary>
    /// <param name="report">The complete report of the run.</param>
    /// <param name="expectedDetail">The exact failure detail line expected under the failure summary.</param>
    private static void AssertSingleFailureReport(RunReport report, string expectedDetail)
    {
        Assert.Empty(report.Events);
        AssertTerminalReport(
            report.Rendered,
            "Processing completed with failures: 0 ingested, 0 skipped, and 1 failed Plugins.",
            expectedError: Lines("1 failed plugin.", expectedDetail));
    }

    /// <summary>
    ///     Joins expected report lines the way a Processing Run joins them.
    /// </summary>
    /// <param name="lines">The expected lines, summary first.</param>
    /// <returns>The expected multi-line report text.</returns>
    /// <remarks>
    ///     The separator is <see cref="Environment.NewLine" /> rather than a literal, because the run uses the platform
    ///     separator: pinning a literal here would assert the platform, not the wording.
    /// </remarks>
    private static string Lines(params string[] lines)
    {
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    ///     Executes one selected-Plugin Processing Run against a Plugin Ingestion stub that returns the supplied
    ///     outcomes, and collects everything the run said.
    /// </summary>
    /// <param name="pluginNames">The selected Plugin names, in selection order.</param>
    /// <param name="outcomes">Builds one outcome per selected Plugin, in the same order.</param>
    /// <param name="reportProgress">Optional structured Plugin Ingestion progress to report before returning.</param>
    /// <returns>The transient events in report order, and the report rendered from how the run ended.</returns>
    private static async Task<RunReport> ExecutePluginRunAsync(
        IReadOnlyList<string> pluginNames,
        Func<SelectedPluginIngestionRequest, IEnumerable<PluginIngestionOutcome>> outcomes,
        Action<IProgress<PluginIngestionProgress>?>? reportProgress = null)
    {
        var events = new List<ProcessingRunEvent>();
        var ingestion = new StubPluginIngestion((request, progress) =>
        {
            reportProgress?.Invoke(progress);
            return new PluginIngestionReport(request, outcomes(request));
        });
        using var sut = new ProcessingRunExecutor(ingestion, new StubRecordStoreSessionOpener());
        var request = new PluginProcessingRunRequest(
            GameDirectory,
            DatabasePath,
            GameRelease.SkyrimSE,
            pluginNames,
            UpdateMode.Append);

        var outcome = await sut.ExecuteAsync(request, new SynchronousProgress<ProcessingRunEvent>(events.Add));

        return new RunReport(events, ProcessingRunPresentation.Render(outcome));
    }

    /// <summary>
    ///     Everything one Processing Run said: the transient events it reported while running, and the report rendered
    ///     from how it ended.
    /// </summary>
    /// <param name="Events">The reported run events, in report order.</param>
    /// <param name="Rendered">The report rendered from the run's outcome.</param>
    private readonly record struct RunReport(
        IReadOnlyList<ProcessingRunEvent> Events,
        RenderedRunReport Rendered);

    /// <summary>
    ///     Plugin Ingestion that reports the supplied structured progress and returns the supplied report, so the only
    ///     wording a run produces is its own.
    /// </summary>
    private sealed class StubPluginIngestion(
        Func<SelectedPluginIngestionRequest, IProgress<PluginIngestionProgress>?, PluginIngestionReport> respond)
        : IPluginIngestion
    {
        /// <inheritdoc />
        public Task<PluginIngestionReport> IngestAsync(
            SelectedPluginIngestionRequest request,
            IFormIdRecordStoreSession recordStore,
            IProgress<PluginIngestionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(respond(request, progress));
        }
    }

    /// <summary>
    ///     Plugin Ingestion that fails loudly if a run reaches it, used by the runs that must never get that far.
    /// </summary>
    private sealed class UnusedPluginIngestion : IPluginIngestion
    {
        /// <inheritdoc />
        public Task<PluginIngestionReport> IngestAsync(
            SelectedPluginIngestionRequest request,
            IFormIdRecordStoreSession recordStore,
            IProgress<PluginIngestionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<PluginIngestionReport>(
                new InvalidOperationException("Plugin Ingestion was not expected for this Processing Run."));
        }
    }

    /// <summary>
    ///     Opens the silent Store session every non-dry selected-Plugin run in this suite uses.
    /// </summary>
    private sealed class StubRecordStoreSessionOpener : IFormIdRecordStoreSessionOpener
    {
        /// <inheritdoc />
        public Task<IFormIdRecordStoreSession> OpenAsync(
            string databasePath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IFormIdRecordStoreSession>(new StubRecordStoreSession());
        }
    }

    /// <summary>
    ///     A Store opener that fails loudly, so a dry run that opened a Store could not pass as one that did not.
    /// </summary>
    private sealed class UnusedRecordStoreSessionOpener : IFormIdRecordStoreSessionOpener
    {
        /// <inheritdoc />
        public Task<IFormIdRecordStoreSession> OpenAsync(
            string databasePath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IFormIdRecordStoreSession>(
                new InvalidOperationException("A dry run must not open a FormID Record Store."));
        }
    }

    /// <summary>
    ///     A Store opener that fails the run, standing in for any terminal failure that is not cancellation.
    /// </summary>
    private sealed class ThrowingRecordStoreSessionOpener(Exception failure) : IFormIdRecordStoreSessionOpener
    {
        /// <inheritdoc />
        public Task<IFormIdRecordStoreSession> OpenAsync(
            string databasePath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IFormIdRecordStoreSession>(failure);
        }
    }

    /// <summary>
    ///     A Store session that succeeds silently, so the only strings a run reports are its own.
    /// </summary>
    private sealed class StubRecordStoreSession : IFormIdRecordStoreSession
    {
        /// <inheritdoc />
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
            return Task.FromResult(new FormIdTextFileImportResult(0, 0));
        }

        /// <inheritdoc />
        public Task OptimizeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
