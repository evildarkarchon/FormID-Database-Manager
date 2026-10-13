using System.Collections.Immutable;
using FormID_Database_Manager.ViewModels;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Renders how a Processing Run ended into the wording the user reads.
/// </summary>
/// <remarks>
///     <para>
///         This is a pure renderer: it takes an outcome, returns what that outcome implies, writes nothing, and holds
///         no state. Every ViewModel write stays with the User Workflow, which also keeps the run activity and the
///         lock that orders it — both carry comments explaining races (issue #60, and the window between workflow
///         commitment and the executor's cancellation source) that a change about locality of wording has no business
///         relocating.
///     </para>
///     <para>
///         That makes this a role-only mirror of <see cref="PluginListPresentationAdapter" />, deliberately: the
///         adapter writes the ViewModel and this module does not, which is why it is not named <c>...Adapter</c>.
///     </para>
/// </remarks>
internal static class ProcessingRunPresentation
{
    /// <summary>
    ///     The maximum per-Plugin detail lines one warning or failure report shows before it summarizes the rest.
    /// </summary>
    private const int OutcomeDetailLimit = 5;

    /// <summary>
    ///     Renders one transient Processing Run progress report into the status its caller should show.
    /// </summary>
    /// <param name="progress">What the run is doing right now.</param>
    /// <returns>The status text, and the percentage that goes with it when the report implies a new one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="progress" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The report is not one this module knows how to render.</exception>
    internal static RenderedRunProgress Render(ProcessingRunProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return progress switch
        {
            PreparingLoadOrder => new RenderedRunProgress("Initializing plugin ingestion...", 0),
            IngestingPlugin ingesting => new RenderedRunProgress(
                $"Ingesting plugin {ingesting.Position} of {ingesting.TotalPluginCount}: {ingesting.PluginName}",
                (double)ingesting.Position / ingesting.TotalPluginCount * 100),
            ImportingFormIdText importing => RenderFormIdTextImport(importing),
            ImportedFormIdText imported => new RenderedRunProgress(
                $"Completed processing {imported.ImportResult.PluginCount} plugins " +
                $"({imported.ImportResult.RecordCount:N0} total records)",
                100),
            ProcessingRunFailure failure => new RenderedRunProgress(
                $"Error during processing: {failure.FailureMessage}",
                null),
            _ => throw new ArgumentOutOfRangeException(
                nameof(progress),
                progress,
                "Unsupported Processing Run progress.")
        };
    }

    /// <summary>
    ///     Renders one in-flight FormID text import report from the counters it carries.
    /// </summary>
    /// <param name="importing">The import counters and the Plugin the run chose to name, if any.</param>
    /// <returns>The status text and its percentage.</returns>
    private static RenderedRunProgress RenderFormIdTextImport(ImportingFormIdText importing)
    {
        // A named Plugin carries no percentage: reaching a Plugin says nothing new about how far through the file the
        // import is, so the percentage already on screen is the honest one to keep showing.
        if (importing.MostRecentPlugin is { } pluginName)
        {
            return new RenderedRunProgress($"Processing plugin: {pluginName}", null);
        }

        // Nothing counted and no Plugin named is the report that opens an import, by the contract on the case itself.
        // This is the only place that decision is made; the run reports the counters and leaves the reading here.
        if (importing.RecordCount == 0)
        {
            return new RenderedRunProgress("Starting processing...", 0);
        }

        // A file the Store measured as empty leaves nothing to divide by, so it reads as no progress at all.
        var progressPercent = importing.TotalBytes > 0
            ? (double)importing.BytesRead / importing.TotalBytes * 100
            : 0;
        return new RenderedRunProgress(
            $"Processing: {progressPercent:F1}% ({importing.RecordCount:N0} records)",
            progressPercent);
    }

    /// <summary>
    ///     Renders one Processing Run outcome into the report its caller should show.
    /// </summary>
    /// <param name="outcome">How the run ended.</param>
    /// <returns>The activity projection the run's transient channel should show, and the messages it implies.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="outcome" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The outcome is not one this module knows how to render.</exception>
    internal static RenderedRunReport Render(ProcessingRunOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome switch
        {
            PluginRunOutcome pluginRun => RenderPluginRun(pluginRun.Report),
            FormIdTextRunOutcome textRun => RenderFormIdTextRun(textRun.ImportResult),
            PlannedRunOutcome planned => RenderPlan(planned.Plan),
            CancelledRunOutcome => RenderCancellation(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "Unsupported Processing Run outcome.")
        };
    }

