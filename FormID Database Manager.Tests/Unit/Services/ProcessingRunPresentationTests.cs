#nullable enable

using System;
using System.Globalization;
using System.Linq;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers every word a Processing Run shows the user, and the arithmetic and guards behind them.
/// </summary>
/// <remarks>
///     <para>
///         Issue #68, under parent #61. This is the permanent home of the run's wording: the characterization suite
///         that pinned these strings drove whole runs through the executor to reach them, which made every wording
///         assertion depend on executor behaviour that has nothing to do with the words. Every case here constructs
///         its outcome or its progress value directly, so this suite cannot fail for the executor's reasons and the
///         executor's suite cannot fail for a wording change.
///     </para>
///     <para>
///         Both halves of what a run says are covered: the transient progress it reports while running, and the report
///         rendered from how it ended, across all four of that report's channels. Asserting the untouched channels
///         empty is deliberate — which list a fact lands in is behaviour, so a message that also appeared elsewhere
///         must not pass.
///     </para>
/// </remarks>
public sealed class ProcessingRunPresentationTests
{
    private const string GameDirectory = @"C:\Games\Skyrim";

    /// <summary>
    ///     Verifies the status a run shows while load order is being prepared, before any Plugin has been read.
    /// </summary>
    [Fact]
    public void Render_PreparingLoadOrder_ReportsThePreparationStatusAtZeroPercent()
    {
        var rendered = ProcessingRunPresentation.Render(new PreparingLoadOrder(4));

        Assert.Equal(new RenderedRunProgress("Initializing plugin ingestion...", 0), rendered);
    }

    /// <summary>
    ///     Verifies the status naming the Plugin being ingested and its position within the whole selection.
    /// </summary>
    [Fact]
    public void Render_IngestingPlugin_NamesThePluginAndItsPositionWithinTheSelection()
    {
        var rendered = ProcessingRunPresentation.Render(new IngestingPlugin("Second.esp", 2, 4));

        Assert.Equal(new RenderedRunProgress("Ingesting plugin 2 of 4: Second.esp", 50), rendered);
    }

    /// <summary>
    ///     Verifies the percentage is computed from the reported byte counts rather than the record count.
    /// </summary>
    [Fact]
    public void Render_FormIdTextImportInFlight_ComputesThePercentageFromTheByteCounts()
    {
        var rendered = ProcessingRunPresentation.Render(new ImportingFormIdText(1_000, 16, 64));

        Assert.Equal(25, rendered.Value);
    }

