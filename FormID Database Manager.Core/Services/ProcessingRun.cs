namespace FormID_Database_Manager.Services;

/// <summary>
///     Executes one Processing Run from a domain request with the cancellation token its caller hands it, reports its
///     transient progress, and returns how it ended.
/// </summary>
/// <remarks>
///     <para>
///         The run writes no wording at all. What it is doing is reported as <see cref="ProcessingRunProgress" />, a
///         vocabulary the run defines for itself rather than one borrowed from Plugin Ingestion or the FormID Record
///         Store, and how it ended is a <see cref="ProcessingRunOutcome" />. Both are turned into words by
///         <see cref="ProcessingRunPresentation" />, which lives outside this class.
///     </para>
///     <para>
///         The executor holds no run state: no cancellation slot, no way to cancel, and nothing to dispose. Whether a
///         Processing Run is active, and its cancellation, belong to whoever owns the run's token (ADR-0012), which
///         creates that token the moment admission admits a press, so a cancel pressed before the run reaches this
///         executor is never dropped.
///     </para>
/// </remarks>
public sealed class ProcessingRunExecutor : IProcessingRunExecutor
{
    private readonly IPluginIngestion _pluginIngestion;
    private readonly IFormIdRecordStoreSessionOpener _recordStoreOpener;

    /// <summary>
    ///     Creates the production Processing Run module with aggregate Plugin Ingestion and Store session ownership.
    /// </summary>
    public ProcessingRunExecutor()
        : this(
            new PluginIngestion(),
            new FormIdRecordStoreSessionOpener())
    {
    }

    /// <summary>
    ///     Creates a Processing Run executor from its complete Plugin Ingestion and run-scoped Store seams.
    /// </summary>
    /// <param name="pluginIngestion">The complete selected-Plugin operation.</param>
    /// <param name="recordStoreOpener">The opener for the Store session owned by each non-dry run.</param>
    /// <exception cref="ArgumentNullException">Either dependency is null.</exception>
    internal ProcessingRunExecutor(
        IPluginIngestion pluginIngestion,
        IFormIdRecordStoreSessionOpener recordStoreOpener)
    {
        _pluginIngestion = pluginIngestion ?? throw new ArgumentNullException(nameof(pluginIngestion));
        _recordStoreOpener = recordStoreOpener ?? throw new ArgumentNullException(nameof(recordStoreOpener));
    }