    /// <summary>
    ///     Renders the authoritative ordered report of a selected-Plugin run into its warning, failure, and completion
    ///     wording.
    /// </summary>
    /// <param name="report">The complete Plugin Ingestion report in selected-Plugin order.</param>
    /// <returns>The rendered report for the run.</returns>
    private static RenderedRunReport RenderPluginRun(PluginIngestionReport report)
    {
        var warningDetails = report.Outcomes
            .SelectMany(outcome => outcome switch
            {
                IngestedPlugin { Warning: not null } ingested => [FormatProcessingWarning(ingested)],
                SkippedPlugin skipped => [FormatSkippedPluginDetail(skipped)],
                _ => Array.Empty<string>()
            })
            .ToList();
        var failedDetails = report.Outcomes
            .OfType<FailedPlugin>()
            .Select(FormatFailedPluginDetail)
            .ToList();
        var ingestedPlugins = report.Outcomes.OfType<IngestedPlugin>().Count();
        var skippedPlugins = report.Outcomes.OfType<SkippedPlugin>().Count();
        var failedPlugins = failedDetails.Count;

        var warningMessages = warningDetails.Count == 0
            ? ImmutableArray<string>.Empty
            :
            [
                FormatOutcomeDetails(
                    $"{warningDetails.Count} processing warning{(warningDetails.Count == 1 ? string.Empty : "s")}.",
                    warningDetails)
            ];
        var errorMessages = failedDetails.Count == 0
            ? ImmutableArray<string>.Empty
            :
            [
                FormatOutcomeDetails(
                    $"{failedPlugins} failed plugin{(failedPlugins == 1 ? string.Empty : "s")}.",
                    failedDetails)
            ];

        // Failures outrank warnings in the completion status, because the count line has to name the worse of the two.
        var completionStatus = failedPlugins > 0
            ? FormatCompletionStatus("Processing completed with failures", ingestedPlugins, skippedPlugins,
                failedPlugins)
            : warningDetails.Count > 0
                ? FormatCompletionStatus("Processing completed with warnings", ingestedPlugins, skippedPlugins,
                    failedPlugins)
                : "Processing completed successfully!";

        return new RenderedRunReport(
            new ActivityProjection(true, completionStatus, 100),
            warningMessages,
            errorMessages,
            ImmutableArray<string>.Empty);
    }

    /// <summary>
    ///     Renders the completion status of a FormID text-file run from the counts the Store confirmed.
    /// </summary>
    /// <param name="importResult">The distinct Plugin and valid record counts of the import.</param>
    /// <returns>The rendered report for the run.</returns>
    /// <remarks>
    ///     A text run used to end with a flat "Processing completed successfully!" while discarding these counts, even
    ///     though a selected-Plugin run has always reported its own. This is the one wording change the split makes to
    ///     a completed run.
    /// </remarks>
    private static RenderedRunReport RenderFormIdTextRun(FormIdTextFileImportResult importResult)
    {
        return new RenderedRunReport(
            new ActivityProjection(
                true,
                $"Processing completed successfully: {importResult.PluginCount} Plugins and " +
                $"{importResult.RecordCount:N0} records.",
                100),
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty);
    }

    /// <summary>
    ///     Renders what a dry run would have done.
    /// </summary>
    /// <param name="plan">The planned work.</param>
    /// <returns>The rendered report for the dry run.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The plan is not one this module knows how to render.</exception>
    /// <remarks>
    ///     The plan is the dry run's entire output and is therefore terminal, so it goes to the information messages
    ///     rather than the transient channel, which is handed back as soon as the run ends and would erase it (#60).
    /// </remarks>
    private static RenderedRunReport RenderPlan(ProcessingRunPlan plan)
    {
        var plannedWork = plan switch
        {
            PluginRunPlan pluginPlan => FormatPluginPlan(pluginPlan.Plan),
            FormIdTextRunPlan textPlan => ImmutableArray.Create(FormatPlannedFormIdTextFile(textPlan)),
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unsupported Processing Run plan.")
        };

        return new RenderedRunReport(
            ActivityProjection.None,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            plannedWork);
    }

    /// <summary>
    ///     Formats a selected-Plugin plan as one message: a summary, then one line per selected Plugin in selection
    ///     order.
    /// </summary>
    /// <param name="plan">What Plugin Ingestion would do to each selected Plugin.</param>
    /// <returns>The single planned-work message, or nothing at all when the plan covers no Plugin.</returns>
    /// <remarks>
    ///     One message rather than one per Plugin because the message lists are bounded and evict the oldest entry, so
    ///     a plan longer than that bound would silently lose its head — and the head is where the summary is. The
    ///     warning and failure reports of a completed run join their details for the same reason.
    /// </remarks>
    private static ImmutableArray<string> FormatPluginPlan(PluginIngestionPlan plan)
    {
        if (plan.Plugins.IsEmpty)
        {
            return ImmutableArray<string>.Empty;
        }

        var lines = new List<string> { FormatPlanSummary(plan) };
        lines.AddRange(plan.Plugins.Select(FormatPlannedPlugin));

        return [string.Join(Environment.NewLine, lines)];
    }