    /// <summary>
    ///     Verifies that a report of no total bytes yields a zero percentage rather than a division by zero.
    /// </summary>
    [Fact]
    public void Render_FormIdTextImportWithNoTotalBytes_ReportsZeroPercentInsteadOfNaN()
    {
        var rendered = UnderInvariantCulture(() =>
            ProcessingRunPresentation.Render(new ImportingFormIdText(500, 0, 0)));

        Assert.Equal(new RenderedRunProgress("Processing: 0.0% (500 records)", 0), rendered);
        Assert.DoesNotContain("NaN", rendered.Status, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies the status that opens an import, which the run states as its own case rather than as counters.
    /// </summary>
    [Fact]
    public void Render_OpeningFormIdText_ReportsTheStartingStatusAtZeroPercent()
    {
        var rendered = ProcessingRunPresentation.Render(new OpeningFormIdText());

        Assert.Equal(new RenderedRunProgress("Starting processing...", 0), rendered);
    }

    /// <summary>
    ///     Verifies that naming a Plugin leaves the percentage already on screen alone.
    /// </summary>
    /// <remarks>
    ///     Reaching a Plugin says nothing new about how far through the file the import is, and the User Workflow reads
    ///     a null value as "keep the value you have".
    /// </remarks>
    [Fact]
    public void Render_ImportingFormIdTextPlugin_NamesThePluginWithNoPercentage()
    {
        var rendered = ProcessingRunPresentation.Render(new ImportingFormIdTextPlugin("PluginA.esp"));

        Assert.Equal(new RenderedRunProgress("Processing plugin: PluginA.esp", null), rendered);
    }

    /// <summary>
    ///     Verifies the status a finished import reports, which restates the counts FormID Text Ingestion read.
    /// </summary>
    [Fact]
    public void Render_ImportedFormIdText_ReportsThePluginAndRecordCountsAtFullProgress()
    {
        var rendered = UnderInvariantCulture(() => ProcessingRunPresentation.Render(
            new ImportedFormIdText(new FormIdTextFileImportResult(2, 2_048))));

        Assert.Equal(
            new RenderedRunProgress("Completed processing 2 plugins (2,048 total records)", 100),
            rendered);
    }

    /// <summary>
    ///     Pins the status an admitted press shows the moment it becomes a run, before the run has reported anything of
    ///     its own, and the zero percentage that resets the bar for it.
    /// </summary>
    /// <remarks>
    ///     Issue #103, under parent #102. A characterization of the sentence the User Workflow used to write itself,
    ///     pinned byte-for-byte before it moved here.
    /// </remarks>
    [Fact]
    public void Render_ProcessingRunStarting_ReportsTheInitializingStatusAtZeroPercent()
    {
        var rendered = ProcessingRunPresentation.Render(new ProcessingRunStarting());

        Assert.Equal(new RenderedRunProgress("Initializing...", 0), rendered);
    }

    /// <summary>
    ///     Pins the status a press shows when it asks the active run to cancel, and that it implies no new percentage,
    ///     so the bar stays where the run left it.
    /// </summary>
    /// <remarks>
    ///     Issue #103, under parent #102. Like the starting status this is a characterization of a sentence the User
    ///     Workflow used to write itself. It is what the run is doing now rather than how it ended, so it is transient
    ///     progress, not the cancellation acknowledgement a cancelled outcome renders (#60).
    /// </remarks>
    [Fact]
    public void Render_ProcessingRunCancellationRequested_ReportsTheCancellingStatusWithoutAPercentage()
    {
        var rendered = ProcessingRunPresentation.Render(new ProcessingRunCancellationRequested());

        Assert.Equal(new RenderedRunProgress("Cancelling...", null), rendered);
    }

    /// <summary>
    ///     Verifies the Plugin-ingestion percentage is the one-based position as a fraction of the whole selection.
    /// </summary>
    /// <param name="position">The one-based position of the Plugin being ingested.</param>
    /// <param name="totalPluginCount">The number of selected Plugins.</param>
    /// <param name="expectedValue">The expected percentage.</param>
    [Theory]
    [InlineData(1, 4, 25d)]
    [InlineData(2, 4, 50d)]
    [InlineData(4, 4, 100d)]
    public void Render_IngestingPlugin_ComputesThePercentageFromThePositionWithinTheSelection(
        int position,
        int totalPluginCount,
        double expectedValue)
    {
        var rendered = ProcessingRunPresentation.Render(
            new IngestingPlugin("Plugin.esp", position, totalPluginCount));

        Assert.Equal(expectedValue, rendered.Value);
    }

    /// <summary>
    ///     Verifies that the record count is grouped with the ambient culture's separator, as every other count the
    ///     app reports is.
    /// </summary>
    /// <remarks>
    ///     Rendered under the invariant culture for the reason given on <see cref="UnderInvariantCulture{TRendered}" />.
    /// </remarks>
    [Fact]
    public void Render_FormIdTextImportInFlight_GroupsTheRecordCount()
    {
        var rendered = UnderInvariantCulture(() =>
            ProcessingRunPresentation.Render(new ImportingFormIdText(2_000, 32, 64)));

        Assert.Equal("Processing: 50.0% (2,000 records)", rendered.Status);
    }

    /// <summary>
    ///     Verifies that the status rounds the percentage to one decimal place while the progress bar keeps the exact
    ///     ratio, so a long import's bar does not advance in visible steps of a tenth of a percent.
    /// </summary>
    [Fact]
    public void Render_FormIdTextImportAtAnUnroundPercentage_RoundsTheStatusButNotTheValue()
    {
        var rendered = UnderInvariantCulture(() =>
            ProcessingRunPresentation.Render(new ImportingFormIdText(2_000, 64_512, 65_536)));

        Assert.Equal("Processing: 98.4% (2,000 records)", rendered.Status);
        Assert.Equal(98.4375, rendered.Value);
    }

    /// <summary>
    ///     Verifies that a null progress report is refused rather than rendered as blank status.
    /// </summary>
    /// <remarks>
    ///     There is no companion test for an unrenderable progress case: the union's constructor is
    ///     <c>private protected</c>, so no assembly but Core can add one, and the renderer's own
    ///     <see cref="ArgumentOutOfRangeException" /> guards only a case added there without a rendering.
    /// </remarks>
    [Fact]
    public void Render_NullProgress_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ProcessingRunPresentation.Render((ProcessingRunProgress)null!));
    }

