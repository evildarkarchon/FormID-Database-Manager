namespace FormID_Database_Manager.Services;

/// <summary>
///     Executes one Processing Run from a domain request, reports its transient progress, and returns how it ended.
/// </summary>
/// <remarks>
///     The run says nothing about its own terminal state: every string describing how a run ended is rendered from the
///     returned outcome by <see cref="ProcessingRunPresentation" />. The one exception is the transient status a
///     failure reports before rethrowing, because a failure produces no outcome to render.
/// </remarks>
public sealed class ProcessingRunExecutor : IProcessingRunExecutor
{
    private readonly Lock _cancellationLock = new();
    private readonly IPluginIngestion _pluginIngestion;
    private readonly IFormIdRecordStoreSessionOpener _recordStoreOpener;
    private CancellationTokenSource? _activeCancellationSource;
    private bool _disposed;

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
    ///     Executes the supplied Processing Run request.
    /// </summary>
    /// <param name="request">The validated domain request describing the run.</param>
    /// <param name="progress">Optional typed run event reporter for transient status.</param>
    /// <returns>How the run ended, including cancellation this executor was asked for.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="ObjectDisposedException">The executor has already been disposed.</exception>
    /// <exception cref="UnresolvableMasterException">
    ///     A selected Plugin declares a master the Data directory cannot supply, which fails the whole run (ADR-0006).
    /// </exception>
    public async Task<ProcessingRunOutcome> ExecuteAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunEvent>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cancellationSource = StartCancellationSource();
        try
        {
            // Do not pass the token to Task.Run scheduling: even a pre-cancelled run must enter the worker
            // so it can return the cancelled outcome and release the source owned by this execution.
            return await Task.Run(() => ExecuteCoreAsync(request, progress, cancellationSource.Token))
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteRun(cancellationSource);
        }
    }

    /// <summary>
    ///     Requests cancellation for the active Processing Run, if one is active.
    /// </summary>
    public void Cancel()
    {
        CancellationTokenSource? cancellationSource;
        lock (_cancellationLock)
        {
            cancellationSource = _activeCancellationSource;
        }

        CancelSource(cancellationSource);
    }

    /// <summary>
    ///     Cancels any active run and releases the owned cancellation source.
    /// </summary>
    public void Dispose()
    {
        CancellationTokenSource? cancellationSource;
        lock (_cancellationLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellationSource = _activeCancellationSource;
        }

        // The active execution remains the source owner and will dispose it in its finally block.
        CancelSource(cancellationSource);
    }

    private static void ReportStatus(
        IProgress<ProcessingRunEvent>? progress,
        string message,
        double? value = null)
    {
        progress?.Report(ProcessingRunEvent.Status(message, value));
    }

    /// <summary>
    ///     Creates and publishes the cancellation source for a new active Processing Run.
    /// </summary>
    /// <returns>The source owned by the new execution.</returns>
    /// <exception cref="ObjectDisposedException">The executor has already been disposed.</exception>
    private CancellationTokenSource StartCancellationSource()
    {
        CancellationTokenSource? previousSource;
        CancellationTokenSource currentSource;
        lock (_cancellationLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ProcessingRunExecutor));
            }

            previousSource = _activeCancellationSource;
            currentSource = new CancellationTokenSource();

            // Publish the newer run before cancelling the older one so delayed older completion
            // can never clear the slot now owned by the newer run.
            _activeCancellationSource = currentSource;
        }

        try
        {
            // The older execution owns disposal of its source; supersession only requests cancellation.
            CancelSource(previousSource);
            return currentSource;
        }
        catch
        {
            CompleteRun(currentSource);
            throw;
        }
    }

    /// <summary>
    ///     Executes one Processing Run on the background worker with the token owned by that execution.
    /// </summary>
    /// <param name="request">The validated domain request describing the run.</param>
    /// <param name="progress">Optional typed run event reporter for transient status.</param>
    /// <param name="cancellationToken">The execution-owned token used by initialization and ingestion.</param>
    /// <returns>How the run ended; processing failures propagate unchanged.</returns>
    private async Task<ProcessingRunOutcome> ExecuteCoreAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.DryRun)
            {
                return new PlannedRunOutcome(CreatePlan(request));
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
        // The token is owned by this execution, so cancellation observed while it is cancelled can only be the
        // cancellation this run was asked for, and is therefore matched precisely and returned as a value. A
        // cancellation nobody asked this executor for is not this run's to reinterpret and still propagates.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CancelledRunOutcome();
        }
        // Cancellation is excluded from failure formatting: the acknowledgement is a terminal fact rendered from the
        // outcome into the message lists, while this channel is transient and is cleared as soon as the run ends, so
        // anything written here about a cancelled run is erased before the user can read it (#60).
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportStatus(progress, $"Error during processing: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    ///     Builds the shallow plan a dry run reports instead of doing the work.
    /// </summary>
    /// <param name="request">The validated domain request describing the run.</param>
    /// <returns>The planned work for the request kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The request kind is unsupported.</exception>
    private static ProcessingRunPlan CreatePlan(ProcessingRunRequest request)
    {
        return request switch
        {
            FormIdTextProcessingRunRequest textRequest => new FormIdTextRunPlan(textRequest.FormIdListPath),
            PluginProcessingRunRequest pluginRequest => new PluginRunPlan([.. pluginRequest.PluginNames]),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, "Unsupported Processing Run request.")
        };
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
    /// <param name="progress">Optional transient Processing Run event reporter.</param>
    /// <param name="cancellationToken">The execution-owned token shared with the import and Store maintenance.</param>
    /// <returns>The completed text-run outcome carrying the import counts.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    private static async Task<FormIdTextRunOutcome> ExecuteTextFileRunAsync(
        FormIdTextProcessingRunRequest request,
        IFormIdRecordStoreSession recordStore,
        IProgress<ProcessingRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        var importResult = await recordStore.ImportFormIdTextFileAsync(
                request.FormIdListPath,
                request.UpdateMode,
                CreateStoreProgressAdapter(progress, request.UpdateMode),
                cancellationToken)
            .ConfigureAwait(false);

        ReportTextImportCompletion(progress, importResult);

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
    /// <param name="progress">Optional transient Processing Run event reporter.</param>
    /// <param name="cancellationToken">The execution-owned token shared with Plugin Ingestion and Store maintenance.</param>
    /// <returns>The completed selected-Plugin outcome carrying the authoritative ordered report.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    private async Task<PluginRunOutcome> ExecutePluginRunAsync(
        PluginProcessingRunRequest request,
        IFormIdRecordStoreSession recordStore,
        IProgress<ProcessingRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        var ingestionRequest = new SelectedPluginIngestionRequest(
            request.GameDirectory,
            request.GameRelease,
            request.PluginNames,
            request.UpdateMode);
        var report = await _pluginIngestion.IngestAsync(
                ingestionRequest,
                recordStore,
                CreatePluginIngestionProgressAdapter(progress),
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
    ///     Creates the adapter that renders the Store's import counters into this run's transient status events.
    /// </summary>
    /// <param name="progress">The run's event reporter, or <see langword="null" /> when nobody is listening.</param>
    /// <param name="updateMode">The run's update mode, which decides whether a newly seen Plugin is named.</param>
    /// <returns>The adapter, or <see langword="null" /> so the Store can skip reporting entirely.</returns>
    private static IProgress<FormIdStoreProgress>? CreateStoreProgressAdapter(
        IProgress<ProcessingRunEvent>? progress,
        UpdateMode updateMode)
    {
        return progress is null ? null : new StoreProgressAdapter(progress, updateMode);
    }

    /// <summary>
    ///     Reports the finished text import as the last transient status of the import.
    /// </summary>
    /// <param name="progress">The run's event reporter, or <see langword="null" /> when nobody is listening.</param>
    /// <param name="importResult">The distinct Plugin and valid record counts the Store confirmed.</param>
    /// <remarks>
    ///     This sits beside <see cref="StoreProgressAdapter" /> because it completes the same small vocabulary: between
    ///     them they render every string a text import shows, from counts the Store reports rather than sentences it
    ///     writes. Both are a temporary home until the run owns a typed progress vocabulary of its own.
    /// </remarks>
    private static void ReportTextImportCompletion(
        IProgress<ProcessingRunEvent>? progress,
        FormIdTextFileImportResult importResult)
    {
        ReportStatus(
            progress,
            $"Completed processing {importResult.PluginCount} plugins ({importResult.RecordCount:N0} total records)",
            100);
    }

    private static IProgress<PluginIngestionProgress>? CreatePluginIngestionProgressAdapter(
        IProgress<ProcessingRunEvent>? progress)
    {
        return progress is null ? null : new PluginIngestionProgressAdapter(progress);
    }

    /// <summary>
    ///     Clears the active slot when this source still owns it, then releases the execution-owned source.
    /// </summary>
    /// <param name="cancellationSource">The source owned by the completing execution.</param>
    private void CompleteRun(CancellationTokenSource cancellationSource)
    {
        lock (_cancellationLock)
        {
            if (ReferenceEquals(_activeCancellationSource, cancellationSource))
            {
                _activeCancellationSource = null;
            }
        }

        // Disposal happens only after the owning worker is finished reading its token.
        cancellationSource.Dispose();
    }

    /// <summary>
    ///     Requests cancellation while tolerating completion that wins the race and disposes the source first.
    /// </summary>
    /// <param name="cancellationSource">The source to cancel, or <see langword="null" /> when idle.</param>
    private static void CancelSource(CancellationTokenSource? cancellationSource)
    {
        try
        {
            cancellationSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The owning execution completed between the active-slot snapshot and this cancellation request.
        }
    }

    /// <summary>
    ///     Renders the FormID Record Store's import counters into this run's transient status events.
    /// </summary>
    /// <remarks>
    ///     Naming a Plugin is the run's decision, not the Store's. The Store reports every Plugin the first time it
    ///     sees one regardless of update mode, and this adapter shows those reports only when the run replaces Plugin
    ///     records — the mode that has always shown them — so an appending run gains no status updates it never had.
    /// </remarks>
    /// <remarks>
    ///     Not thread-safe, and deliberately so: one adapter serves one import, whose single reading loop is the only
    ///     caller of <see cref="Report" />. The remembered Plugin needs no synchronization under that contract, and a
    ///     caller that fans reports out across threads would break the ordering this adapter reads meaning from anyway.
    /// </remarks>
    private sealed class StoreProgressAdapter(IProgress<ProcessingRunEvent> inner, UpdateMode updateMode)
        : IProgress<FormIdStoreProgress>
    {
        private string? _mostRecentPlugin;

        /// <inheritdoc />
        public void Report(FormIdStoreProgress value)
        {
            // Nothing counted and no Plugin seen can only be the report that opens an import.
            if (value is { RecordCount: 0, MostRecentPlugin: null })
            {
                inner.Report(ProcessingRunEvent.Status("Starting processing...", 0));
                return;
            }

            // The Store advances the most recently seen Plugin only on a Plugin's first row, so a changed name is
            // exactly a newly seen Plugin and never a record-count report that happens to carry the same name. The
            // comparison matches the Store's own case-insensitive Plugin identity, because this reconstructs the
            // decision the Store already made: the two must not disagree about what counts as the same Plugin.
            if (value.MostRecentPlugin is { } pluginName &&
                !string.Equals(pluginName, _mostRecentPlugin, StringComparison.OrdinalIgnoreCase))
            {
                _mostRecentPlugin = pluginName;
                if (updateMode == UpdateMode.ReplacePluginRecords)
                {
                    inner.Report(ProcessingRunEvent.Status($"Processing plugin: {pluginName}"));
                }

                return;
            }

            // A file the Store measured as empty leaves nothing to divide by, so it reads as no progress at all.
            var progressPercent = value.TotalBytes > 0 ? (double)value.BytesRead / value.TotalBytes * 100 : 0;
            inner.Report(ProcessingRunEvent.Status(
                $"Processing: {progressPercent:F1}% ({value.RecordCount:N0} records)",
                progressPercent));
        }
    }

    private sealed class PluginIngestionProgressAdapter(IProgress<ProcessingRunEvent> inner)
        : IProgress<PluginIngestionProgress>
    {
        /// <inheritdoc />
        public void Report(PluginIngestionProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);

            switch (value.Stage)
            {
                case PluginIngestionProgressStage.PreparingLoadOrder:
                    ReportStatus(inner, "Initializing plugin ingestion...", 0);
                    break;

                case PluginIngestionProgressStage.IngestingPlugin:
                    var progressPercent = (double)value.PluginPosition!.Value / value.TotalPluginCount * 100;
                    ReportStatus(
                        inner,
                        $"Ingesting plugin {value.PluginPosition} of {value.TotalPluginCount}: {value.PluginName}",
                        progressPercent);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        value.Stage,
                        "Unsupported Plugin Ingestion progress stage.");
            }
        }
    }
}
