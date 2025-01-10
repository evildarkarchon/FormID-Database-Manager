using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;

namespace FormID_Database_Manager.Services;

public class PluginListManager
{
    private readonly GameDetectionService _gameDetectionService;
    private readonly MainWindowViewModel _viewModel;

    public PluginListManager(GameDetectionService gameDetectionService, MainWindowViewModel viewModel)
    {
        _gameDetectionService = gameDetectionService;
        _viewModel = viewModel;
    }

    public async Task RefreshPluginList(
        string gameDirectory,
        GameRelease gameRelease,
        ObservableCollection<PluginListItem> plugins,
        bool showAdvanced)
    {
        try
        {
            var env = GameEnvironment.Typical.Construct(gameRelease);
            var loadOrder = env.LoadOrder.ListedOrder;
            var basePlugins = _gameDetectionService.GetBaseGamePlugins(gameRelease);

            // Determine the data path
            var dataPath = Path.GetFileName(gameDirectory).Equals("Data", StringComparison.OrdinalIgnoreCase)
                ? gameDirectory
                : Path.Combine(gameDirectory, "Data");

            plugins.Clear();
            int nonBasePluginCount = 0;
            foreach (var plugin in loadOrder)
            {
                var pluginFileName = plugin.ModKey.FileName;

                // Skip base plugins if not in advanced mode
                if (!showAdvanced && basePlugins.Contains(pluginFileName))
                    continue;

                // Check if the plugin file actually exists in the data directory
                var pluginPath = Path.Combine(dataPath, pluginFileName);
                if (!File.Exists(pluginPath))
                    continue;

                plugins.Add(new PluginListItem
                {
                    Name = pluginFileName,
                    IsSelected = false
                });
                nonBasePluginCount++;
            }

            // Add a standard informational message instead of an error
            _viewModel.AddInformationMessage($"Loaded {nonBasePluginCount} non-base game plugins");
        }
        catch (Exception ex)
        {
            // Clear the plugins list in case of error
            plugins.Clear();

            // Provide a clear error message
            _viewModel.AddErrorMessage($"Failed to load plugins: {ex.Message}");
            _viewModel.AddErrorMessage("Ensure you selected the correct game Data directory");
        }
    }

    public void SelectAll(ObservableCollection<PluginListItem> plugins)
    {
        foreach (var plugin in plugins)
        {
            plugin.IsSelected = true;
        }
    }

    public void SelectNone(ObservableCollection<PluginListItem> plugins)
    {
        foreach (var plugin in plugins)
        {
            plugin.IsSelected = false;
        }
    }
}