    /// <summary>
    ///     Verifies the completion line of a run in which every selected Plugin became an Ingested Plugin without a
    ///     Processing Warning.
    /// </summary>
    [Fact]
    public void Render_EveryPluginIngestedWithoutWarnings_ReportsOnlyTheSuccessfulCompletionLine()
    {
        var rendered = RenderPluginRun(
            new IngestedPlugin("First.esp", 2),
            new IngestedPlugin("Second.esp", 3));

        AssertTerminalReport(rendered, "Processing completed successfully!");
    }

    /// <summary>
    ///     Verifies the warning report and warning completion line of a run whose Ingested Plugins carried
    ///     recoverable record issues, including the singular and plural forms of both the summary and the per-Plugin
    ///     detail.
    /// </summary>
    [Fact]
    public void Render_PluginRunWithProcessingWarnings_ReportsWarningDetailsThenWarningCompletion()
    {
        var rendered = RenderPluginRun(
            new IngestedPlugin("Warned.esp", 2, new ProcessingWarning(1, ["Recoverable issue"])),
            new IngestedPlugin("Noisy.esp", 3, new ProcessingWarning(3, ["First issue", "Second issue"])),
            new IngestedPlugin("Quiet.esp", 1));

        AssertTerminalReport(
            rendered,
            "Processing completed with warnings: 3 ingested, 0 skipped, and 0 failed Plugins.",
            Lines(
                "2 processing warnings.",
                "Warned.esp: 1 recoverable record issue. Recoverable issue",
                "Noisy.esp: 3 recoverable record issues. First issue; Second issue; and 1 more."));
    }

    /// <summary>
    ///     Verifies the singular warning summary, which a run with exactly one warning detail renders differently.
    /// </summary>
    [Fact]
    public void Render_SingleProcessingWarning_ReportsTheSingularWarningSummary()
    {
        var rendered = RenderPluginRun(
            new IngestedPlugin("Warned.esp", 2, new ProcessingWarning(1, ["Recoverable issue"])));

        AssertTerminalReport(
            rendered,
            "Processing completed with warnings: 1 ingested, 0 skipped, and 0 failed Plugins.",
            Lines(
                "1 processing warning.",
                "Warned.esp: 1 recoverable record issue. Recoverable issue"));
    }

    /// <summary>
    ///     Verifies the detail wording of the skip reason whose Plugin was absent from the prepared load order.
    /// </summary>
    [Fact]
    public void Render_SkippedPluginNotPresentInLoadOrder_ReportsTheLoadOrderSkipDetail()
    {
        var rendered = RenderPluginRun(new SkippedPlugin("Missing.esp", SkippedPluginReason.NotPresentInLoadOrder));

        AssertSingleSkipReport(rendered, "Missing.esp: Could not find plugin in load order: Missing.esp");
    }

    /// <summary>
    ///     Verifies the detail wording of the skip reason that names the Plugin Ingestion-resolved file path.
    /// </summary>
    [Fact]
    public void Render_SkippedPluginFileUnavailable_ReportsTheResolvedPathSkipDetail()
    {
        const string resolvedPluginPath = @"C:\Games\Skyrim\Data\Missing.esp";

        var rendered = RenderPluginRun(
            new SkippedPlugin("Missing.esp", SkippedPluginReason.PluginFileUnavailable, resolvedPluginPath));

        AssertSingleSkipReport(rendered, $"Missing.esp: Could not find plugin file: {resolvedPluginPath}");
    }

    /// <summary>
    ///     Verifies the detail wording of the skip reason for a Plugin that stored no FormID records, whose detail
    ///     repeats the Plugin name after the outcome prefix.
    /// </summary>
    [Fact]
    public void Render_SkippedPluginWithZeroFormIdRecords_ReportsTheZeroRecordSkipDetail()
    {
        var rendered = RenderPluginRun(new SkippedPlugin("Empty.esp", SkippedPluginReason.ZeroFormIdRecords));

        AssertSingleSkipReport(rendered, "Empty.esp: Empty.esp produced zero FormID records.");
    }

