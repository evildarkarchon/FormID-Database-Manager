using System.Diagnostics.CodeAnalysis;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Compatibility adapter for callers that still use <see cref="ProcessingParameters" />.
/// </summary>
/// <remarks>
///     New production workflow code should call <see cref="ProcessingRun" /> directly. This adapter keeps older tests,
///     benchmarks, and integration surfaces working while the nullable parameter bag is retired incrementally.
/// </remarks>
public class PluginProcessingService : IDisposable
{
    private readonly IThreadDispatcher _dispatcher;
    private readonly ProcessingRun _processingRun;
    private readonly MainWindowViewModel _viewModel;

    /// <summary>
    ///     Creates the legacy processing adapter.
    /// </summary>
    /// <param name="databaseService">The database module used by the Processing Run.</param>
    /// <param name="viewModel">The UI state object that receives legacy error callbacks.</param>
    /// <param name="dispatcher">Optional dispatcher used to marshal legacy error callbacks.</param>
    /// <param name="loadOrderProvider">Optional Plugin load-order provider used by the Processing Run.</param>
    public PluginProcessingService(
        DatabaseService databaseService,
        MainWindowViewModel viewModel,
        IThreadDispatcher? dispatcher = null,
        IGameLoadOrderProvider? loadOrderProvider = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _dispatcher = dispatcher ?? new ImmediateThreadDispatcher();
        _processingRun = new ProcessingRun(
            databaseService ?? throw new ArgumentNullException(nameof(databaseService)),
            loadOrderProvider);
    }

    /// <summary>
    ///     Disposes the underlying Processing Run module.
    /// </summary>
    public virtual void Dispose()
    {
        _processingRun.Dispose();
    }

    /// <summary>
    ///     Adds an error message to the view model, ensuring thread-safe access to UI updates.
    /// </summary>
    /// <param name="message">The error message to be added.</param>
    internal void AddErrorMessage(string message)
    {
        // Use Dispatcher to ensure UI thread update.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => _viewModel.AddErrorMessage(message));
        }
        else
        {
            _viewModel.AddErrorMessage(message);
        }
    }

    /// <summary>
    ///     Adds a warning message to the view model, ensuring thread-safe access to UI updates.
    /// </summary>
    /// <param name="message">The warning message to be added.</param>
    internal void AddWarningMessage(string message)
    {
        // Use Dispatcher to ensure UI thread update.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => _viewModel.AddWarningMessage(message));
        }
        else
        {
            _viewModel.AddWarningMessage(message);
        }
    }

    /// <summary>
    ///     Processes game plugins using the legacy parameter bag.
    /// </summary>
    /// <param name="parameters">The legacy processing parameters to adapt into a Processing Run request.</param>
    /// <param name="progress">Optional legacy progress reporter.</param>
    /// <returns>A task that completes when the adapted Processing Run completes, fails, or observes cancellation.</returns>
    [RequiresUnreferencedCode(
        "Uses reflection-based name extraction for Mutagen records via PluginIngestion.")]
    public virtual Task ProcessPlugins(
        ProcessingParameters parameters,
        IProgress<(string Message, double? Value)>? progress = null)
    {
        var request = CreateRequest(parameters);
        return _processingRun.ExecuteAsync(request, new LegacyProgressAdapter(progress, AddErrorMessage, AddWarningMessage));
    }

    /// <summary>
    ///     Cancels the ongoing Processing Run, if one is in progress.
    /// </summary>
    public virtual void CancelProcessing()
    {
        _processingRun.Cancel();
    }

    private static ProcessingRunRequest CreateRequest(ProcessingParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var updateMode = parameters.UpdateMode ? UpdateMode.ReplacePluginRecords : UpdateMode.Append;
        if (!string.IsNullOrWhiteSpace(parameters.FormIdListPath))
        {
            return new FormIdTextProcessingRunRequest(
                parameters.FormIdListPath,
                parameters.DatabasePath,
                parameters.GameRelease,
                updateMode,
                parameters.DryRun);
        }

        return new PluginProcessingRunRequest(
            parameters.GameDirectory,
            parameters.DatabasePath,
            parameters.GameRelease,
            parameters.SelectedPlugins.Select(plugin => plugin.Name),
            updateMode,
            parameters.DryRun);
    }

    private sealed class LegacyProgressAdapter(
        IProgress<(string Message, double? Value)>? progress,
        Action<string> reportError,
        Action<string> reportWarning)
        : IProgress<ProcessingRunEvent>
    {
        public void Report(ProcessingRunEvent value)
        {
            if (value.Kind == ProcessingRunEventKind.Error)
            {
                reportError(value.Message);
            }
            else if (value.Kind == ProcessingRunEventKind.Warning)
            {
                reportWarning(value.Message);
            }

            progress?.Report((value.Message, value.Value));
        }
    }
}
