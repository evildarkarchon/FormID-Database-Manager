using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using FormID_Database_Manager.Models;
using Mutagen.Bethesda;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FormID_Database_Manager;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly WindowManager _windowManager;
    private readonly GameDetectionService _gameDetectionService;
    private readonly PluginListManager _pluginListManager;
    private readonly DatabaseService _databaseService;
    private readonly PluginProcessingService _pluginProcessingService;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        _windowManager = new WindowManager(StorageProvider, _viewModel);
        _gameDetectionService = new GameDetectionService();
        _pluginListManager = new PluginListManager(_gameDetectionService, _viewModel);
        _databaseService = new DatabaseService();
        _pluginProcessingService = new PluginProcessingService(_databaseService, _viewModel);
    }

    private async void SelectGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        var path = await _windowManager.SelectGameDirectory();
        if (string.IsNullOrEmpty(path)) return;

        _viewModel.GameDirectory = path;
        GameDirectoryTextBox.Text = path;

        var detectedGame = await _gameDetectionService.DetectGame(path);
        if (detectedGame.HasValue)
        {
            _viewModel.DetectedGame = detectedGame.ToString();
            GameReleaseTextBlock.Text = detectedGame.ToString();
            await _pluginListManager.RefreshPluginList(
                path,
                detectedGame.Value,
                _viewModel.Plugins,
                AdvancedModeCheckBox.IsChecked ?? false);

            PluginList.ItemsSource = null;
            PluginList.ItemsSource = _viewModel.Plugins;
        }
        else
        {
            _viewModel.AddErrorMessage(
                "Could not detect game from directory. Please ensure this is a valid game Data directory.");
        }
    }

    private async void OnSelectDatabase_Click(object sender, RoutedEventArgs e)
    {
        var path = await _windowManager.SelectDatabaseFile();
        if (string.IsNullOrEmpty(path)) return;

        _viewModel.DatabasePath = path;
        DatabasePathTextBox.Text = path;
    }

    private async void OnSelectFormIdList_Click(object sender, RoutedEventArgs e)
    {
        var path = await _windowManager.SelectFormIdListFile();
        if (string.IsNullOrEmpty(path)) return;

        _viewModel.FormIdListPath = path;
        FormIdListPathTextBox.Text = path;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _pluginListManager.SelectAll(_viewModel.Plugins);
        PluginList.ItemsSource = null;
        PluginList.ItemsSource = _viewModel.Plugins;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        _pluginListManager.SelectNone(_viewModel.Plugins);
        PluginList.ItemsSource = null;
        PluginList.ItemsSource = _viewModel.Plugins;
    }

    private async void AdvancedMode_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_viewModel.GameDirectory) && !string.IsNullOrEmpty(_viewModel.DetectedGame))
        {
            await _pluginListManager.RefreshPluginList(
                _viewModel.GameDirectory,
                Enum.Parse<GameRelease>(_viewModel.DetectedGame),
                _viewModel.Plugins,
                AdvancedModeCheckBox.IsChecked ?? false);

            PluginList.ItemsSource = null;
            PluginList.ItemsSource = _viewModel.Plugins;
        }
    }

    private async void ProcessFormIds_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsProcessing)
        {
            _viewModel.ProgressStatus = "Cancelling...";
            _pluginProcessingService.CancelProcessing();
            return;
        }

        var processButton = (Button)sender;

        try
        {
            // Get all UI values on the UI thread
            var parameters = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                processButton.Content = "Cancel Processing";
                _viewModel.IsProcessing = true;
                _viewModel.ProgressValue = 0;
                _viewModel.ProgressStatus = "Initializing...";
                _viewModel.ErrorMessages.Clear(); // Clear previous error messages

                return new ProcessingParameters
                {
                    GameDirectory = _viewModel.GameDirectory,
                    DatabasePath = _viewModel.DatabasePath,
                    GameRelease = Enum.Parse<GameRelease>(_viewModel.DetectedGame),
                    SelectedPlugins = _viewModel.Plugins.Where(p => p.IsSelected).ToList(),
                    UpdateMode = UpdateModeCheckBox.IsChecked ?? false,
                    FormIdListPath = _viewModel.FormIdListPath
                };
            });

            // Validate parameters
            var usingTextFile = !string.IsNullOrEmpty(parameters.FormIdListPath);

            if (!usingTextFile && string.IsNullOrEmpty(parameters.GameDirectory))
            {
                _viewModel.AddErrorMessage("Game directory must be specified when processing plugins");
                return;
            }

            if (!usingTextFile && !parameters.SelectedPlugins.Any())
            {
                _viewModel.AddErrorMessage("No plugins selected");
                return;
            }

            // Validate game type is selected
            if (string.IsNullOrEmpty(_viewModel.DetectedGame))
            {
                _viewModel.AddErrorMessage("Game type must be specified. Please select a game directory first.");
                return;
            }

            // Set database path if not specified
            if (string.IsNullOrEmpty(parameters.DatabasePath))
            {
                parameters.DatabasePath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    $"{_viewModel.DetectedGame}.db");

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _viewModel.DatabasePath = parameters.DatabasePath;
                    DatabasePathTextBox.Text = parameters.DatabasePath;
                });
            }

            var progress = new Progress<(string Message, double? Value)>(update =>
            {
                _viewModel.ProgressStatus = update.Message;
                if (update.Value.HasValue)
                {
                    _viewModel.ProgressValue = update.Value.Value;
                }
            });

            try
            {
                // Run the processing in a background thread
                await Task.Run(async () => { await _pluginProcessingService.ProcessPlugins(parameters, progress); });
            }
            catch (OperationCanceledException)
            {
                _viewModel.ProgressStatus = "Processing cancelled by user.";
            }
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Error processing FormIDs: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _viewModel.IsProcessing = false;
                _viewModel.ProgressStatus = string.Empty;
                processButton.Content = "Process FormIDs";
            });
        }
    }
}