    /// <summary>
    ///     Verifies the failure detail for a Plugin whose read failed before any record could be enumerated.
    /// </summary>
    [Fact]
    public void Render_FailedPluginWhileOpening_ReportsTheOpeningFailureDetail()
    {
        var rendered = RenderPluginRun(new FailedPlugin(
            "Bad.esp",
            new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header.")));

        AssertSingleFailureReport(rendered, "Bad.esp: Error opening Bad.esp: Invalid plugin header.");
    }

    /// <summary>
    ///     Verifies the failure detail for a Plugin that opened and then failed during record enumeration.
    /// </summary>
    [Fact]
    public void Render_FailedPluginWhileReadingRecords_ReportsTheEnumerationFailureDetail()
    {
        var rendered = RenderPluginRun(new FailedPlugin(
            "Bad.esp",
            new PluginReadDiagnostic(PluginReadPhase.ReadingRecords, "Unexpected end of record.")));

        AssertSingleFailureReport(rendered, "Bad.esp: Error enumerating records in Bad.esp: Unexpected end of record.");
    }

    /// <summary>
    ///     Verifies the bounded diagnostic detail one warned Plugin renders when it carries more issues than the
    ///     warning facts retain, which is a per-Plugin truncation separate from the per-report one below.
    /// </summary>
    [Fact]
    public void Render_WarnedPluginWithMoreIssuesThanRetainedDetails_AppendsTheOmittedIssueCount()
    {
        var rendered = RenderPluginRun(new IngestedPlugin(
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
                ])));

        AssertTerminalReport(
            rendered,
            "Processing completed with warnings: 1 ingested, 0 skipped, and 0 failed Plugins.",
            Lines(
                "1 processing warning.",
                "Warned.esp: 7 recoverable record issues. First issue; Second issue; Third issue; " +
                "Fourth issue; Fifth issue; and 2 more."));
    }

    /// <summary>
    ///     Verifies the detail wording of a warned Plugin whose warning facts retained no diagnostic detail at all,
    ///     which runs the omitted-count clause straight onto the summary sentence's full stop.
    /// </summary>
    [Fact]
    public void Render_WarnedPluginWithoutDiagnosticDetails_AppendsTheOmittedCountToTheIssueSentence()
    {
        var rendered = RenderPluginRun(new IngestedPlugin("Warned.esp", 2, new ProcessingWarning(3, [])));

        AssertTerminalReport(
            rendered,
            "Processing completed with warnings: 1 ingested, 0 skipped, and 0 failed Plugins.",
            Lines(
                "1 processing warning.",
                "Warned.esp: 3 recoverable record issues.; and 3 more."));
    }

