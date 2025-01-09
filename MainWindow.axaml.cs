using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using FormID_Database_Manager.Models;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FormID_Database_Manager;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly GameDetectionService _gameDetectionService;
    private readonly DatabaseService _databaseService;
    private readonly PluginProcessingService _pluginProcessingService;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        _gameDetectionService = new GameDetectionService();
        _databaseService = new DatabaseService();
        _pluginProcessingService = new PluginProcessingService(_databaseService, Log);
    }

    private void Log(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            LogTextBox.Text += $"{message}\n";
            LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0;
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogTextBox.Text += $"{message}\n";
                LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0;
            });
        }
    }

    private async void SelectGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        await HandleSelectGameDirectory();
    }

    private async Task HandleSelectGameDirectory()
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Game Directory",
                AllowMultiple = false
            });

            if (!folders.Any()) return;

            var path = folders[0].Path.LocalPath;
            _viewModel.GameDirectory = path;
            GameDirectoryTextBox.Text = path;

            var detectedGame = await _gameDetectionService.DetectGame(path);
            if (detectedGame.HasValue)
            {
                _viewModel.DetectedGame = detectedGame.ToString();
                GameReleaseTextBlock.Text = detectedGame.ToString();
                await RefreshPluginList(detectedGame.Value);
            }
            else
            {
                Log("Error: Could not detect game from directory. Please ensure this is a valid game Data directory.");
            }
        }
        catch (Exception ex)
        {
            Log($"Error selecting game directory: {ex.Message}");
        }
    }

    private async void OnSelectDatabase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var fileTypeChoices = new List<FilePickerFileType>
            {
                new("Database Files")
                {
                    Patterns = new[] { "*.db" }
                }
            };

            var options = new FilePickerSaveOptions
            {
                Title = "Select Database Location",
                DefaultExtension = "db",
                SuggestedFileName = "FormIDs.db",
                FileTypeChoices = fileTypeChoices
            };

            var file = await StorageProvider.SaveFilePickerAsync(options);

            if (file == null) return;

            _viewModel.DatabasePath = file.Path.LocalPath;
            DatabasePathTextBox.Text = file.Path.LocalPath;
        }
        catch (Exception ex)
        {
            Log($"Error selecting database: {ex.Message}");
        }
    }

    private async Task RefreshPluginList(GameRelease gameRelease)
    {
        try
        {
            var env = GameEnvironment.Typical.Construct(gameRelease);
            var loadOrder = env.LoadOrder.ListedOrder;
            var basePlugins = _gameDetectionService.GetBaseGamePlugins(gameRelease);
            var showAdvanced = AdvancedModeCheckBox.IsChecked ?? false;

            _viewModel.Plugins.Clear();
            foreach (var plugin in loadOrder)
            {
                if (!showAdvanced && basePlugins.Contains(plugin.ModKey.FileName))
                    continue;

                _viewModel.Plugins.Add(new PluginListItem
                {
                    Name = plugin.ModKey.FileName,
                    IsSelected = false
                });
            }

            PluginList.ItemsSource = null;
            PluginList.ItemsSource = _viewModel.Plugins;
            Log($"Found {_viewModel.Plugins.Count} plugins");
        }
        catch (Exception ex)
        {
            Log($"Error loading plugin list: {ex.Message}");
            Log("Make sure you selected the game's Data directory (e.g., 'Skyrim Special Edition\\Data')");
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var plugin in _viewModel.Plugins)
        {
            plugin.IsSelected = true;
        }

        PluginList.ItemsSource = null;
        PluginList.ItemsSource = _viewModel.Plugins;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var plugin in _viewModel.Plugins)
        {
            plugin.IsSelected = false;
        }

        PluginList.ItemsSource = null;
        PluginList.ItemsSource = _viewModel.Plugins;
    }

    private async void AdvancedMode_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_viewModel.GameDirectory) && !string.IsNullOrEmpty(_viewModel.DetectedGame))
        {
            await RefreshPluginList(Enum.Parse<GameRelease>(_viewModel.DetectedGame));
        }
    }

    private async void ProcessFormIds_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsProcessing)
        {
            Log("Already processing...");
            return;
        }

        try
        {
            var processButton = (Button)sender;
            processButton.Content = "Cancel Processing";
            _viewModel.IsProcessing = true;

            var selectedPlugins = _viewModel.Plugins.Where(p => p.IsSelected).ToList();
            if (!selectedPlugins.Any())
            {
                Log("No plugins selected");
                return;
            }

            var dbPath = _viewModel.DatabasePath;
            if (string.IsNullOrEmpty(dbPath))
            {
                dbPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    $"{_viewModel.DetectedGame}.db");
                _viewModel.DatabasePath = dbPath;
            }

            var progress = new Progress<string>(message => Log(message));

            try
            {
                await _pluginProcessingService.ProcessPlugins(
                    _viewModel.GameDirectory,
                    dbPath,
                    Enum.Parse<GameRelease>(_viewModel.DetectedGame),
                    selectedPlugins,
                    UpdateModeCheckBox.IsChecked ?? false,
                    VerboseCheckBox.IsChecked ?? false,
                    DryRunCheckBox.IsChecked ?? false,
                    progress);
            }
            catch (OperationCanceledException)
            {
                Log("Processing cancelled by user.");
            }
        }
        catch (Exception ex)
        {
            Log($"Error processing FormIDs: {ex.Message}");
        }
        finally
        {
            _viewModel.IsProcessing = false;
            var processButton = (Button)sender;
            processButton.Content = "Process FormIDs";
        }
    }
}