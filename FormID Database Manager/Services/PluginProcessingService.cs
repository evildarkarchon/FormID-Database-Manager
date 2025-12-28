using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda.Environments;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Provides functionality to process game plugins using various parameters and services.
/// </summary>
public class PluginProcessingService : IDisposable
{
    private readonly object _cancellationLock = new();
    private readonly DatabaseService _databaseService;
    private readonly ModProcessor _modProcessor;
    private readonly FormIdTextProcessor _textProcessor;
    private readonly MainWindowViewModel _viewModel;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly IThreadDispatcher _dispatcher;

    /// <summary>
    ///     Service responsible for processing game plugins, utilizing various modules such as
    ///     database operations, form ID text processing, and mod-specific logic.
    /// </summary>
    /// <remarks>
    ///     This service integrates multiple components to handle plugin processing for different
    ///     scenarios, including adding error messages and allowing cancellation of ongoing tasks.
    /// </remarks>
    public PluginProcessingService(
        DatabaseService databaseService,
        MainWindowViewModel viewModel,
        IThreadDispatcher? dispatcher = null)
    {
        _databaseService = databaseService;
        _viewModel = viewModel;
        _dispatcher = dispatcher ?? new AvaloniaThreadDispatcher();
        _textProcessor = new FormIdTextProcessor(databaseService);
        _modProcessor = new ModProcessor(databaseService, AddErrorMessage);
    }

    /// <summary>
    ///     Disposes of the resources used by the PluginProcessingService.
    /// </summary>
    public void Dispose()
    {
        lock (_cancellationLock)
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    /// <summary>
    ///     Adds an error message to the view model, ensuring thread-safe access to UI updates.
    /// </summary>
    /// <param name="message">The error message to be added.</param>
    private void AddErrorMessage(string message)
    {
        // Use Dispatcher to ensure UI thread update
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
    ///     Processes game plugins based on the provided parameters, handling actions such as database initialization,
    ///     plugin processing, and optional text file processing.
    /// </summary>
    /// <param name="parameters">
    ///     The parameters that specify how the plugin processing should be conducted, including game directory,
    ///     selected plugins, and database path.
    /// </param>
    /// <param name="progress">
    ///     Optional progress reporter to report the current status and progress value during the processing.
    /// </param>
    /// <returns>
    ///     A task representing the asynchronous operation of processing plugins, allowing cancellation or exception handling
    ///     when required.
    /// </returns>
    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records via ModProcessor.ProcessPlugin.")]
    public async Task ProcessPlugins(
        ProcessingParameters parameters,
        IProgress<(string Message, double? Value)>? progress = null)
    {
        CancellationTokenSource cancellationTokenSource;
        lock (_cancellationLock)
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource = _cancellationTokenSource;
        }

        if (parameters.DryRun)
        {
            if (!string.IsNullOrEmpty(parameters.FormIdListPath))
            {
                progress?.Report(($"Would process FormID list file: {parameters.FormIdListPath}", null));
                return;
            }

            foreach (var plugin in parameters.SelectedPlugins)
            {
                progress?.Report(($"Would process {plugin.Name}", null));
            }

            return;
        }

        await _databaseService
            .InitializeDatabase(parameters.DatabasePath, parameters.GameRelease, cancellationTokenSource.Token)
            .ConfigureAwait(false);
        await using var conn =
            new SqliteConnection(DatabaseService.GetOptimizedConnectionString(parameters.DatabasePath));
        await conn.OpenAsync(cancellationTokenSource.Token).ConfigureAwait(false);
        await _databaseService.ConfigureConnection(conn, cancellationTokenSource.Token).ConfigureAwait(false);

        try
        {
            // Process text file if specified
            if (!string.IsNullOrEmpty(parameters.FormIdListPath))
            {
                await _textProcessor.ProcessFormIdListFile(
                    parameters.FormIdListPath,
                    conn,
                    parameters.GameRelease,
                    parameters.UpdateMode,
                    cancellationTokenSource.Token,
                    progress).ConfigureAwait(false);

                if (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await _databaseService.OptimizeDatabase(conn, cancellationTokenSource.Token)
                        .ConfigureAwait(false);
                    progress?.Report(("Processing completed successfully!", 100));
                }

                return;
            }

            // Process plugins
            progress?.Report(("Initializing plugin processing...", 0));
            var env = GameEnvironment.Typical.Construct(parameters.GameRelease);
            // Note: ToList() is required here because ModProcessor.ProcessPlugin expects IList
            var loadOrder = env.LoadOrder.ListedOrder.ToList();
            var pluginList = new List<PluginListItem>(parameters.SelectedPlugins);
            var successfulPlugins = 0;
            var failedPlugins = 0;

            for (var i = 0; i < pluginList.Count; i++)
            {
                if (cancellationTokenSource.Token.IsCancellationRequested)
                {
                    break;
                }

                var pluginItem = pluginList[i];
                var progressPercent = (double)(i + 1) / pluginList.Count * 100;
                progress?.Report(($"Processing plugin {i + 1} of {pluginList.Count}: {pluginItem.Name}",
                    progressPercent));

                try
                {
                    await _modProcessor.ProcessPlugin(
                        parameters.GameDirectory!,
                        conn,
                        parameters.GameRelease,
                        pluginItem,
                        loadOrder,
                        parameters.UpdateMode,
                        cancellationTokenSource.Token).ConfigureAwait(false);
                    successfulPlugins++;
                }
                catch (Exception ex)
                {
                    failedPlugins++;
                    AddErrorMessage($"Failed to process plugin {pluginItem.Name}: {ex.Message}");
                    progress?.Report(($"Error processing plugin {pluginItem.Name}: {ex.Message}", null));
                    AddErrorMessage("Continuing with next plugin...");
                }
            }

            if (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                await _databaseService.OptimizeDatabase(conn, cancellationTokenSource.Token)
                    .ConfigureAwait(false);
                if (failedPlugins > 0)
                {
                    progress?.Report(
                        ($"Processing completed with {successfulPlugins} successful and {failedPlugins} failed plugins.",
                            100));
                }
                else
                {
                    progress?.Report(("Processing completed successfully!", 100));
                }
            }
            else
            {
                progress?.Report(("Processing cancelled.", null));
            }
        }
        catch (OperationCanceledException)
        {
            progress?.Report(("Processing cancelled.", null));
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report(($"Error during processing: {ex.Message}", null));
            throw;
        }
        finally
        {
            lock (_cancellationLock)
            {
                if (_cancellationTokenSource == cancellationTokenSource)
                {
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                }
            }
        }
    }

    /// <summary>
    ///     Cancels the ongoing plugin processing operation, if one is in progress.
    /// </summary>
    /// <remarks>
    ///     This method signals the cancellation token source associated with the current
    ///     plugin processing task, allowing the operation to terminate gracefully. If no
    ///     processing task is active, the method has no effect.
    /// </remarks>
    public void CancelProcessing()
    {
        lock (_cancellationLock)
        {
            _cancellationTokenSource?.Cancel();
        }
    }
}
