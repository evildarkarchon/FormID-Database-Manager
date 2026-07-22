using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     The validation failure raised when a Processing Run cannot be started from the supplied domain request.
/// </summary>
public sealed class ProcessingRunValidationException : Exception
{
    /// <summary>
    ///     Creates a validation failure with a user-facing message.
    /// </summary>
    /// <param name="message">The reason the Processing Run cannot start.</param>
    public ProcessingRunValidationException(string message) : base(message)
    {
    }
}

/// <summary>
///     Base request for one Processing Run against a FormID Record Store.
/// </summary>
public abstract record ProcessingRunRequest
{
    private protected ProcessingRunRequest(
        string databasePath,
        GameRelease gameRelease,
        UpdateMode updateMode,
        bool dryRun)
    {
        if (!dryRun && string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ProcessingRunValidationException("Database path must be specified");
        }

        DatabasePath = databasePath;
        GameRelease = gameRelease;
        UpdateMode = updateMode;
        DryRun = dryRun;
    }

    /// <summary>
    ///     The SQLite database path used by this Processing Run.
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    ///     The GameRelease whose FormID table is read or written.
    /// </summary>
    public GameRelease GameRelease { get; }

    /// <summary>
    ///     Controls whether ingested Plugin records are appended or replace existing Plugin rows.
    /// </summary>
    public UpdateMode UpdateMode { get; }

    /// <summary>
    ///     When true, the run reports what would be processed without opening the FormID Record Store.
    /// </summary>
    public bool DryRun { get; }
}

/// <summary>
///     Request for a Processing Run that ingests selected Plugin files.
/// </summary>
public sealed record PluginProcessingRunRequest : ProcessingRunRequest
{
    /// <summary>
    ///     Creates a Plugin ingestion Processing Run request.
    /// </summary>
    /// <param name="gameDirectory">The selected game root or Data directory.</param>
    /// <param name="databasePath">The SQLite database path to write.</param>
    /// <param name="gameRelease">The GameRelease whose load-order and table rules apply.</param>
    /// <param name="pluginNames">The selected Plugin names to process, captured as an immutable snapshot.</param>
    /// <param name="updateMode">The storage update behavior for ingested Plugin records.</param>
    /// <param name="dryRun">Whether to report the planned work without writing to the store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pluginNames" /> is <see langword="null" />.</exception>
    /// <exception cref="ProcessingRunValidationException">
    ///     A required path or Plugin name is blank, the selection is empty, or Plugin names are duplicated.
    /// </exception>
    public PluginProcessingRunRequest(
        string? gameDirectory,
        string databasePath,
        GameRelease gameRelease,
        IEnumerable<string> pluginNames,
        UpdateMode updateMode,
        bool dryRun = false)
        : base(databasePath, gameRelease, updateMode, dryRun)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new ProcessingRunValidationException("Game directory must be specified when processing plugins");
        }

        ArgumentNullException.ThrowIfNull(pluginNames);

        try
        {
            PluginNames = PluginSelectionSnapshot.Capture(pluginNames);
        }
        catch (ArgumentException ex)
        {
            // Public requests translate internal selection invariants into the Processing Run validation contract.
            throw new ProcessingRunValidationException(ex.Message);
        }

        GameDirectory = gameDirectory;
    }

    /// <summary>
    ///     The selected game root or Data directory.
    /// </summary>
    public string GameDirectory { get; }

    /// <summary>
    ///     The selected Plugin names captured at run start.
    /// </summary>
    public IReadOnlyList<string> PluginNames { get; }
}

