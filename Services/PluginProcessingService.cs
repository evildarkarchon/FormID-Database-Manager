using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Avalonia.Threading;

namespace FormID_Database_Manager.Services;

public class PluginProcessingService
{
    private readonly DatabaseService _databaseService;
    private readonly FormIdTextProcessor _textProcessor;
    private readonly ModProcessor _modProcessor;
    private readonly MainWindowViewModel _viewModel;
    private CancellationTokenSource? _cancellationTokenSource;

    public PluginProcessingService(
        DatabaseService databaseService,
        MainWindowViewModel viewModel)
    {
        _databaseService = databaseService;
        _viewModel = viewModel;
        _textProcessor = new FormIdTextProcessor(databaseService, AddErrorMessage);
        _modProcessor = new ModProcessor(databaseService, AddErrorMessage);
    }

    private void AddErrorMessage(string message)
    {
        // Use Dispatcher to ensure UI thread update
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => _viewModel.AddErrorMessage(message));
        }
        else
        {
            _viewModel.AddErrorMessage(message);
        }
    }

    public async Task ProcessPlugins(
        ProcessingParameters parameters,
        IProgress<(string Message, double? Value)>? progress = null)
    {
        _cancellationTokenSource = new CancellationTokenSource();

        if (parameters.DryRun)
        {
            if (!string.IsNullOrEmpty(parameters.FormIdListPath))
            {
                progress?.Report(($"Would process FormID list file: {parameters.FormIdListPath}", null));
                return;
            }

            foreach (var plugin in parameters.SelectedPlugins)
            {
                if (parameters.UpdateMode)
                    progress?.Report(($"Would delete existing entries for {plugin.Name}", null));
                progress?.Report(($"Would process {plugin.Name}", null));
            }

            return;
        }

        await _databaseService.InitializeDatabase(parameters.DatabasePath, parameters.GameRelease);
        await using var conn = new SQLiteConnection($"Data Source={parameters.DatabasePath};Version=3;");
        await conn.OpenAsync(_cancellationTokenSource.Token);

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
                    _cancellationTokenSource.Token,
                    progress);

                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await _databaseService.OptimizeDatabase(conn);
                    progress?.Report(("Processing completed successfully!", 100));
                }

                return;
            }

            // Process plugins
            progress?.Report(("Initializing plugin processing...", 0));
            var env = GameEnvironment.Typical.Construct(parameters.GameRelease);
            var loadOrder = env.LoadOrder.ListedOrder.ToList();
            var pluginList = new List<PluginListItem>(parameters.SelectedPlugins);
            var successfulPlugins = 0;
            var failedPlugins = 0;

            for (var i = 0; i < pluginList.Count; i++)
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    break;

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
                        _cancellationTokenSource.Token);
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

            if (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                await _databaseService.OptimizeDatabase(conn);
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
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
    }

    public void CancelProcessing()
    {
        _cancellationTokenSource?.Cancel();
    }
}