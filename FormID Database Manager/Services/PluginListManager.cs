using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Manages the list of plugins for a game environment, including operations
///     such as loading plugin data, refreshing the displayed list, and managing
///     plugin selection states.
/// </summary>
public class PluginListManager(
    GameDetectionService gameDetectionService,
    MainWindowViewModel viewModel,
    IThreadDispatcher dispatcher)
{
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
        try
        {
            // Run expensive I/O operations on background thread to avoid UI freeze
            // For 1000+ plugins on HDD, this prevents 1-10 second UI freeze
            var (pluginItems, nonBaseCount) = await Task.Run(() =>
            {
                // Determine the data path
                var dataPath = Path.GetFileName(gameDirectory).Equals("Data", StringComparison.OrdinalIgnoreCase)
                    ? gameDirectory
                    : Path.Combine(gameDirectory, "Data");

                // Prepare environment scoped to the target directory
                var env = GameEnvironment.Typical.Builder(gameRelease)
                    .WithTargetDataFolder(dataPath)
                    .Build();

                var loadOrder = env.LoadOrder.ListedOrder;
                var basePlugins = gameDetectionService.GetBaseGamePlugins(gameRelease);

                var items = new List<PluginListItem>();
                var addedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var count = 0;

                // Process plugins - File.Exists calls now on background thread
                foreach (var plugin in loadOrder)
                {
                    var pluginFileName = plugin.ModKey.FileName;

                    // Deduplication check
                    if (addedPlugins.Contains(pluginFileName))
                    {
                        continue;
                    }

                    // Skip base plugins if not in advanced mode
                    if (!showAdvanced && basePlugins.Contains(pluginFileName))
                    {
                        continue;
                    }

                    // Check if the plugin file exists (synchronous I/O now safe on background thread)
                    var pluginPath = Path.Combine(dataPath, pluginFileName);
                    if (!File.Exists(pluginPath))
                    {
                        continue;
                    }

                    // Skip empty (header-only) plugins with no FormIDs
                    if (!HasRecords(pluginPath, gameRelease))
                    {
                        continue;
                    }

                    // Add the plugin to the list
                    items.Add(new PluginListItem { Name = pluginFileName, IsSelected = false });
                    addedPlugins.Add(pluginFileName);
                    count++;
                }

                return (items, count);
            }).ConfigureAwait(false);

            // Update UI on UI thread
            await dispatcher.InvokeAsync(() =>
            {
                // Clear plugin lists
                plugins.Clear();
                viewModel.FilteredPlugins.Clear();

                // Populate plugin list - FilteredPlugins is auto-synced via CollectionChanged -> ApplyFilter()
                foreach (var plugin in pluginItems)
                {
                    plugins.Add(plugin);
                }

                // Add a standard informational message
                viewModel.AddInformationMessage($"Loaded {nonBaseCount} non-base game plugins");
            });
        }
        catch (Exception ex)
        {
            // Update UI on UI thread in case of error
            await dispatcher.InvokeAsync(() =>
            {
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

    /// <summary>
    ///     Checks whether a plugin file contains any major records (FormIDs).
    ///     Used to filter out header-only plugins that would clutter the plugin list.
    ///     Returns true on any error (fail-open) so unreadable plugins still appear.
    /// </summary>
    /// <param name="pluginPath">Full path to the plugin file.</param>
    /// <param name="gameRelease">The game release to parse the plugin as.</param>
    /// <returns>True if the plugin has records or cannot be read; false if header-only.</returns>
    internal static bool HasRecords(string pluginPath, GameRelease gameRelease)
    {
        try
        {
            IModGetter? mod = gameRelease switch
            {
                GameRelease.Oblivion => OblivionMod.CreateFromBinaryOverlay(pluginPath,
                    OblivionRelease.Oblivion),
                GameRelease.SkyrimSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                    SkyrimRelease.SkyrimSE),
                GameRelease.SkyrimSEGog => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                    SkyrimRelease.SkyrimSEGog),
                GameRelease.SkyrimVR => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                    SkyrimRelease.SkyrimVR),
                GameRelease.Fallout4 => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                    Fallout4Release.Fallout4),
                GameRelease.Starfield => StarfieldMod.CreateFromBinaryOverlay(pluginPath,
                    StarfieldRelease.Starfield),
                _ => null
            };

            if (mod == null)
            {
                return true; // Unsupported game - fail-open
            }

            return mod.EnumerateMajorRecords().Any();
        }
        catch
        {
            return true; // Fail-open: show plugin if we can't read it
        }
    }
}
