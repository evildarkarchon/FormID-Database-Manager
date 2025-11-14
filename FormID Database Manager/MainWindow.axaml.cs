using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;

namespace FormID_Database_Manager;

public partial class MainWindow : Window, IDisposable
{
    private readonly GameDetectionService _gameDetectionService;
    private readonly PluginListManager _pluginListManager;
    private readonly PluginProcessingService _pluginProcessingService;
    private readonly MainWindowViewModel _viewModel;
    private readonly WindowManager _windowManager;

    public MainWindow()
    {
        try
        {
            InitializeComponent();
        }
        catch (InvalidOperationException)
        {
            // InitializeComponent might fail in test scenarios
            // This is expected when running headless tests
        }

        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        // Only initialize services if StorageProvider is available (not in test mode)
        // In test scenarios, StorageProvider may be null
        if (StorageProvider != null)
        {
            _windowManager = new WindowManager(StorageProvider, _viewModel);
        }
        else
        {
            // Create a null WindowManager for test scenarios
            _windowManager = null!;
        }

        _gameDetectionService = new GameDetectionService();
        _pluginListManager = new PluginListManager(_gameDetectionService, _viewModel);
        var databaseService = new DatabaseService();
        _pluginProcessingService = new PluginProcessingService(databaseService, _viewModel);
    }

    public void Dispose()
    {
        // Ensure any ongoing processing is cancelled before disposal
        // This prevents resource leaks from uncanceled async operations
        _pluginProcessingService.CancelProcessing();

        // Give a brief moment for cancellation to propagate
        // This is a synchronous delay which is acceptable in Dispose
        Thread.Sleep(100);

        // Dispose managed resources
        if (_pluginProcessingService is IDisposable disposableService)
        {
            disposableService.Dispose();
        }
    }

    private async void SelectGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SelectGameDirectoryAsync();
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    private async Task SelectGameDirectoryAsync()
    {
        if (_windowManager == null)
        {
            return;
        }

        var path = await _windowManager.SelectGameDirectory();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _viewModel.GameDirectory = path;

        var detectedGame = _gameDetectionService.DetectGame(path);
        if (detectedGame.HasValue)
        {
            _viewModel.DetectedGame = detectedGame.ToString() ?? string.Empty;
            await _pluginListManager.RefreshPluginList(
                path,
                detectedGame.Value,
                _viewModel.Plugins,
                AdvancedModeCheckBox.IsChecked ?? false);
        }
        else
        {
            _viewModel.AddErrorMessage(
                "Could not detect game from directory. Please ensure this is a valid game Data directory.");
        }
    }

    private async void OnSelectDatabase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SelectDatabaseAsync();
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    private async Task SelectDatabaseAsync()
    {
        if (_windowManager == null)
        {
            return;
        }

        var path = await _windowManager.SelectDatabaseFile();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _viewModel.DatabasePath = path;
    }

    private async void OnSelectFormIdList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SelectFormIdListAsync();
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    private async Task SelectFormIdListAsync()
    {
        if (_windowManager == null)
        {
            return;
        }

        var path = await _windowManager.SelectFormIdListFile();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _viewModel.FormIdListPath = path;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _pluginListManager.SelectAll(_viewModel.Plugins);
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        _pluginListManager.SelectNone(_viewModel.Plugins);
    }

    private async void AdvancedMode_CheckedChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            await AdvancedModeChangedAsync();
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    private async Task AdvancedModeChangedAsync()
    {
        if (!string.IsNullOrEmpty(_viewModel.GameDirectory) && !string.IsNullOrEmpty(_viewModel.DetectedGame))
        {
            if (Enum.TryParse<GameRelease>(_viewModel.DetectedGame, out var gameRelease))
            {
                await _pluginListManager.RefreshPluginList(
                    _viewModel.GameDirectory,
                    gameRelease,
                    _viewModel.Plugins,
                    AdvancedModeCheckBox.IsChecked ?? false);
            }
            else
            {
                _viewModel.AddErrorMessage($"Invalid game release: {_viewModel.DetectedGame}");
            }
        }
    }

    private async void ProcessFormIds_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ProcessFormIdsAsync(sender as Button);
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    private async Task ProcessFormIdsAsync(Button? processButton)
    {
        if (_viewModel.IsProcessing)
        {
            _viewModel.ProgressStatus = "Cancelling...";
            _pluginProcessingService.CancelProcessing();
            return;
        }

        if (processButton != null)
        {
            processButton.Content = "Cancel Processing";
        }

        _viewModel.IsProcessing = true;
        _viewModel.ProgressValue = 0;
        _viewModel.ProgressStatus = "Initializing...";
        _viewModel.ErrorMessages.Clear();

        try
        {
            // Validate game type is selected
            if (string.IsNullOrEmpty(_viewModel.DetectedGame))
            {
                _viewModel.AddErrorMessage("Game type must be specified. Please select a game directory first.");
                return;
            }

            if (!Enum.TryParse<GameRelease>(_viewModel.DetectedGame, out var gameRelease))
            {
                _viewModel.AddErrorMessage($"Invalid game release: {_viewModel.DetectedGame}");
                return;
            }

            var parameters = new ProcessingParameters
            {
                GameDirectory = _viewModel.GameDirectory,
                DatabasePath = _viewModel.DatabasePath,
                GameRelease = gameRelease,
                SelectedPlugins = _viewModel.Plugins.Where(p => p.IsSelected).ToList(),
                UpdateMode = UpdateModeCheckBox.IsChecked ?? false,
                FormIdListPath = _viewModel.FormIdListPath
            };

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

            // Set database path if not specified
            if (string.IsNullOrEmpty(parameters.DatabasePath))
            {
                parameters.DatabasePath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    $"{_viewModel.DetectedGame}.db");
                _viewModel.DatabasePath = parameters.DatabasePath;
            }

            var progress = new Progress<(string Message, double? Value)>(update =>
            {
                // Progress<T> automatically marshals to UI thread
                _viewModel.UpdateProgress(update.Message, update.Value);
            });

            // Run the processing in a background thread
            await Task.Run(async () =>
            {
                await _pluginProcessingService.ProcessPlugins(parameters, progress).ConfigureAwait(false);
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _viewModel.ProgressStatus = "Processing cancelled by user.";
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Error processing FormIDs: {ex.Message}");
        }
        finally
        {
            _viewModel.IsProcessing = false;
            _viewModel.ProgressStatus = string.Empty;
            if (processButton != null)
            {
                processButton.Content = "Process FormIDs";
            }
        }
    }
}