    /// <summary>
    ///     Verifies the report-level truncation of warning details: five details survive and the rest become one
    ///     trailing omitted-count line.
    /// </summary>
    [Fact]
    public void Render_MoreThanFiveWarningDetails_TruncatesToFiveAndReportsTheOmittedCount()
    {
        var rendered = RenderPluginRun(Enumerable
            .Range(1, 7)
            .Select(position => new IngestedPlugin(
                $"Warned{position}.esp",
                1,
                new ProcessingWarning(1, [$"Warned{position}.esp detail"])))
            .ToArray<PluginIngestionOutcome>());

        AssertTerminalReport(
            rendered,
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
    ///     Verifies the report-level truncation of failure details, which uses the same five-detail bound and the same
    ///     trailing omitted-count line as the warning report.
    /// </summary>
    [Fact]
    public void Render_MoreThanFiveFailureDetails_TruncatesToFiveAndReportsTheOmittedCount()
    {
        var rendered = RenderPluginRun(Enumerable
            .Range(1, 7)
            .Select(position => new FailedPlugin(
                $"Bad{position}.esp",
                new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header.")))
            .ToArray<PluginIngestionOutcome>());

        AssertTerminalReport(
            rendered,
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
    ///     Verifies the complete report of a run that produced Ingested, Skipped and Failed Plugins at once: the
    ///     warning message, the failure message, and the failure completion line with all three counts.
    /// </summary>
    /// <remarks>
    ///     Failures outrank warnings in the completion line, which is only observable when a run produced both.
    /// </remarks>
    [Fact]
    public void Render_IngestedSkippedAndFailedPlugins_ReportsWarningsFailuresAndTheFailureCompletionLine()
    {
        var rendered = RenderPluginRun(
            new IngestedPlugin("Warned.esp", 2, new ProcessingWarning(1, ["Recoverable issue"])),
            new SkippedPlugin("Skipped.esp", SkippedPluginReason.ZeroFormIdRecords),
            new FailedPlugin(
                "Bad.esp",
                new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header.")),
            new IngestedPlugin("Good.esp", 3));

        AssertTerminalReport(
            rendered,
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
    ///     Verifies the completion line of a FormID text-file run, which reports the counts FormID Text Ingestion read.
    /// </summary>
    /// <remarks>
    ///     This is one of the two strings parent #61 changed on purpose: a text run used to end with a flat
    ///     "Processing completed successfully!" that discarded the counts a selected-Plugin run had always reported.
    /// </remarks>
    [Fact]
    public void Render_FormIdTextRunOutcome_ReportsThePluginAndRecordCountsInTheCompletionLine()
    {
        var rendered = UnderInvariantCulture(() => ProcessingRunPresentation.Render(
            new FormIdTextRunOutcome(new FormIdTextFileImportResult(2, 2_048))));

        AssertTerminalReport(rendered, "Processing completed successfully: 2 Plugins and 2,048 records.");
    }

    /// <summary>
    ///     Verifies the acknowledgement a cancelled run leaves behind, and the list it belongs in.
    /// </summary>
    /// <remarks>
    ///     Issue #60. The acknowledgement is terminal and stays off the transient channel, which is handed back as the
    ///     run ends and would erase it. "Cancelling..." — what the run is doing rather than how it ended — is rendered
    ///     from the cancellation-requested fact instead, as transient progress.
    /// </remarks>
    [Fact]
    public void Render_CancelledRun_RendersTheAcknowledgementAsAnInformationMessageOnly()
    {
        var rendered = ProcessingRunPresentation.Render(new CancelledRunOutcome());

        Assert.Equal(["Processing cancelled by user."], rendered.InformationMessages);
        Assert.Empty(rendered.WarningMessages);
        Assert.Empty(rendered.ErrorMessages);
    }

    /// <summary>
    ///     Verifies a dry run's plan reaches the information messages as one summarized message, says nothing on the
    ///     transient channel, and names what would happen to every selected Plugin in selection order.
    /// </summary>
    /// <remarks>
    ///     The plan is the dry run's entire output and is therefore terminal: the transient channel is handed back as
    ///     the run ends and would erase it (#60). It is a single message because the ViewModel's message lists are
    ///     bounded and evict the oldest entry, so a plan spread over one message per Plugin would silently lose its
    ///     head once the selection outgrew that bound.
    /// </remarks>
    [Fact]
    public void Render_PluginPlan_ReportsTheWholePlanAsOneSummarizedInformationMessage()
    {
        const string resolvedPluginPath = @"C:\Games\Skyrim\Data\Unavailable.esp";

        var rendered = ProcessingRunPresentation.Render(new PlannedRunOutcome(new PluginRunPlan(
            new PluginIngestionPlan(
                new PluginProcessingRunRequest(
                    PluginListSource.Create(GameRelease.SkyrimSE, @"C:\Games\Skyrim"),
                    string.Empty,
                    ["First.esp", "Absent.esp", "Unavailable.esp", "Broken.esp"], UpdateMode.Append, dryRun: true),
                [
                new PlannedPluginIngestion("First.esp"),
                new PlannedPluginSkip("Absent.esp", PlannedSkipReason.NotPresentInLoadOrder),
                new PlannedPluginSkip(
                    "Unavailable.esp",
                    PlannedSkipReason.PluginFileUnavailable,
                    resolvedPluginPath),
                new PlannedPluginFailure(
                    "Broken.esp",
                    new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Unreadable header."))
            ]))));

        Assert.Equal(
            [
                Lines(
                    "Dry run: 1 would be ingested, 2 would be skipped, and 1 would fail.",
                    "Would ingest First.esp",
                    "Would skip Absent.esp: not present in the load order",
                    $"Would skip Unavailable.esp: could not find plugin file: {resolvedPluginPath}",
                    "Would fail to open Broken.esp: Unreadable header.")
            ],
            rendered.InformationMessages);
        Assert.Empty(rendered.WarningMessages);
        Assert.Empty(rendered.ErrorMessages);
    }

    /// <summary>
    ///     Verifies that a plan longer than the ViewModel's bounded message list still arrives whole.
    /// </summary>
    /// <remarks>
    ///     <see cref="MainWindowViewModel.AddMessage" /> keeps ten messages and drops the oldest, so a plan
    ///     of one message per Plugin would lose its first entries — the ones a user reads first. This asserts the
    ///     shape that makes that impossible rather than the wording, which the tests above pin.
    /// </remarks>
    [Fact]
    public void Render_PluginPlanLongerThanTheMessageBound_StaysOneMessage()
    {
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, @"C:\Games\Skyrim"),
            string.Empty,
            Enumerable.Range(1, 40).Select(position => $"Plugin{position}.esp"), UpdateMode.Append, dryRun: true);
        var plan = new PluginIngestionPlan(request, Enumerable
            .Range(1, 40)
            .Select(position => new PlannedPluginIngestion($"Plugin{position}.esp")));

        var rendered = ProcessingRunPresentation.Render(new PlannedRunOutcome(new PluginRunPlan(plan)));

        var plannedWork = Assert.Single(rendered.InformationMessages);
        Assert.StartsWith(
            "Dry run: 40 would be ingested, 0 would be skipped, and 0 would fail.",
            plannedWork,
            StringComparison.Ordinal);
        Assert.Contains("Would ingest Plugin1.esp", plannedWork, StringComparison.Ordinal);
        Assert.Contains("Would ingest Plugin40.esp", plannedWork, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies that a FormID text-file plan groups the file size the way every other count the app reports is
    ///     grouped, under the invariant culture for the reason given above.
    /// </summary>
    [Fact]
    public void Render_FormIdTextPlan_GroupsTheFileSizeInBytes()
    {
        var rendered = UnderInvariantCulture(() => ProcessingRunPresentation.Render(
            new PlannedRunOutcome(new FormIdTextRunPlan(@"C:\Imports\formids.txt", 1_234_567))));

        Assert.Equal(
            [@"Would import FormID list file: C:\Imports\formids.txt (1,234,567 bytes)"],
            rendered.InformationMessages);
        Assert.Empty(rendered.WarningMessages);
        Assert.Empty(rendered.ErrorMessages);
    }

    /// <summary>
    ///     Verifies that an empty file is reported as present at zero bytes rather than as absent.
    /// </summary>
    /// <remarks>
    ///     Presence and size are separate facts in the plan for exactly this case: a file that exists and holds nothing
    ///     is a different thing to tell the user about than a file that is not there.
    /// </remarks>
    [Fact]
    public void Render_FormIdTextPlanForAnEmptyFile_ReportsZeroBytesRatherThanAbsence()
    {
        var rendered = ProcessingRunPresentation.Render(
            new PlannedRunOutcome(new FormIdTextRunPlan(@"C:\Imports\formids.txt", 0)));

        Assert.Equal(
            [@"Would import FormID list file: C:\Imports\formids.txt (0 bytes)"],
            rendered.InformationMessages);
    }

    /// <summary>
    ///     Verifies that a FormID text file the plan could not find is reported rather than treated as a failure.
    /// </summary>
    [Fact]
    public void Render_FormIdTextPlanForAMissingFile_ReportsTheFileNotFoundLine()
    {
        var rendered = ProcessingRunPresentation.Render(
            new PlannedRunOutcome(new FormIdTextRunPlan(@"C:\Imports\formids.txt", null)));

        Assert.Equal(
            [@"Would import FormID list file: C:\Imports\formids.txt (file not found)"],
            rendered.InformationMessages);
    }

    /// <summary>
    ///     Verifies that a null outcome is refused rather than rendered as an empty report.
    /// </summary>
    [Fact]
    public void Render_NullOutcome_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ProcessingRunPresentation.Render((ProcessingRunOutcome)null!));
    }

    /// <summary>
    ///     Pins the sentence each Processing Run Admission refusal reaches the user as (ADR-0010).
    /// </summary>
    /// <param name="reasonName">
    ///     The name of the refusal reason, passed as text because the reason is internal and a public test method
    ///     cannot take it as a parameter.
    /// </param>
    /// <param name="expected">The exact sentence the user reads.</param>
    [Theory]
    [InlineData(nameof(RunRejectionReason.NoGameSelected), "Please select a game from the dropdown first.")]
    [InlineData(nameof(RunRejectionReason.NoGameDirectory),
        "Game directory must be specified when processing plugins")]
    [InlineData(nameof(RunRejectionReason.PluginListLoading),
        "The plugin list is still loading for this directory. Try again when it finishes.")]
    [InlineData(nameof(RunRejectionReason.PluginListUnavailable),
        "The plugin list for this directory did not load. Browse to the directory again to retry.")]
    [InlineData(nameof(RunRejectionReason.NoPluginsSelected), "No plugins selected")]
    [InlineData(nameof(RunRejectionReason.NoDatabaseLocation),
        "Choose a database file: the default location is unavailable.")]
    public void Render_RunRejectionReason_ReturnsTheRefusalSentence(string reasonName, string expected)
    {
        var reason = Enum.Parse<RunRejectionReason>(reasonName);

        Assert.Equal(expected, ProcessingRunPresentation.Render(reason));
    }

    /// <summary>
    ///     Verifies that every refusal reason has a sentence, so adding a reason without wording fails here.
    /// </summary>
    [Fact]
    public void Render_EveryRunRejectionReason_HasASentence()
    {
        foreach (var reason in Enum.GetValues<RunRejectionReason>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ProcessingRunPresentation.Render(reason)));
        }
    }

    /// <summary>
    ///     Verifies that a refusal reason this module does not know is refused rather than rendered as empty text.
    /// </summary>
    [Fact]
    public void Render_UndefinedRunRejectionReason_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessingRunPresentation.Render((RunRejectionReason)999));
    }

    /// <summary>
    ///     Pins the generic sentence an unexpected failure reaches the user as: the failure's own message behind the
    ///     legacy "Error processing FormIDs" prefix.
    /// </summary>
    [Fact]
    public void RenderFailure_AnyOtherException_PrefixesTheFailureMessage()
    {
        var rendered = ProcessingRunPresentation.RenderFailure(new InvalidOperationException("store unavailable"));

        Assert.Equal("Error processing FormIDs: store unavailable", rendered);
    }

    /// <summary>
    ///     Pins that an Unresolvable Master reaches the user as its own message, unwrapped, because that message already
    ///     names the missing master and the directory that must supply it (ADR-0006).
    /// </summary>
    /// <remarks>
    ///     Issue #103, under parent #102. The expected sentence is written out rather than read back from the exception,
    ///     so this pins the whole sentence the user reads and cannot pass because the renderer echoed whatever it was
    ///     given. The exception's own wording is pinned by <c>ProcessingRunContractTests</c>.
    /// </remarks>
    [Fact]
    public void RenderFailure_UnresolvableMaster_RendersTheExceptionMessageUnwrapped()
    {
        var rendered = ProcessingRunPresentation.RenderFailure(
            new UnresolvableMasterException("User.esp", "Starfield.esm"));

        Assert.Equal(
            "Could not resolve 'Starfield.esm', a master file declared by User.esp. This game separates master " +
            "files by type in the load order, so that master must be present in the Data directory being " +
            "processed before any selected plugin can be read.",
            rendered);
    }

    /// <summary>
    ///     Pins the sentence an unrecognized Plugin-read failure reaches the user as: it names the Plugin and what was
    ///     being done to it, says how to work around it, and ends with the original failure's message (ADR-0016 rule 7).
    /// </summary>
    /// <remarks>
    ///     Issue #136. The phase is passed by name because it is internal and a public test method cannot take it as a
    ///     parameter. The expected sentences are written out so the renderer cannot pass by echoing its input.
    /// </remarks>
    /// <param name="phaseName">The name of the Plugin-read phase the failure arrived in.</param>
    /// <param name="expected">The exact sentence the user reads.</param>
    [Theory]
    [InlineData(nameof(PluginReadPhase.OpeningPlugin),
        "Error processing FormIDs: opening Broken.esp failed unexpectedly, so the run stopped. Deselect Broken.esp " +
        "to process the other selected plugins. Cause: Adapter state is broken.")]
    [InlineData(nameof(PluginReadPhase.ReadingRecords),
        "Error processing FormIDs: reading the records of Broken.esp failed unexpectedly, so the run stopped. " +
        "Deselect Broken.esp to process the other selected plugins. Cause: Adapter state is broken.")]
    public void RenderFailure_UnrecognizedPluginReadFailure_NamesThePluginAndThePhase(
        string phaseName,
        string expected)
    {
        var failure = new UnrecognizedPluginReadFailureException(
            "Broken.esp",
            Enum.Parse<PluginReadPhase>(phaseName),
            new InvalidOperationException("Adapter state is broken."));

        Assert.Equal(expected, ProcessingRunPresentation.RenderFailure(failure));
    }

    /// <summary>
    ///     Verifies that rendering a missing failure is refused rather than rendered as a message about nothing.
    /// </summary>
    [Fact]
    public void RenderFailure_NullFailure_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ProcessingRunPresentation.RenderFailure(null!));
    }

    /// <summary>
    ///     Renders the report of one selected-Plugin run that ended with the supplied outcomes.
    /// </summary>
    /// <param name="outcomes">One outcome per selected Plugin, in selection order.</param>
    /// <returns>The rendered report.</returns>
    /// <remarks>
    ///     The ingestion request is rebuilt from the outcomes because a report validates its outcomes against the
    ///     selection they came from. No executor is involved: an outcome is a value, and rendering one is all this
    ///     suite is about.
    /// </remarks>
    private static RenderedRunReport RenderPluginRun(params PluginIngestionOutcome[] outcomes)
    {
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory),
            "ingestion.db",
            outcomes.Select(static outcome => outcome.PluginName),
            UpdateMode.Append);

        return ProcessingRunPresentation.Render(new PluginRunOutcome(new PluginIngestionReport(request, outcomes)));
    }

