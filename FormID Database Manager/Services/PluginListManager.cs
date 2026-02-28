using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Manages the list of plugins for a game environment, including operations
///     such as loading plugin data, refreshing the displayed list, and managing
///     plugin selection states.
/// </summary>
public class PluginListManager(
    GameDetectionService gameDetectionService,
    MainWindowViewModel viewModel,
    IThreadDispatcher dispatcher,
    IGameLoadOrderProvider? loadOrderProvider = null)
{
    private int _refreshVersion;

    /// <summary>
    ///     Refreshes the list of plugins by loading data from the specified game directory and game release.
    ///     Clears the existing plugin lists, adds the updated plugin list, and updates the filtered plugin list.
    ///     Displays messages for the plugin load process and handles errors if the operation fails.
    ///     Offloads synchronous I/O operations to background thread to prevent UI freezing (1-10s for 1000+ plugins on HDD).
    /// </summary>
    /// <param name="gameDirectory">The path to the game directory containing the "Data" folder.</param>
    /// <param name="gameRelease">The version of the game for which the plugins are being refreshed.</param>
    /// <param name="plugins">The observable collection that will hold the list of plugins to be displayed.</param>
    /// <param name="showAdvanced">A flag indicating whether advanced mode is enabled for filtering plugins.</param>
    /// <returns>A task that represents the asynchronous operation of refreshing the plugin list.</returns>
    public async Task RefreshPluginList(
        string gameDirectory,
        GameRelease gameRelease,
        ObservableCollection<PluginListItem> plugins,
        bool showAdvanced)
    {
        var refreshVersion = Interlocked.Increment(ref _refreshVersion);

        bool IsLatestRefresh()
        {
            return refreshVersion == Volatile.Read(ref _refreshVersion);
        }

        try
        {
            // Run expensive I/O operations on background thread to avoid UI freeze
            // For 1000+ plugins on HDD, this prevents 1-10 second UI freeze
            var (pluginItems, loadedCount, isStale) = await Task.Run(() =>
            {
                if (!IsLatestRefresh())
                {
                    return (new List<PluginListItem>(), 0, true);
                }

                var effectiveLoadOrderProvider = loadOrderProvider ?? new GameLoadOrderProvider();

                // Determine the data path
                var dataPath = GameReleaseHelper.ResolveDataPath(gameDirectory);

                var loadOrderSnapshot = effectiveLoadOrderProvider.BuildSnapshot(gameRelease, dataPath);
                var loadOrder = loadOrderSnapshot.ListedPluginNames;
                var basePluginSet = new HashSet<string>(
                    gameDetectionService.GetBaseGamePlugins(gameRelease),
                    StringComparer.OrdinalIgnoreCase);

                var items = new List<PluginListItem>();
                var addedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var count = 0;

                var totalPlugins = loadOrder.Count;
                var scannedCount = 0;

                // Show scanning progress on UI thread
                dispatcher.Post(() =>
                {
                    if (!IsLatestRefresh())
                    {
                        return;
                    }

                    viewModel.IsScanning = true;
                    viewModel.UpdateProgress("Scanning plugins...", 0);
                });

                // Process plugins - File.Exists calls now on background thread
                foreach (var plugin in loadOrder)
                {
                    if (!IsLatestRefresh())
                    {
                        return (items, count, true);
                    }

                    var pluginFileName = plugin;

                    // Report scanning progress every 10th plugin to reduce UI thread pressure
                    scannedCount++;
                    if (scannedCount % 10 == 0 || scannedCount == totalPlugins)
                    {
                        var currentCount = scannedCount;
                        var total = totalPlugins;
                        dispatcher.Post(() =>
                        {
                            if (!IsLatestRefresh())
                            {
                                return;
                            }

                            viewModel.UpdateProgress($"Scanning plugins... ({currentCount}/{total})", (double)currentCount / total * 100);
                        });
                    }

                    // Deduplication check
                    if (addedPlugins.Contains(pluginFileName))
                    {
                        continue;
                    }

                    // Skip base plugins if not in advanced mode
                    if (!showAdvanced && basePluginSet.Contains(pluginFileName))
                    {
                        continue;
                    }

                    // Check if the plugin file exists (synchronous I/O now safe on background thread)
                    var pluginPath = Path.Combine(dataPath, pluginFileName);
                    if (!File.Exists(pluginPath))
                    {
                        continue;
                    }

                    // Add the plugin to the list
                    items.Add(new PluginListItem { Name = pluginFileName, IsSelected = false });
                    addedPlugins.Add(pluginFileName);
                    count++;
                }

                // Clear scanning state
                dispatcher.Post(() =>
                {
                    if (!IsLatestRefresh())
                    {
                        return;
                    }

                    viewModel.IsScanning = false;
                    viewModel.ProgressValue = 0;
                    viewModel.ProgressStatus = string.Empty;
                });

                return (items, count, false);
            }).ConfigureAwait(false);

            if (isStale || !IsLatestRefresh())
            {
                return;
            }

            // Update UI on UI thread
            await dispatcher.InvokeAsync(() =>
            {
                if (!IsLatestRefresh())
                {
                    return;
                }

                // Suspend filter during bulk add to avoid N ApplyFilter calls
                viewModel.SuspendFilter();
                try
                {
                    // Clear plugin lists
                    plugins.Clear();
                    viewModel.FilteredPlugins.Clear();

                    // Populate plugin list
                    foreach (var plugin in pluginItems)
                    {
                        plugins.Add(plugin);
                    }
                }
                finally
                {
                    // Resume triggers a single ApplyFilter for all added plugins
                    viewModel.ResumeFilter();
                }

                // Add a standard informational message
                var pluginLabel = showAdvanced ? "plugins" : "non-base game plugins";
                viewModel.AddInformationMessage($"Loaded {loadedCount} {pluginLabel}");
            });
        }
        catch (Exception ex)
        {
            if (!IsLatestRefresh())
            {
                return;
            }

            // Update UI on UI thread in case of error
            await dispatcher.InvokeAsync(() =>
            {
                if (!IsLatestRefresh())
                {
                    return;
                }

                // Clear scanning state
                viewModel.IsScanning = false;
                viewModel.ProgressValue = 0;
                viewModel.ProgressStatus = string.Empty;

                // Clear both collections in case of error
                plugins.Clear();
                viewModel.FilteredPlugins.Clear();

                // Provide a clear error message
                viewModel.AddErrorMessage($"Failed to load plugins: {ex.Message}");
                viewModel.AddErrorMessage("Ensure you selected the correct game Data directory");
            });
        }
    }

    /// <summary>
    ///     Marks all plugins in the provided collection as selected by setting their IsSelected property to true.
    /// </summary>
    /// <param name="plugins">The collection of plugins to be updated with selection state.</param>
    public void SelectAll(ObservableCollection<PluginListItem> plugins)
    {
        foreach (var plugin in plugins)
        {
            plugin.IsSelected = true;
        }
    }

    /// <summary>
    ///     Deselects all plugins in the specified collection by setting their selection state to false.
    ///     This method ensures no plugins remain selected in the provided collection.
    /// </summary>
    /// <param name="plugins">The collection of plugins to modify, where each plugin's selection state will be cleared.</param>
    public void SelectNone(ObservableCollection<PluginListItem> plugins)
    {
        foreach (var plugin in plugins)
        {
            plugin.IsSelected = false;
        }
    }
}
