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

        var pluginNameSnapshot = pluginNames.ToArray();
        if (pluginNameSnapshot.Length == 0)
        {
            throw new ProcessingRunValidationException("No plugins selected");
        }

        if (pluginNameSnapshot.Any(string.IsNullOrWhiteSpace))
        {
            throw new ProcessingRunValidationException("Plugin name must be specified");
        }

        GameDirectory = gameDirectory;
        PluginNames = pluginNameSnapshot;
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
    private readonly IGameLoadOrderProvider _loadOrderProvider;
    private readonly PluginIngestion _pluginIngestion;
    private readonly IFormIdRecordStoreSessionOpener _recordStoreOpener;
    private CancellationTokenSource? _activeCancellationSource;
    private bool _disposed;

    /// <summary>
    ///     Creates the Processing Run module.
    /// </summary>
    /// <param name="loadOrderProvider">Optional Plugin load-order provider; production uses Mutagen-backed lookup.</param>
    public ProcessingRunExecutor(IGameLoadOrderProvider? loadOrderProvider = null)
        : this(loadOrderProvider, new PluginIngestion(), new FormIdRecordStoreSessionOpener())
    {
    }

    internal ProcessingRunExecutor(
        IGameLoadOrderProvider? loadOrderProvider,
        PluginIngestion pluginIngestion)
        : this(loadOrderProvider, pluginIngestion, new FormIdRecordStoreSessionOpener())
    {
    }

    internal ProcessingRunExecutor(
        IGameLoadOrderProvider? loadOrderProvider,
        PluginIngestion pluginIngestion,
        IFormIdRecordStoreSessionOpener recordStoreOpener)
    {
        _loadOrderProvider = loadOrderProvider ?? new GameLoadOrderProvider();
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

    [RequiresUnreferencedCode(
        "Uses reflection-based name extraction for Mutagen records via PluginIngestion.")]
    private async Task ExecutePluginRunAsync(
        PluginProcessingRunRequest request,
        IFormIdRecordStoreSession recordStore,
        IProgress<ProcessingRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        ReportStatus(progress, "Initializing plugin ingestion...", 0);

        var pluginNames = request.PluginNames.ToList();
        var dataPath = GameReleaseHelper.ResolveDataPath(request.GameDirectory);
        var loadOrderSnapshot = _loadOrderProvider.BuildSnapshot(
            request.GameRelease,
            dataPath,
            includeMasterFlagsLookup: true);

        var successfulPlugins = 0;
        var skippedPlugins = 0;
        var failedPlugins = 0;
        var warningDetails = new List<string>();
        var failedDetails = new List<string>();

        for (var i = 0; i < pluginNames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pluginName = pluginNames[i];
            var progressPercent = (double)(i + 1) / pluginNames.Count * 100;
            ReportStatus(progress, $"Ingesting plugin {i + 1} of {pluginNames.Count}: {pluginName}", progressPercent);

            var result = await _pluginIngestion.IngestAsync(
                    new PluginIngestionRequest(
                        request.GameDirectory,
                        request.GameRelease,
                        pluginName,
                        loadOrderSnapshot,
                        request.UpdateMode),
                    recordStore,
                    cancellationToken)
                .ConfigureAwait(false);

            switch (result.Kind)
            {
                case PluginIngestionResultKind.Succeeded:
                    successfulPlugins++;
                    warningDetails.AddRange(result.Warnings);
                    break;

                case PluginIngestionResultKind.Skipped:
                    skippedPlugins++;
                    warningDetails.Add(FormatPluginOutcomeDetail(result));
                    break;

                case PluginIngestionResultKind.Failed:
                    failedPlugins++;
                    failedDetails.Add(FormatPluginOutcomeDetail(result));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(result),
                        result.Kind,
                        "Unsupported Plugin Ingestion outcome.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        await recordStore.OptimizeAsync(cancellationToken).ConfigureAwait(false);

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
                    successfulPlugins,
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
                    successfulPlugins,
                    skippedPlugins,
                    failedPlugins),
                100);
        }
        else
        {
            ReportStatus(progress, "Processing completed successfully!", 100);
        }
    }

    private static string FormatPluginOutcomeDetail(PluginIngestionResult result)
    {
        return string.IsNullOrWhiteSpace(result.Detail)
            ? result.PluginName
            : $"{result.PluginName}: {result.Detail}";
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
        int successfulPlugins,
        int skippedPlugins,
        int failedPlugins)
    {
        return $"{prefix}: {successfulPlugins} successful, {skippedPlugins} skipped, and {failedPlugins} failed plugins.";
    }

    private static IProgress<FormIdStoreProgress>? CreateStoreProgressAdapter(
        IProgress<ProcessingRunEvent>? progress)
    {
        return progress is null ? null : new StoreProgressAdapter(progress);
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
}