    /// <summary>
    ///     Formats the counts a selected-Plugin plan comes to, mirroring a completed run's own count line.
    /// </summary>
    /// <param name="plan">What Plugin Ingestion would do to each selected Plugin.</param>
    /// <returns>The summary line shown above the per-Plugin detail.</returns>
    private static string FormatPlanSummary(PluginIngestionPlan plan)
    {
        var wouldIngest = plan.Plugins.OfType<PlannedPluginIngestion>().Count();
        var wouldSkip = plan.Plugins.OfType<PlannedPluginSkip>().Count();
        var wouldFail = plan.Plugins.OfType<PlannedPluginFailure>().Count();

        return $"Dry run: {wouldIngest} would be ingested, {wouldSkip} would be skipped, " +
               $"and {wouldFail} would fail.";
    }

    /// <summary>
    ///     Formats what one selected Plugin would contribute to a run.
    /// </summary>
    /// <param name="planned">The planned outcome for one selected Plugin.</param>
    /// <returns>User-facing detail naming the Plugin and what would happen to it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The planned outcome is unsupported.</exception>
    private static string FormatPlannedPlugin(PlannedPlugin planned)
    {
        return planned switch
        {
            PlannedPluginIngestion ingestion => $"Would ingest {ingestion.PluginName}",
            PlannedPluginSkip skip => $"Would skip {skip.PluginName}: {FormatPlannedSkipReason(skip)}",
            // Only the opening phase is reachable from a plan, which opens overlays and enumerates no records, so the
            // wording names opening rather than switching on a phase that cannot vary here.
            PlannedPluginFailure failure =>
                $"Would fail to open {failure.PluginName}: {failure.Diagnostic.Message}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(planned),
                planned,
                "Unsupported planned Plugin outcome.")
        };
    }

    /// <summary>
    ///     Formats one predictable skip reason for a planned Plugin.
    /// </summary>
    /// <param name="skip">The planned skip facts.</param>
    /// <returns>The reason clause shown after the Plugin name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The planned skip reason is unsupported.</exception>
    private static string FormatPlannedSkipReason(PlannedPluginSkip skip)
    {
        return skip.Reason switch
        {
            PlannedSkipReason.NotPresentInLoadOrder => "not present in the load order",
            PlannedSkipReason.PluginFileUnavailable => $"could not find plugin file: {skip.ResolvedPluginPath}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(skip),
                skip.Reason,
                "Unsupported planned skip reason.")
        };
    }

    /// <summary>
    ///     Formats the presence and size of the file a FormID text-file dry run would import.
    /// </summary>
    /// <param name="plan">The planned text-file work.</param>
    /// <returns>User-facing detail naming the file and either its size or its absence.</returns>
    /// <remarks>
    ///     The line reports only what the plan looked up. It deliberately says nothing about rows or Plugins, because
    ///     the plan did not read the file to find out.
    /// </remarks>
    private static string FormatPlannedFormIdTextFile(FormIdTextRunPlan plan)
    {
        return plan.SizeInBytes is { } sizeInBytes
            ? $"Would import FormID list file: {plan.FormIdListPath} ({sizeInBytes:N0} bytes)"
            : $"Would import FormID list file: {plan.FormIdListPath} (file not found)";
    }

    /// <summary>
    ///     Renders the acknowledgement a cancelled run leaves behind.
    /// </summary>
    /// <returns>The rendered report for the cancelled run.</returns>
    /// <remarks>
    ///     The acknowledgement is a terminal fact and stays off the transient channel for the same reason the plan
    ///     does: the channel is cleared as the run ends, so an acknowledgement written there is erased before the user
    ///     can read it (#60). "Cancelling..." — what the run is doing rather than how it ended — remains the User
    ///     Workflow's to report, and is not this module's to render.
    /// </remarks>
    private static RenderedRunReport RenderCancellation()
    {
        return new RenderedRunReport(
            ActivityProjection.None,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            ["Processing cancelled by user."]);
    }

    /// <summary>
    ///     Formats one warned Ingested Plugin from structured warning facts retained by Plugin Ingestion.
    /// </summary>
    /// <param name="ingestedPlugin">The Ingested Plugin carrying a Processing Warning.</param>
    /// <returns>User-facing warning detail with bounded diagnostics and an omitted-detail count.</returns>
    /// <exception cref="ArgumentException"><paramref name="ingestedPlugin" /> has no warning facts.</exception>
    private static string FormatProcessingWarning(IngestedPlugin ingestedPlugin)
    {
        var warning = ingestedPlugin.Warning ?? throw new ArgumentException(
            "An Ingested Plugin must carry warning facts before warning formatting.",
            nameof(ingestedPlugin));
        var message = $"{ingestedPlugin.PluginName}: {warning.TotalIssueCount} recoverable record issue" +
                      $"{(warning.TotalIssueCount == 1 ? string.Empty : "s")}.";
        if (!warning.DiagnosticDetails.IsEmpty)
        {
            message += $" {string.Join("; ", warning.DiagnosticDetails)}";
        }

        if (warning.OmittedDetailCount > 0)
        {
            message += $"; and {warning.OmittedDetailCount} more.";
        }

        return message;
    }

    /// <summary>
    ///     Formats one typed Skipped Plugin reason without exposing presentation wording through Plugin Ingestion.
    /// </summary>
    /// <param name="skippedPlugin">The Skipped Plugin facts.</param>
    /// <returns>User-facing detail for the stable skip reason.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The skip reason is unsupported.</exception>
    private static string FormatSkippedPluginDetail(SkippedPlugin skippedPlugin)
    {
        // Plugin Ingestion owns path resolution; this module only turns its stable facts into presentation wording.
        var detail = skippedPlugin.Reason switch
        {
            SkippedPluginReason.NotPresentInLoadOrder =>
                $"Could not find plugin in load order: {skippedPlugin.PluginName}",
            SkippedPluginReason.PluginFileUnavailable =>
                $"Could not find plugin file: {skippedPlugin.ResolvedPluginPath}",
            SkippedPluginReason.ZeroFormIdRecords =>
                $"{skippedPlugin.PluginName} produced zero FormID records.",
            _ => throw new ArgumentOutOfRangeException(
                nameof(skippedPlugin),
                skippedPlugin.Reason,
                "Unsupported skipped Plugin reason.")
        };

        return $"{skippedPlugin.PluginName}: {detail}";
    }

    /// <summary>
    ///     Formats one Failed Plugin from its stable reason and internal diagnostic phase.
    /// </summary>
    /// <param name="failedPlugin">The Failed Plugin facts.</param>
    /// <returns>User-facing failure detail.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The Plugin-read phase is unsupported.</exception>
    private static string FormatFailedPluginDetail(FailedPlugin failedPlugin)
    {
        var detail = failedPlugin.Diagnostic.Phase switch
        {
            PluginReadPhase.OpeningPlugin =>
                $"Error opening {failedPlugin.PluginName}: {failedPlugin.Diagnostic.Message}",
            PluginReadPhase.ReadingRecords =>
                $"Error enumerating records in {failedPlugin.PluginName}: {failedPlugin.Diagnostic.Message}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(failedPlugin),
                failedPlugin.Diagnostic.Phase,
                "Unsupported Plugin-read phase.")
        };

        return $"{failedPlugin.PluginName}: {detail}";
    }

    private static string FormatOutcomeDetails(string summary, IReadOnlyList<string> details)
    {
        var lines = new List<string> { summary };
        lines.AddRange(details.Take(OutcomeDetailLimit));

        var remaining = details.Count - OutcomeDetailLimit;
        if (remaining > 0)
        {
            lines.Add($"and {remaining} more.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatCompletionStatus(
        string prefix,
        int ingestedPlugins,
        int skippedPlugins,
        int failedPlugins)
    {
        return $"{prefix}: {ingestedPlugins} ingested, {skippedPlugins} skipped, and {failedPlugins} failed Plugins.";
    }
}

/// <summary>
///     One transient Processing Run status, rendered but not yet written anywhere.
/// </summary>
/// <param name="Status">The status text for the run's progress channel.</param>
/// <param name="Value">
///     The progress percentage, or <see langword="null" /> to keep the percentage already on screen.
/// </param>
/// <remarks>
///     This is deliberately not an <see cref="ActivityProjection" />, which a terminal report does return: that type
///     carries a non-nullable percentage and so cannot express "this report changes the words but not the bar", which
///     is exactly what a Plugin-named text-import report does.
/// </remarks>
internal readonly record struct RenderedRunProgress(string Status, double? Value);

/// <summary>
///     Everything one Processing Run outcome has to say to the user, rendered but not yet written anywhere.
/// </summary>
/// <param name="Activity">
///     The run's terminal activity projection, or <see cref="ActivityProjection.None" /> when the outcome has nothing
///     to say on the transient progress channel.
/// </param>
/// <param name="WarningMessages">Messages for the warning list, in report order.</param>
/// <param name="ErrorMessages">Messages for the error list, in report order.</param>
/// <param name="InformationMessages">Messages for the information list, in report order.</param>
internal readonly record struct RenderedRunReport(
    ActivityProjection Activity,
    ImmutableArray<string> WarningMessages,
    ImmutableArray<string> ErrorMessages,
    ImmutableArray<string> InformationMessages);