    /// <summary>
    ///     Executes the supplied Processing Run request on a worker thread with the caller's cancellation token.
    /// </summary>
    /// <param name="request">The validated domain request describing the run.</param>
    /// <param name="progress">Optional typed reporter for the run's transient progress.</param>
    /// <param name="cancellationToken">
    ///     The run's token, owned by the caller. Cancelling it, even before this method is called, ends the run as a
    ///     <see cref="CancelledRunOutcome" />.
    /// </param>
    /// <returns>How the run ended, including the cancellation the caller requested through its token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="UnresolvableMasterException">
    ///     A selected Plugin declares a master the Data directory cannot supply, which fails the whole run (ADR-0006).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     A collaborator cancelled while <paramref name="cancellationToken" /> was not cancelled, which is not a
    ///     cancellation the caller asked for (ADR-0007).
    /// </exception>
    public async Task<ProcessingRunOutcome> ExecuteAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Do not pass the token to Task.Run scheduling: even a pre-cancelled run must enter the worker so it can
        // return the cancelled outcome rather than a cancelled task, which a caller would see as an exception.
        return await Task.Run(() => ExecuteCoreAsync(request, progress, cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Executes one Processing Run on the background worker with the caller's token.
    /// </summary>
    /// <param name="request">The validated domain request describing the run.</param>
    /// <param name="progress">Optional typed reporter for the run's transient progress.</param>
    /// <param name="cancellationToken">The run's token, used by initialization and ingestion.</param>
    /// <returns>How the run ended; processing failures propagate unchanged.</returns>
    private async Task<ProcessingRunOutcome> ExecuteCoreAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.DryRun)
            {
                return new PlannedRunOutcome(
                    await CreatePlanAsync(request, cancellationToken).ConfigureAwait(false));
            }

            var recordStore = await _recordStoreOpener.OpenAsync(
                request.DatabasePath,
                request.GameRelease,
                cancellationToken).ConfigureAwait(false);

            try
            {
                switch (request)
                {
                    case FormIdTextProcessingRunRequest textRequest:
                        return await ExecuteTextFileRunAsync(textRequest, recordStore, progress, cancellationToken)
                            .ConfigureAwait(false);

                    case PluginProcessingRunRequest pluginRequest:
                        return await ExecutePluginRunAsync(pluginRequest, recordStore, progress, cancellationToken)
                            .ConfigureAwait(false);

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(request),
                            request,
                            "Unsupported Processing Run request.");
                }
            }
            finally
            {
                try
                {
                    await recordStore.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Store disposal is best-effort cleanup and must not replace the Processing Run outcome.
                }
            }
        }
        // The token is the caller's, and only the caller cancels it, so cancellation observed while it is cancelled is
        // the cancellation the caller asked for, and is therefore matched precisely and returned as a value. A
        // cancellation observed while it is not cancelled was asked for by nobody, is not this run's to reinterpret,
        // and still propagates (ADR-0007). Failures are not caught at all: they reach the caller once, as exceptions,
        // and nothing about them is said on the transient progress channel the caller clears as the run ends.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CancelledRunOutcome();
        }
    }

    /// <summary>
    ///     Builds the plan a dry run reports instead of doing the work, without opening a FormID Record Store.
    /// </summary>
    /// <param name="request">The validated domain request describing the run.</param>
    /// <param name="cancellationToken">The caller-owned run token shared with Plugin Ingestion's planning.</param>
    /// <returns>The planned work for the request kind.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The request kind is unsupported.</exception>
    /// <exception cref="UnresolvableMasterException">
    ///     A selected Plugin declares a master the Data directory cannot supply, which fails the whole plan exactly as
    ///     it fails a whole run (ADR-0006) — and lets the user see it before committing to one.
    /// </exception>
    private async Task<ProcessingRunPlan> CreatePlanAsync(
        ProcessingRunRequest request,
        CancellationToken cancellationToken)
    {
        switch (request)
        {
            case FormIdTextProcessingRunRequest textRequest:
                return CreateFormIdTextPlan(textRequest);

            case PluginProcessingRunRequest pluginRequest:
                var plan = await _pluginIngestion
                    .PlanAsync(pluginRequest, cancellationToken)
                    .ConfigureAwait(false);
                return new PluginRunPlan(plan);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request,
                    "Unsupported Processing Run request.");
        }
    }

    /// <summary>
    ///     Looks up the two facts a FormID text-file dry run reports about the file it would import.
    /// </summary>
    /// <param name="request">The validated FormID text-file request.</param>
    /// <returns>The planned text-file work, carrying presence and size.</returns>
    /// <remarks>
    ///     This is a file-metadata lookup and nothing else: the file is not opened, parsed, or counted, because a row
    ///     count is what the import itself measures and a plan that measured it would have done the run.
    /// </remarks>
    private static FormIdTextRunPlan CreateFormIdTextPlan(FormIdTextProcessingRunRequest request)
    {
        var formIdListFile = new FileInfo(request.FormIdListPath);

        // Exists is read before Length because Length throws for a file that is not there, and absence is a fact this
        // plan reports rather than a failure it raises.
        return new FormIdTextRunPlan(
            request.FormIdListPath,
            formIdListFile.Exists ? formIdListFile.Length : null);
    }

    /// <summary>
    ///     Imports one FormID text file, performs explicit successful-run maintenance, and returns the Store's counts.
    /// </summary>
    /// <remarks>
    ///     The import's completion is reported before maintenance and before the cancellation check, which is where the
    ///     Store used to report it from: the counts describe the import, not the run, and a cancellation accepted after
    ///     the commit has never erased the fact that the import finished.
    /// </remarks>
    /// <param name="request">The validated FormID text-file request.</param>
    /// <param name="recordStore">The already-open Store session owned by the surrounding Processing Run.</param>
    /// <param name="progress">Optional transient Processing Run progress reporter.</param>
    /// <param name="cancellationToken">The caller-owned run token shared with the import and Store maintenance.</param>
    /// <returns>The completed text-run outcome carrying the import counts.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    private static async Task<FormIdTextRunOutcome> ExecuteTextFileRunAsync(
        FormIdTextProcessingRunRequest request,
        IFormIdRecordStoreSession recordStore,
        IProgress<ProcessingRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        var importResult = await recordStore.ImportFormIdTextFileAsync(
                request.FormIdListPath,
                request.UpdateMode,
                CreateStoreProgress(progress, request.UpdateMode),
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new ImportedFormIdText(importResult));

        cancellationToken.ThrowIfCancellationRequested();
        await recordStore.OptimizeAsync(cancellationToken).ConfigureAwait(false);

        // Cancellation accepted during maintenance must surface as cancellation, never as a completed outcome.
        cancellationToken.ThrowIfCancellationRequested();
        return new FormIdTextRunOutcome(importResult);
    }

    /// <summary>
    ///     Runs one complete selected-Plugin operation, performs explicit successful-run maintenance, then returns the
    ///     authoritative ordered report.
    /// </summary>
    /// <param name="request">The validated immutable selected-Plugin request.</param>
    /// <param name="recordStore">The already-open Store session owned by the surrounding Processing Run.</param>
    /// <param name="progress">Optional transient Processing Run progress reporter.</param>
    /// <param name="cancellationToken">The caller-owned run token shared with Plugin Ingestion and Store maintenance.</param>
    /// <returns>The completed selected-Plugin outcome carrying the authoritative ordered report.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    private async Task<PluginRunOutcome> ExecutePluginRunAsync(
        PluginProcessingRunRequest request,
        IFormIdRecordStoreSession recordStore,
        IProgress<ProcessingRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        var report = await _pluginIngestion.IngestAsync(
                request,
                recordStore,
                CreatePluginIngestionProgress(progress),
                cancellationToken)
            .ConfigureAwait(false);

        // A collaborator can return while cancellation is racing; incomplete work must never reach successful-run maintenance.
        cancellationToken.ThrowIfCancellationRequested();
        await recordStore.OptimizeAsync(cancellationToken).ConfigureAwait(false);

        // Cancellation accepted during maintenance must surface as cancellation, so no completed outcome is returned.
        // Returning the report is likewise delayed until maintenance succeeds, so a failed optimization has no summary.
        cancellationToken.ThrowIfCancellationRequested();
        return new PluginRunOutcome(report);
    }

    /// <summary>
    ///     Creates the reporter that states the FormID Record Store's import counters in this run's own vocabulary.
    /// </summary>
    /// <param name="progress">The run's progress reporter, or <see langword="null" /> when nobody is listening.</param>
    /// <param name="updateMode">The run's update mode, which decides whether a newly seen Plugin is named.</param>
    /// <returns>The reporter, or <see langword="null" /> so the Store can skip reporting entirely.</returns>
    private static IProgress<FormIdStoreProgress>? CreateStoreProgress(
        IProgress<ProcessingRunProgress>? progress,
        UpdateMode updateMode)
    {
        return progress is null
            ? null
            : new ProjectedProgress<FormIdStoreProgress>(progress, value => ProjectFormIdTextImport(value, updateMode));
    }

    /// <summary>
    ///     Creates the reporter that states Plugin Ingestion's stage facts in this run's own vocabulary.
    /// </summary>
    /// <param name="progress">The run's progress reporter, or <see langword="null" /> when nobody is listening.</param>
    /// <returns>The reporter, or <see langword="null" /> so Plugin Ingestion can skip reporting entirely.</returns>
    private static IProgress<PluginIngestionProgress>? CreatePluginIngestionProgress(
        IProgress<ProcessingRunProgress>? progress)
    {
        return progress is null
            ? null
            : new ProjectedProgress<PluginIngestionProgress>(progress, ProjectPluginIngestionProgress);
    }

    /// <summary>
    ///     Restates one Plugin Ingestion stage as the run's own progress.
    /// </summary>
    /// <param name="value">The structured stage facts Plugin Ingestion reported.</param>
    /// <returns>The same fact in the run's vocabulary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The Plugin Ingestion stage is unsupported.</exception>
    /// <remarks>
    ///     The stages map one for one, and this restatement exists anyway: a Processing Run reports what it is doing,
    ///     not which of its collaborators is doing it, so forwarding the collaborator's enum would leak that seam to
    ///     everyone who renders run progress.
    /// </remarks>
    private static ProcessingRunProgress ProjectPluginIngestionProgress(PluginIngestionProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Stage switch
        {
            PluginIngestionProgressStage.PreparingLoadOrder => new PreparingLoadOrder(value.TotalPluginCount),
            PluginIngestionProgressStage.IngestingPlugin => new IngestingPlugin(
                value.PluginName!,
                value.PluginPosition!.Value,
                value.TotalPluginCount),
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Stage,
                "Unsupported Plugin Ingestion progress stage.")
        };
    }

    /// <summary>
    ///     Restates one Store fact as the run's own progress without remembering previous reports.
    /// </summary>
    /// <param name="value">The counter update or Plugin first-encounter fact supplied by the Store.</param>
    /// <param name="updateMode">The run's update mode, which decides whether a newly seen Plugin is named.</param>
    /// <returns>The run report, or null for a Plugin announcement suppressed in append mode.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The Store report case is unsupported.</exception>
    private static ProcessingRunProgress? ProjectFormIdTextImport(
        FormIdStoreProgress value,
        UpdateMode updateMode)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            // There is deliberately no case for the report that opens an import: a Store report of no records that
            // names no new Plugin already *is* that report, and saying so is the renderer's decision to make. Adding
            // a branch here that produced the same value would only let the two disagree about what opens an import.
            FormIdStoreCounterUpdate => new ImportingFormIdText(value.RecordCount, value.BytesRead, value.TotalBytes,
                null),
            // Naming a Plugin is the run's decision, not the Store's. An appending run has never named one, so it
            // drops the report entirely rather than gaining status updates it does not show today.
            FormIdStorePluginFirstEncountered plugin => updateMode == UpdateMode.ReplacePluginRecords
                ? new ImportingFormIdText(value.RecordCount, value.BytesRead, value.TotalBytes, plugin.PluginName)
                : null,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported Store progress case.")
        };
    }

    /// <summary>
    ///     Reports one collaborator's progress facts in this run's own progress vocabulary.
    /// </summary>
    /// <typeparam name="TSource">The collaborator's own progress type.</typeparam>
    /// <param name="runProgress">The run's progress reporter, which receives the restated facts.</param>
    /// <param name="project">
    ///     Restates one report, or returns <see langword="null" /> for a report the run does not show at all.
    /// </param>
    private sealed class ProjectedProgress<TSource>(
        IProgress<ProcessingRunProgress> runProgress,
        Func<TSource, ProcessingRunProgress?> project) : IProgress<TSource>
    {
        /// <inheritdoc />
        public void Report(TSource value)
        {
            if (project(value) is { } projected)
            {
                runProgress.Report(projected);
            }
        }
    }
}
