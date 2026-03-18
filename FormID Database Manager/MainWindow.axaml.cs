using System;
using System.Diagnostics.CodeAnalysis;
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
    private readonly IGameLocationService _gameLocationService;
    private readonly PluginListManager _pluginListManager;
    private readonly PluginProcessingService _pluginProcessingService;
    private readonly MainWindowViewModel _viewModel;
    private readonly WindowManager _windowManager;
    private int _gameSelectionVersion;
    private bool _suppressDirectorySelectionChanged;
    private bool _suppressGameSelectionChanged;

    public MainWindow() : this(null, null, null, null, null)
    {
    }

    internal MainWindow(
        MainWindowViewModel? viewModel,
        GameDetectionService? gameDetectionService,
        IGameLocationService? gameLocationService,
        PluginListManager? pluginListManager,
        PluginProcessingService? pluginProcessingService)
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

        _viewModel = viewModel ?? new MainWindowViewModel();
        DataContext = _viewModel;

        _windowManager = new WindowManager(StorageProvider, _viewModel);

        _gameDetectionService = gameDetectionService ?? new GameDetectionService();
        _gameLocationService = gameLocationService ?? new GameLocationService();
        _pluginListManager = pluginListManager ??
                             new PluginListManager(_gameDetectionService, _viewModel, new AvaloniaThreadDispatcher());
        var databaseService = new DatabaseService();
        _pluginProcessingService = pluginProcessingService ??
                                   new PluginProcessingService(databaseService, _viewModel, new AvaloniaThreadDispatcher());

        this.Closed += (_, _) => Dispose();
    }

    public void Dispose()
    {
        // Signal cancellation for any ongoing processing
        // The cancellation token will interrupt async operations
        _pluginProcessingService.CancelProcessing();

        // Dispose managed resources
        if (_pluginProcessingService is IDisposable disposableService)
        {
            disposableService.Dispose();
        }

        _viewModel.Dispose();
    }

    private async void GameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGameSelectionChanged)
        {
            return;
        }

        try
        {
            await OnGameSelectedAsync();
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    private async Task OnGameSelectedAsync()
    {
        if (_viewModel.SelectedGame is not { } selectedGame)
        {
            return;
        }

        var gameSelectionVersion = Interlocked.Increment(ref _gameSelectionVersion);

        ResetGameSelectionState();

        // Run GameLocations lookup off UI thread (does registry + file system I/O)
        var folders = await Task.Run(() => _gameLocationService.GetGameFolders(selectedGame));

        if (!IsLatestGameSelection(gameSelectionVersion))
        {
            return;
        }

        if (folders.Count == 0)
        {
            _viewModel.AddInformationMessage(
                $"No installed locations found for {selectedGame}. Use Browse to select a directory.");
        }
        else
        {
            ApplyDetectedFolders(folders);
            await LoadPluginsForCurrentSelection();
        }
    }

    private async void DirectoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDirectorySelectionChanged)
        {
            return;
        }

        try
        {
            await LoadPluginsForCurrentSelection();
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    private async void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await BrowseDirectoryAsync();
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    private async Task BrowseDirectoryAsync()
    {
        var path = await _windowManager.SelectGameDirectory();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        Interlocked.Increment(ref _gameSelectionVersion);
        SetGameDirectory(path);

        // Auto-detect game from directory if no game is selected yet
        if (_viewModel.SelectedGame is null)
        {
            var detectedGame = _gameDetectionService.DetectGame(path);
            if (detectedGame.HasValue)
            {
                // Suppress the SelectionChanged event to prevent re-running location detection
                _suppressGameSelectionChanged = true;
                try
                {
                    _viewModel.SelectedGame = detectedGame.Value;
                }
                finally
                {
                    _suppressGameSelectionChanged = false;
                }
            }
            else
            {
                _viewModel.AddErrorMessage(
                    "Could not detect game from directory. Please select a game from the dropdown.");
                return;
            }
        }

        await LoadPluginsForCurrentSelection();
    }

    private async Task LoadPluginsForCurrentSelection()
    {
        if (_viewModel.SelectedGame is not { } game || string.IsNullOrEmpty(_viewModel.GameDirectory))
        {
            return;
        }

        await _pluginListManager.RefreshPluginList(
            _viewModel.GameDirectory,
            game,
            _viewModel.Plugins,
            AdvancedModeCheckBox.IsChecked ?? false);
    }

    private bool IsLatestGameSelection(int gameSelectionVersion)
    {
        return gameSelectionVersion == Volatile.Read(ref _gameSelectionVersion);
    }

    private void ResetGameSelectionState()
    {
        RunWithDirectorySelectionSuppressed(() =>
        {
            _viewModel.GameDirectory = string.Empty;
            _viewModel.DetectedDirectories.Clear();
        });

        _viewModel.Plugins.Clear();
    }

    private void ApplyDetectedFolders(System.Collections.Generic.IReadOnlyList<string> folders)
    {
        RunWithDirectorySelectionSuppressed(() =>
        {
            _viewModel.DetectedDirectories.Clear();

            if (folders.Count > 1)
            {
                foreach (var folder in folders)
                {
                    _viewModel.DetectedDirectories.Add(folder);
                }
            }

            _viewModel.GameDirectory = folders[0];
        });
    }

    private void SetGameDirectory(string path)
    {
        RunWithDirectorySelectionSuppressed(() => _viewModel.GameDirectory = path);
    }

    private void RunWithDirectorySelectionSuppressed(Action action)
    {
        _suppressDirectorySelectionChanged = true;
        try
        {
            action();
        }
        finally
        {
            _suppressDirectorySelectionChanged = false;
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
        await LoadPluginsForCurrentSelection();
    }

    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records.")]
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

    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records.")]
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
            if (_viewModel.SelectedGame is not { } gameRelease)
            {
                _viewModel.AddErrorMessage("Please select a game from the dropdown first.");
                return;
            }

            var parameters = new ProcessingParameters
            {
                GameDirectory = _viewModel.GameDirectory,
                DatabasePath = _viewModel.DatabasePath,
                GameRelease = gameRelease,
                SelectedPlugins = _viewModel.GetSelectedPlugins(),
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
                    $"{GameReleaseHelper.GetSafeTableName(gameRelease)}.db");
                _viewModel.DatabasePath = parameters.DatabasePath;
            }

            var progress = new Progress<(string Message, double? Value)>(update =>
            {
                // Progress<T> automatically marshals to UI thread
                _viewModel.UpdateProgress(update.Message, update.Value);
            });

            await _pluginProcessingService.ProcessPlugins(parameters, progress);
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
