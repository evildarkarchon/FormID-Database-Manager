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

        ImmutableArray<string> warningMessages = warningDetails.Count == 0
            ? ImmutableArray<string>.Empty
            : [
                FormatOutcomeDetails(
                    $"{warningDetails.Count} processing warning{(warningDetails.Count == 1 ? string.Empty : "s")}.",
                    warningDetails)
            ];
        ImmutableArray<string> errorMessages = failedDetails.Count == 0
            ? ImmutableArray<string>.Empty
            : [
                FormatOutcomeDetails(
                    $"{failedPlugins} failed plugin{(failedPlugins == 1 ? string.Empty : "s")}.",
                    failedDetails)
            ];

        // Failures outrank warnings in the completion status, because the count line has to name the worse of the two.
        var completionStatus = failedPlugins > 0
            ? FormatCompletionStatus("Processing completed with failures", ingestedPlugins, skippedPlugins, failedPlugins)
            : warningDetails.Count > 0
                ? FormatCompletionStatus("Processing completed with warnings", ingestedPlugins, skippedPlugins, failedPlugins)
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
            PluginRunPlan pluginPlan => pluginPlan.PluginNames
                .Select(static pluginName => $"Would process {pluginName}")
                .ToImmutableArray(),
            FormIdTextRunPlan textPlan =>
                ImmutableArray.Create($"Would process FormID list file: {textPlan.FormIdListPath}"),
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unsupported Processing Run plan.")
        };

        return new RenderedRunReport(
            ActivityProjection.None,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            plannedWork);
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
