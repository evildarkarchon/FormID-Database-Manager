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
    ///     A recoverable or fatal error message associated with the run.
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
    ///     Creates an error event.
    /// </summary>
    /// <param name="message">The user-facing error message.</param>
    public static ProcessingRunEvent Error(string message)
    {
        return new ProcessingRunEvent(ProcessingRunEventKind.Error, message);
    }
}

/// <summary>
///     Executes one Processing Run from a domain request and emits typed run events.
/// </summary>
public class ProcessingRun : IDisposable
{
    private readonly Lock _cancellationLock = new();
    private readonly DatabaseService _databaseService;
    private readonly IGameLoadOrderProvider _loadOrderProvider;
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    ///     Creates the Processing Run module.
    /// </summary>
    /// <param name="databaseService">The database module used to open the FormID Record Store.</param>
    /// <param name="loadOrderProvider">Optional Plugin load-order provider; production uses Mutagen-backed lookup.</param>
    public ProcessingRun(DatabaseService databaseService, IGameLoadOrderProvider? loadOrderProvider = null)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _loadOrderProvider = loadOrderProvider ?? new GameLoadOrderProvider();
    }

    /// <summary>
    ///     Executes the supplied Processing Run request.
    /// </summary>
    /// <param name="request">The validated domain request describing the run.</param>
    /// <param name="progress">Optional typed run event reporter.</param>
    /// <returns>A task that completes when the run completes, fails, or observes cancellation.</returns>
    [RequiresUnreferencedCode(
        "Uses reflection-based name extraction for Mutagen records via ModProcessor.ProcessPlugin.")]
    public virtual async Task ExecuteAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunEvent>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cancellationTokenSource = StartCancellationSource();
        await Task.Run(
                () => ExecuteCoreAsync(request, progress, cancellationTokenSource),
                cancellationTokenSource.Token)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Requests cancellation for the active Processing Run, if one is active.
    /// </summary>
    public virtual void Cancel()
    {
        lock (_cancellationLock)
        {
            _cancellationTokenSource?.Cancel();
        }
    }

    /// <summary>
    ///     Cancels any active run and releases the owned cancellation source.
    /// </summary>
    public virtual void Dispose()
    {
        lock (_cancellationLock)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
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

    private CancellationTokenSource StartCancellationSource()
    {
        lock (_cancellationLock)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            return _cancellationTokenSource;
        }
    }

    [RequiresUnreferencedCode(
        "Uses reflection-based name extraction for Mutagen records via ModProcessor.ProcessPlugin.")]
    private async Task ExecuteCoreAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunEvent>? progress,
        CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            if (request.DryRun)
            {
                ReportDryRun(request, progress);
                return;
            }

            await using var recordStore = await FormIdRecordStore.OpenAsync(
                    _databaseService,
                    request.DatabasePath,
                    request.GameRelease,
                    cancellationTokenSource.Token)
                .ConfigureAwait(false);

            switch (request)
            {
                case FormIdTextProcessingRunRequest textRequest:
                    await ExecuteTextFileRunAsync(textRequest, recordStore, progress, cancellationTokenSource.Token)
                        .ConfigureAwait(false);
                    break;

                case PluginProcessingRunRequest pluginRequest:
                    await ExecutePluginRunAsync(pluginRequest, recordStore, progress, cancellationTokenSource.Token)
                        .ConfigureAwait(false);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(request), request, "Unsupported Processing Run request.");
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
        finally
        {
            ReleaseCancellationSource(cancellationTokenSource);
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
        FormIdRecordStore recordStore,
        IProgress<ProcessingRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        await recordStore.ImportFormIdTextFileAsync(
                request.FormIdListPath,
                request.UpdateMode,
                CreateStoreProgressAdapter(progress),
                cancellationToken)
            .ConfigureAwait(false);

        if (!cancellationToken.IsCancellationRequested)
        {
            await recordStore.OptimizeAsync(cancellationToken).ConfigureAwait(false);
            ReportStatus(progress, "Processing completed successfully!", 100);
        }
    }

    [RequiresUnreferencedCode(
        "Uses reflection-based name extraction for Mutagen records via ModProcessor.ProcessPlugin.")]
    private async Task ExecutePluginRunAsync(
        PluginProcessingRunRequest request,
        FormIdRecordStore recordStore,
        IProgress<ProcessingRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        ReportStatus(progress, "Initializing plugin processing...", 0);

        var pluginNames = request.PluginNames.ToList();
        var dataPath = GameReleaseHelper.ResolveDataPath(request.GameDirectory);
        var loadOrderSnapshot = _loadOrderProvider.BuildSnapshot(
            request.GameRelease,
            dataPath,
            includeMasterFlagsLookup: true);

        var successfulPlugins = 0;
        var failedPlugins = 0;
        var modProcessor = new ModProcessor(message => ReportError(progress, message));

        for (var i = 0; i < pluginNames.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var pluginName = pluginNames[i];
            var progressPercent = (double)(i + 1) / pluginNames.Count * 100;
            ReportStatus(progress, $"Processing plugin {i + 1} of {pluginNames.Count}: {pluginName}", progressPercent);

            try
            {
                await modProcessor.ProcessPlugin(
                        request.GameDirectory,
                        recordStore,
                        request.GameRelease,
                        pluginName,
                        loadOrderSnapshot,
                        request.UpdateMode,
                        cancellationToken)
                    .ConfigureAwait(false);
                successfulPlugins++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedPlugins++;
                ReportError(progress, $"Failed to process plugin {pluginName}: {ex.Message}");
                ReportStatus(progress, $"Error processing plugin {pluginName}: {ex.Message}");
                ReportError(progress, "Continuing with next plugin...");
            }
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            await recordStore.OptimizeAsync(cancellationToken).ConfigureAwait(false);
            if (failedPlugins > 0)
            {
                ReportStatus(
                    progress,
                    $"Processing completed with {successfulPlugins} successful and {failedPlugins} failed plugins.",
                    100);
            }
            else
            {
                ReportStatus(progress, "Processing completed successfully!", 100);
            }
        }
        else
        {
            ReportStatus(progress, "Processing cancelled.");
        }
    }

    private static IProgress<FormIdStoreProgress>? CreateStoreProgressAdapter(
        IProgress<ProcessingRunEvent>? progress)
    {
        return progress is null ? null : new StoreProgressAdapter(progress);
    }

    private void ReleaseCancellationSource(CancellationTokenSource cancellationTokenSource)
    {
        lock (_cancellationLock)
        {
            if (_cancellationTokenSource == cancellationTokenSource)
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
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