    /// <summary>
    ///     Asserts the complete rendered report of one completed run across all three of its message lists.
    /// </summary>
    /// <param name="rendered">The report rendered from how the run ended.</param>
    /// <param name="expectedCompletion">The exact completion line, the run's only information message.</param>
    /// <param name="expectedWarning">The exact warning message, or null when the run rendered none.</param>
    /// <param name="expectedError">The exact failure message, or null when the run rendered none.</param>
    /// <remarks>
    ///     Issue #94. The completion line is how the run ended, a terminal fact, so it is the information message next
    ///     to where a dry run's plan and a cancelled run's acknowledgement already go. It used to be the run's terminal
    ///     activity, which the run erased again as it handed the progress channel back (#60). The warning and failure
    ///     details stay in their own lists, so the completion line is asserted as the single information message.
    /// </remarks>
    private static void AssertTerminalReport(
        RenderedRunReport rendered,
        string expectedCompletion,
        string? expectedWarning = null,
        string? expectedError = null)
    {
        string[] expectedWarnings = expectedWarning is null ? [] : [expectedWarning];
        string[] expectedErrors = expectedError is null ? [] : [expectedError];

        Assert.Equal(expectedWarnings, rendered.WarningMessages);
        Assert.Equal(expectedErrors, rendered.ErrorMessages);
        Assert.Equal([expectedCompletion], rendered.InformationMessages);
    }

