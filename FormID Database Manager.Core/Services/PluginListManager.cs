using System.Collections.ObjectModel;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Applies Plugin List refresh results to the ViewModel while the deeper refresh module owns Plugin List rules.
/// </summary>
public class PluginListManager
{
    private readonly IThreadDispatcher _dispatcher;
    private readonly IPluginListRefresh _pluginListRefresh;
    private readonly MainWindowViewModel _viewModel;
    private int _refreshVersion;

    /// <summary>
    ///     Creates the ViewModel adapter for loading Plugin Lists.
    /// </summary>
    /// <param name="gameDetectionService">The GameRelease rule source used by the refresh module.</param>
    /// <param name="viewModel">The UI projection updated by the adapter.</param>
    /// <param name="dispatcher">The UI thread dispatcher used for ViewModel mutation.</param>
    /// <param name="loadOrderProvider">Optional load-order adapter; production uses the Mutagen-backed provider.</param>
    public PluginListManager(
        GameDetectionService gameDetectionService,
        MainWindowViewModel viewModel,
        IThreadDispatcher dispatcher,
        IGameLoadOrderProvider? loadOrderProvider = null)
        : this(new PluginListRefresh(gameDetectionService, loadOrderProvider), viewModel, dispatcher)
    {
    }

    internal PluginListManager(
        IPluginListRefresh pluginListRefresh,
        MainWindowViewModel viewModel,
        IThreadDispatcher dispatcher)
    {
        _pluginListRefresh = pluginListRefresh ?? throw new ArgumentNullException(nameof(pluginListRefresh));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    ///     Refreshes the ViewModel's Plugin List projection for the selected GameRelease and directory.
    /// </summary>
    /// <param name="gameDirectory">The selected game root or existing Data directory.</param>
    /// <param name="gameRelease">The GameRelease whose load-order and base Plugin rules apply.</param>
    /// <param name="plugins">The UI collection that receives the loaded Plugin List.</param>
    /// <param name="showAdvanced">Whether Advanced Mode is active for base Plugin filtering.</param>
    /// <returns>A task that completes after refresh and any applicable ViewModel projection finish.</returns>
    public virtual async Task RefreshPluginList(
        string gameDirectory,
        GameRelease gameRelease,
        ObservableCollection<PluginListItem> plugins,
        bool showAdvanced)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var refreshVersion = Interlocked.Increment(ref _refreshVersion);
        var request = new PluginListRefreshRequest(
            gameDirectory,
            gameRelease,
            ToAdvancedMode(showAdvanced));
        var progress = new PluginListRefreshProgressAdapter(
            _viewModel,
            _dispatcher,
            () => refreshVersion == Volatile.Read(ref _refreshVersion));
        var result = await _pluginListRefresh.RefreshAsync(request, progress).ConfigureAwait(false);

        switch (result.Status)
        {
            case PluginListRefreshStatus.Completed:
                await ApplyCompletedResultAsync(result, plugins, showAdvanced).ConfigureAwait(false);
                break;

            case PluginListRefreshStatus.Failed:
                await ApplyFailedResultAsync(result, plugins).ConfigureAwait(false);
                break;

            case PluginListRefreshStatus.Stale:
            case PluginListRefreshStatus.Cancelled:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unsupported Plugin List refresh status.");
        }
    }

    /// <summary>
    ///     Marks all plugins in the provided collection as selected by setting their IsSelected property to true.
    /// </summary>
    /// <param name="plugins">The collection of plugins to be updated with selection state.</param>
    public virtual void SelectAll(ObservableCollection<PluginListItem> plugins)
    {
        foreach (var plugin in plugins)
        {
            plugin.IsSelected = true;
        }
    }

    /// <summary>
    ///     Deselects all plugins in the specified collection by setting their selection state to false.
    /// </summary>
    /// <param name="plugins">The collection of plugins whose selection state will be cleared.</param>
    public virtual void SelectNone(ObservableCollection<PluginListItem> plugins)
    {
        foreach (var plugin in plugins)
        {
            plugin.IsSelected = false;
        }
    }

    private static AdvancedMode ToAdvancedMode(bool showAdvanced)
    {
        return showAdvanced ? AdvancedMode.On : AdvancedMode.Off;
    }

    private Task ApplyCompletedResultAsync(
        PluginListRefreshResult result,
        ObservableCollection<PluginListItem> plugins,
        bool showAdvanced)
    {
        return _dispatcher.InvokeAsync(() =>
        {
            ClearScanningState();

            _viewModel.SuspendFilter();
            try
            {
                plugins.Clear();
                _viewModel.FilteredPlugins.Clear();

                foreach (var plugin in result.Plugins)
                {
                    plugins.Add(new PluginListItem { Name = plugin.Name, IsSelected = false });
                }
            }
            finally
            {
                _viewModel.ResumeFilter();
            }

            var pluginLabel = showAdvanced ? "plugins" : "non-base game plugins";
            _viewModel.AddInformationMessage($"Loaded {result.LoadedCount} {pluginLabel}");
        });
    }

    private Task ApplyFailedResultAsync(
        PluginListRefreshResult result,
        ObservableCollection<PluginListItem> plugins)
    {
        return _dispatcher.InvokeAsync(() =>
        {
            ClearScanningState();
            plugins.Clear();
            _viewModel.FilteredPlugins.Clear();

            var message = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Unknown error"
                : result.ErrorMessage;
            _viewModel.AddErrorMessage($"Failed to load plugins: {message}");
            _viewModel.AddErrorMessage("Ensure you selected the correct game Data directory");
        });
    }

    private void ClearScanningState()
    {
        _viewModel.IsScanning = false;
        _viewModel.ProgressValue = 0;
        _viewModel.ProgressStatus = string.Empty;
    }

    private sealed class PluginListRefreshProgressAdapter(
        MainWindowViewModel viewModel,
        IThreadDispatcher dispatcher,
        Func<bool> isCurrentRefresh)
        : IProgress<PluginListRefreshProgress>
    {
        /// <inheritdoc />
        public void Report(PluginListRefreshProgress value)
        {
            dispatcher.Post(() =>
            {
                // Post is asynchronous, so freshness must be checked on the dispatcher after any newer refresh starts.
                if (!isCurrentRefresh())
                {
                    return;
                }

                viewModel.IsScanning = true;

                if (value.ScannedCount == 0 || value.TotalCount == 0)
                {
                    viewModel.UpdateProgress("Scanning plugins...", 0);
                    return;
                }

                viewModel.UpdateProgress(
                    $"Scanning plugins... ({value.ScannedCount}/{value.TotalCount})",
                    (double)value.ScannedCount / value.TotalCount * 100);
            });
        }
    }
}
