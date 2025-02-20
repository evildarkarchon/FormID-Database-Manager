using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Manages the list of plugins for a game environment, including operations
/// such as loading plugin data, refreshing the displayed list, and managing
/// plugin selection states.
/// </summary>
public class PluginListManager(GameDetectionService gameDetectionService, MainWindowViewModel viewModel)
{
    /// <summary>
    /// Refreshes the list of plugins by loading data from the specified game directory and game release.
    /// Clears the existing plugin lists, adds the updated plugin list, and updates the filtered plugin list.
    /// Displays messages for the plugin load process and handles errors if the operation fails.
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
        try
        {
            // Prepare environment
            var env = GameEnvironment.Typical.Construct(gameRelease);
            var loadOrder = env.LoadOrder.ListedOrder;
            var basePlugins = gameDetectionService.GetBaseGamePlugins(gameRelease);

            // Determine the data path
            var dataPath = Path.GetFileName(gameDirectory).Equals("Data", StringComparison.OrdinalIgnoreCase)
                ? gameDirectory
                : Path.Combine(gameDirectory, "Data");

            // Clear plugin lists
            plugins.Clear();
            viewModel.FilteredPlugins.Clear();
            var nonBasePluginCount = 0;

            // Process plugins asynchronously using Task.Run for file checks
            foreach (var plugin in loadOrder)
            {
                var pluginFileName = plugin.ModKey.FileName;

                // Skip base plugins if not in advanced mode
                if (!showAdvanced && basePlugins.Contains(pluginFileName))
                    continue;

                // Check if the plugin file exists asynchronously
                var pluginPath = Path.Combine(dataPath, pluginFileName);
                if (!await Task.Run(() => File.Exists(pluginPath)))
                    continue;

                // Add the plugin to the list
                plugins.Add(new PluginListItem
                {
                    Name = pluginFileName,
                    IsSelected = false
                });
                nonBasePluginCount++;
            }

            // Populate the filtered plugins list (UI-bound operation)
            foreach (var plugin in plugins)
            {
                viewModel.FilteredPlugins.Add(plugin);
            }

            // Add a standard informational message
            viewModel.AddInformationMessage($"Loaded {nonBasePluginCount} non-base game plugins");
        }
        catch (Exception ex)
        {
            // Clear both collections in case of error
            plugins.Clear();
            viewModel.FilteredPlugins.Clear();

            // Provide a clear error message
            viewModel.AddErrorMessage($"Failed to load plugins: {ex.Message}");
            viewModel.AddErrorMessage("Ensure you selected the correct game Data directory");
        }
    }

    /// <summary>
    /// Marks all plugins in the provided collection as selected by setting their IsSelected property to true.
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
    /// Deselects all plugins in the specified collection by setting their selection state to false.
    /// This method ensures no plugins remain selected in the provided collection.
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