    /// <summary>
    ///     Asserts the complete report of a run whose single selected Plugin was skipped for one reason.
    /// </summary>
    /// <param name="rendered">The rendered report.</param>
    /// <param name="expectedDetail">The exact skip detail line expected under the warning summary.</param>
    private static void AssertSingleSkipReport(RenderedRunReport rendered, string expectedDetail)
    {
        AssertTerminalReport(
            rendered,
            "Processing completed with warnings: 0 ingested, 1 skipped, and 0 failed Plugins.",
            Lines("1 processing warning.", expectedDetail));
    }

    /// <summary>
    ///     Asserts the complete report of a run whose single selected Plugin failed to be read.
    /// </summary>
    /// <param name="rendered">The rendered report.</param>
    /// <param name="expectedDetail">The exact failure detail line expected under the failure summary.</param>
    private static void AssertSingleFailureReport(RenderedRunReport rendered, string expectedDetail)
    {
        AssertTerminalReport(
            rendered,
            "Processing completed with failures: 0 ingested, 0 skipped, and 1 failed Plugins.",
            expectedError: Lines("1 failed plugin.", expectedDetail));
    }

    /// <summary>
    ///     Joins expected report lines the way the presentation joins them.
    /// </summary>
    /// <param name="lines">The expected lines, summary first.</param>
    /// <returns>The expected multi-line message text.</returns>
    /// <remarks>
    ///     The separator is <see cref="Environment.NewLine" /> rather than a literal, because the renderer uses the
    ///     platform separator: pinning a literal here would assert the platform, not the wording.
    /// </remarks>
    private static string Lines(params string[] lines)
    {
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    ///     Renders under the invariant culture, for the assertions that pin a grouped number.
    /// </summary>
    /// <typeparam name="TRendered">What the render returns.</typeparam>
    /// <param name="render">The render to run.</param>
    /// <returns>What the render returned.</returns>
    /// <remarks>
    ///     The grouping separator is the machine's otherwise, and those tests would then assert the regional settings
    ///     rather than the wording. <see cref="CultureInfo.CurrentCulture" /> is an <c>AsyncLocal</c>, so the
    ///     assignment is confined to this asynchronous flow and cannot reach the tests running beside it in this
    ///     parallelized collection — and it is restored either way, so a failed assertion leaves nothing behind.
    /// </remarks>
    private static TRendered UnderInvariantCulture<TRendered>(Func<TRendered> render)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
