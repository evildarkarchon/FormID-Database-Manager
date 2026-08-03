namespace FormID_Database_Manager.Services;

/// <summary>
///     Executes one Processing Run from a domain request and emits typed run events.
/// </summary>
public sealed class ProcessingRunExecutor : IProcessingRunExecutor
{
    private const int OutcomeDetailLimit = 5;

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
    /// <param name="progress">Optional typed run event reporter.</param>
    /// <returns>A task that completes when the run completes, fails, or observes cancellation.</returns>
    public async Task ExecuteAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunEvent>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cancellationSource = StartCancellationSource();
        try
        {
            // Do not pass the token to Task.Run scheduling: even a pre-cancelled run must enter the worker
            // so it can report cancellation and release the source owned by this execution.
            await Task.Run(() => ExecuteCoreAsync(request, progress, cancellationSource.Token))
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

    private static void ReportError(IProgress<ProcessingRunEvent>? progress, string message)
    {
        progress?.Report(ProcessingRunEvent.Error(message));
    }

    private static void ReportWarning(IProgress<ProcessingRunEvent>? progress, string message)
    {
        progress?.Report(ProcessingRunEvent.Warning(message));
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
    /// <param name="progress">Optional typed run event reporter.</param>
    /// <param name="cancellationToken">The execution-owned token used by initialization and ingestion.</param>
    /// <returns>A task that completes with the run and propagates cancellation or processing failures unchanged.</returns>
    private async Task ExecuteCoreAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.DryRun)
            {
                ReportDryRun(request, progress);
                return;
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
                        await ExecuteTextFileRunAsync(textRequest, recordStore, progress, cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case PluginProcessingRunRequest pluginRequest:
                        await ExecutePluginRunAsync(pluginRequest, recordStore, progress, cancellationToken)
                            .ConfigureAwait(false);
                        break;

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
        // Cancellation reports nothing of its own and is excluded from failure formatting: the acknowledgement is a
        // terminal fact that belongs in the message lists, while this channel is transient and is cleared as soon as
        // the run ends, so anything written here about a cancelled run is erased before the user can read it (#60).
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportStatus(progress, $"Error during processing: {ex.Message}");
            throw;
        }
    }

    private static void ReportDryRun(ProcessingRunRequest request, IProgress<ProcessingRunEvent>? progress)
    {
        switch (request)
        {
            case FormIdTextProcessingRunRequest textRequest:
                ReportStatus(progress, $"Would process FormID list file: {textRequest.FormIdListPath}");
                break;

            case PluginProcessingRunRequest pluginRequest:
                foreach (var pluginName in pluginRequest.PluginNames)
                {
                    ReportStatus(progress, $"Would process {pluginName}");
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request, "Unsupported Processing Run request.");
        }
    }

    private static async Task ExecuteTextFileRunAsync(
        FormIdTextProcessingRunRequest request,
        IFormIdRecordStoreSession recordStore,
        IProgress<ProcessingRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        await recordStore.ImportFormIdTextFileAsync(
                request.FormIdListPath,
                request.UpdateMode,
                CreateStoreProgressAdapter(progress),
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        await recordStore.OptimizeAsync(cancellationToken).ConfigureAwait(false);

        // Cancellation accepted during maintenance must surface as cancellation, never as a success terminal event.
        cancellationToken.ThrowIfCancellationRequested();
        ReportStatus(progress, "Processing completed successfully!", 100);
    }

    /// <summary>
    ///     Runs one complete selected-Plugin operation, performs explicit successful-run maintenance, then formats the
    ///     authoritative ordered report into Processing Run events.
    /// </summary>
    /// <param name="request">The validated immutable selected-Plugin request.</param>
    /// <param name="recordStore">The already-open Store session owned by the surrounding Processing Run.</param>
    /// <param name="progress">Optional user-facing Processing Run event reporter.</param>
    /// <param name="cancellationToken">The execution-owned token shared with Plugin Ingestion and Store maintenance.</param>
    /// <returns>A task that completes after optimization and terminal reporting.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    private async Task ExecutePluginRunAsync(
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

        // Cancellation accepted during maintenance must surface as cancellation, so no outcome is formatted or reported.
        cancellationToken.ThrowIfCancellationRequested();

        // Outcome wording is intentionally delayed until maintenance succeeds so a failed optimization has no terminal summary.
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
        var failedPlugins = report.Outcomes.OfType<FailedPlugin>().Count();

        if (warningDetails.Count > 0)
        {
            ReportWarning(
                progress,
                FormatOutcomeDetails(
                    $"{warningDetails.Count} processing warning{(warningDetails.Count == 1 ? string.Empty : "s")}.",
                    warningDetails));
        }

        if (failedDetails.Count > 0)
        {
            ReportError(
                progress,
                FormatOutcomeDetails(
                    $"{failedPlugins} failed plugin{(failedPlugins == 1 ? string.Empty : "s")}.",
                    failedDetails));
        }

        if (failedPlugins > 0)
        {
            ReportStatus(
                progress,
                FormatCompletionStatus(
                    "Processing completed with failures",
                    ingestedPlugins,
                    skippedPlugins,
                    failedPlugins),
                100);
        }
        else if (warningDetails.Count > 0)
        {
            ReportStatus(
                progress,
                FormatCompletionStatus(
                    "Processing completed with warnings",
                    ingestedPlugins,
                    skippedPlugins,
                    failedPlugins),
                100);
        }
        else
        {
            ReportStatus(progress, "Processing completed successfully!", 100);
        }
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
        // Plugin Ingestion owns path resolution; Processing Run only turns its stable facts into presentation wording.
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

    private static IProgress<FormIdStoreProgress>? CreateStoreProgressAdapter(
        IProgress<ProcessingRunEvent>? progress)
    {
        return progress is null ? null : new StoreProgressAdapter(progress);
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

    private sealed class StoreProgressAdapter(IProgress<ProcessingRunEvent> inner)
        : IProgress<FormIdStoreProgress>
    {
        public void Report(FormIdStoreProgress value)
        {
            inner.Report(ProcessingRunEvent.Status(value.Message, value.Value));
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