/// <summary>
///     Request for a Processing Run that imports a pipe-delimited FormID text file.
/// </summary>
public sealed record FormIdTextProcessingRunRequest : ProcessingRunRequest
{
    /// <summary>
    ///     Creates a FormID text-file Processing Run request.
    /// </summary>
    /// <param name="formIdListPath">The pipe-delimited FormID text file to import.</param>
    /// <param name="databasePath">The SQLite database path to write.</param>
    /// <param name="gameRelease">The GameRelease whose table receives the imported rows.</param>
    /// <param name="updateMode">The storage update behavior for Plugins found in the text file.</param>
    /// <param name="dryRun">Whether to report the planned work without writing to the store.</param>
    public FormIdTextProcessingRunRequest(
        string? formIdListPath,
        string databasePath,
        GameRelease gameRelease,
        UpdateMode updateMode,
        bool dryRun = false)
        : base(databasePath, gameRelease, updateMode, dryRun)
    {
        if (string.IsNullOrWhiteSpace(formIdListPath))
        {
            throw new ProcessingRunValidationException("FormID text file must be specified");
        }

        FormIdListPath = formIdListPath;
    }

    /// <summary>
    ///     The pipe-delimited FormID text file to import.
    /// </summary>
    public string FormIdListPath { get; }
}

/// <summary>
///     The kind of event emitted by a Processing Run.
/// </summary>
public enum ProcessingRunEventKind
{
    /// <summary>
    ///     A status/progress update that can be shown as the current run status.
    /// </summary>
    Status,

    /// <summary>
    ///     A non-fatal Processing Warning that should be shown separately from errors.
    /// </summary>
    Warning,

    /// <summary>
    ///     A failed Plugin or fatal run error message associated with the run.
    /// </summary>
    Error
}

/// <summary>
///     A typed event emitted by a Processing Run.
/// </summary>
/// <param name="Kind">The event kind.</param>
/// <param name="Message">The user-facing event message.</param>
/// <param name="Value">Optional progress percentage.</param>
public readonly record struct ProcessingRunEvent(ProcessingRunEventKind Kind, string Message, double? Value = null)
{
    /// <summary>
    ///     Creates a status/progress event.
    /// </summary>
    /// <param name="message">The user-facing status message.</param>
    /// <param name="value">Optional progress percentage.</param>
    public static ProcessingRunEvent Status(string message, double? value = null)
    {
        return new ProcessingRunEvent(ProcessingRunEventKind.Status, message, value);
    }

    /// <summary>
    ///     Creates a warning event.
    /// </summary>
    /// <param name="message">The user-facing warning message.</param>
    public static ProcessingRunEvent Warning(string message)
    {
        return new ProcessingRunEvent(ProcessingRunEventKind.Warning, message);
    }

    /// <summary>
    ///     Creates an error event.
    /// </summary>
    /// <param name="message">The user-facing error message.</param>
    public static ProcessingRunEvent Error(string message)
    {
        return new ProcessingRunEvent(ProcessingRunEventKind.Error, message);
    }
}

/// <summary>
///     Executes typed Processing Run requests and owns cancellation for the active run.
/// </summary>
internal interface IProcessingRunExecutor : IDisposable
{
    /// <summary>
    ///     Executes the supplied Processing Run request.
    /// </summary>
    /// <param name="request">The validated domain request describing the run.</param>
    /// <param name="progress">Optional typed run event reporter.</param>
    /// <returns>A task that completes when the run completes, fails, or observes cancellation.</returns>
    [RequiresUnreferencedCode(
        "Uses reflection-based name extraction for Mutagen records via PluginIngestion.")]
    Task ExecuteAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunEvent>? progress = null);

    /// <summary>
    ///     Requests cancellation for the active Processing Run, if one is active.
    /// </summary>
    void Cancel();
}

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
    [RequiresUnreferencedCode(
        "Uses reflection-based name extraction for Mutagen records via PluginIngestion.")]
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
    [RequiresUnreferencedCode(
        "Uses reflection-based name extraction for Mutagen records via PluginIngestion.")]
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
        catch (OperationCanceledException)
        {
            ReportStatus(progress, "Processing cancelled.");
            throw;
        }
        catch (Exception ex)
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
    [RequiresUnreferencedCode(
        "Uses reflection-based name extraction for Mutagen records via PluginIngestion.")]
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
