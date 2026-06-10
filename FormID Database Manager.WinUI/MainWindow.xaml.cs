using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using FormID_Database_Manager.WinUI.Services;

namespace FormID_Database_Manager.WinUI;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly IFileDialogService _fileDialogService;
    private readonly GameDetectionService _gameDetectionService;
    private readonly IGameLocationService _gameLocationService;
    private readonly PluginListManager _pluginListManager;
    private readonly PluginProcessingService _pluginProcessingService;
    private int _gameSelectionVersion;
    private bool _disposed;
    private bool _suppressDirectorySelectionChanged;
    private bool _suppressGameSelectionChanged;

    /// <summary>
    /// Initializes the WinUI main window with production platform services.
    /// </summary>
    public MainWindow()
    {
        var dispatcher = new WinUiThreadDispatcher(DispatcherQueue);
        ViewModel = new MainWindowViewModel(dispatcher);
        _gameDetectionService = new GameDetectionService();
        _gameLocationService = new GameLocationService();
        _pluginListManager = new PluginListManager(_gameDetectionService, ViewModel, dispatcher);
        _pluginProcessingService = new PluginProcessingService(new DatabaseService(), ViewModel, dispatcher);

        InitializeWindow();
        _fileDialogService = new WinUiFileDialogService(AppWindow, ViewModel);
    }

    /// <summary>
    /// Initializes the WinUI main window with supplied services for migration smoke tests.
    /// </summary>
    /// <param name="viewModel">The UI-neutral state object shared with the migration core.</param>
    /// <param name="fileDialogService">The picker service used by browse and file-selection handlers.</param>
    /// <param name="gameDetectionService">The service used to detect a game from a browsed directory.</param>
    /// <param name="gameLocationService">The service used to find installed game folders.</param>
    /// <param name="pluginListManager">The service used to load and select plugins.</param>
    /// <param name="pluginProcessingService">The owned processing service canceled during window close.</param>
    internal MainWindow(
        MainWindowViewModel viewModel,
        IFileDialogService? fileDialogService,
        GameDetectionService? gameDetectionService,
        IGameLocationService? gameLocationService,
        PluginListManager? pluginListManager,
        PluginProcessingService? pluginProcessingService)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        var dispatcher = new WinUiThreadDispatcher(DispatcherQueue);
        _gameDetectionService = gameDetectionService ?? new GameDetectionService();
        _gameLocationService = gameLocationService ?? new GameLocationService();
        _pluginListManager = pluginListManager ?? new PluginListManager(_gameDetectionService, ViewModel, dispatcher);
        _pluginProcessingService = pluginProcessingService ?? new PluginProcessingService(
            new DatabaseService(),
            ViewModel,
            dispatcher);

        InitializeWindow();
        _fileDialogService = fileDialogService ?? new WinUiFileDialogService(AppWindow, ViewModel);
    }

    /// <summary>
    /// Initializes XAML, assigns the root ViewModel, and attaches close-time cleanup.
    /// </summary>
    private void InitializeWindow()
    {
        InitializeComponent();
        Root.DataContext = ViewModel;
        Closed += MainWindow_Closed;
    }

    public MainWindowViewModel ViewModel { get; }

    /// <summary>
    /// Cancels in-flight processing and releases services owned by this window.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Closed -= MainWindow_Closed;
        _pluginProcessingService.CancelProcessing();
        _pluginProcessingService.Dispose();
        ViewModel.Dispose();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    /// <summary>
    /// Handles game selection changes by loading installed directories and refreshing plugins.
    /// </summary>
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
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads installed directories for the selected game while ignoring stale lookup results.
    /// </summary>
    /// <returns>A task that completes after directory detection and plugin loading finish.</returns>
    private async Task OnGameSelectedAsync()
    {
        if (ViewModel.SelectedGame is not { } selectedGame)
        {
            return;
        }

        var gameSelectionVersion = Interlocked.Increment(ref _gameSelectionVersion);

        ResetGameSelectionState();

        // Mutagen game-location lookup touches registry and file system state, so keep it off the UI thread.
        var folders = await Task.Run(() => _gameLocationService.GetGameFolders(selectedGame));

        if (!IsLatestGameSelection(gameSelectionVersion))
        {
            return;
        }

        if (folders.Count == 0)
        {
            ViewModel.AddInformationMessage(
                $"No installed locations found for {selectedGame}. Use Browse to select a directory.");
        }
        else
        {
            ApplyDetectedFolders(folders);
            await LoadPluginsForCurrentSelection();
        }
    }

    /// <summary>
    /// Handles detected-directory changes by reloading plugins for the current selection.
    /// </summary>
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
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles Browse button clicks through the WinUI folder picker.
    /// </summary>
    private async void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await BrowseDirectoryAsync();
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Selects a game directory, auto-detects the game when needed, and refreshes plugins.
    /// </summary>
    /// <returns>A task that completes after picker handling and plugin refresh finish.</returns>
    private async Task BrowseDirectoryAsync()
    {
        var path = await _fileDialogService.SelectGameDirectory();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        Interlocked.Increment(ref _gameSelectionVersion);
        SetGameDirectory(path);

        if (ViewModel.SelectedGame is null)
        {
            var detectedGame = _gameDetectionService.DetectGame(path);
            if (detectedGame.HasValue)
            {
                // Suppress SelectionChanged so a browsed directory does not trigger a second installed-location scan.
                _suppressGameSelectionChanged = true;
                try
                {
                    ViewModel.SelectedGame = detectedGame.Value;
                }
                finally
                {
                    _suppressGameSelectionChanged = false;
                }
            }
            else
            {
                ViewModel.AddErrorMessage(
                    "Could not detect game from directory. Please select a game from the dropdown.");
                return;
            }
        }

        await LoadPluginsForCurrentSelection();
    }

    /// <summary>
    /// Refreshes the plugin list when both a game and directory are available.
    /// </summary>
    /// <returns>A task that completes when the plugin refresh is finished or skipped.</returns>
    private async Task LoadPluginsForCurrentSelection()
    {
        if (ViewModel.SelectedGame is not { } game || string.IsNullOrEmpty(ViewModel.GameDirectory))
        {
            return;
        }

        await _pluginListManager.RefreshPluginList(
            ViewModel.GameDirectory,
            game,
            ViewModel.Plugins,
            AdvancedModeCheckBox.IsChecked ?? false);
    }

    /// <summary>
    /// Determines whether an asynchronous game-directory lookup still matches the latest selection.
    /// </summary>
    /// <param name="gameSelectionVersion">The version captured when the lookup started.</param>
    /// <returns><see langword="true"/> when the lookup should still update UI state.</returns>
    private bool IsLatestGameSelection(int gameSelectionVersion)
    {
        return gameSelectionVersion == Volatile.Read(ref _gameSelectionVersion);
    }

    /// <summary>
    /// Clears directory and plugin state before applying a new game selection.
    /// </summary>
    private void ResetGameSelectionState()
    {
        RunWithDirectorySelectionSuppressed(() =>
        {
            ViewModel.GameDirectory = string.Empty;
            ViewModel.DetectedDirectories.Clear();
        });

        ViewModel.Plugins.Clear();
    }

    /// <summary>
    /// Applies installed game folders while suppressing duplicate selection-change reloads.
    /// </summary>
    /// <param name="folders">The installed folders found for the selected game.</param>
    private void ApplyDetectedFolders(IReadOnlyList<string> folders)
    {
        RunWithDirectorySelectionSuppressed(() =>
        {
            ViewModel.DetectedDirectories.Clear();

            if (folders.Count > 1)
            {
                foreach (var folder in folders)
                {
                    ViewModel.DetectedDirectories.Add(folder);
                }
            }

            ViewModel.GameDirectory = folders[0];
        });
    }

    /// <summary>
    /// Updates the selected game directory without triggering an immediate duplicate reload.
    /// </summary>
    /// <param name="path">The selected game directory path.</param>
    private void SetGameDirectory(string path)
    {
        RunWithDirectorySelectionSuppressed(() => ViewModel.GameDirectory = path);
    }

    /// <summary>
    /// Temporarily suppresses directory selection handling while the ViewModel is updated programmatically.
    /// </summary>
    /// <param name="action">The directory-state update to run.</param>
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

    /// <summary>
    /// Handles database picker button clicks.
    /// </summary>
    private async void OnSelectDatabase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SelectDatabaseAsync();
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Selects a database path without overwriting state when the picker is canceled.
    /// </summary>
    /// <returns>A task that completes after picker handling finishes.</returns>
    private async Task SelectDatabaseAsync()
    {
        var path = await _fileDialogService.SelectDatabaseFile();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        ViewModel.DatabasePath = path;
    }

    /// <summary>
    /// Handles FormID list picker button clicks.
    /// </summary>
    private async void OnSelectFormIdList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SelectFormIdListAsync();
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Selects an optional FormID text file without overwriting state when the picker is canceled.
    /// </summary>
    /// <returns>A task that completes after picker handling finishes.</returns>
    private async Task SelectFormIdListAsync()
    {
        var path = await _fileDialogService.SelectFormIdListFile();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        ViewModel.FormIdListPath = path;
    }

    /// <summary>
    /// Selects every currently loaded plugin.
    /// </summary>
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _pluginListManager.SelectAll(ViewModel.Plugins);
    }

    /// <summary>
    /// Clears selection for every currently loaded plugin.
    /// </summary>
    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        _pluginListManager.SelectNone(ViewModel.Plugins);
    }

    /// <summary>
    /// Reloads plugins when advanced-mode visibility changes.
    /// </summary>
    private async void AdvancedMode_CheckedChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoadPluginsForCurrentSelection();
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles process button clicks by starting processing or cancelling the active run.
    /// </summary>
    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records.")]
    private async void ProcessFormIds_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ProcessFormIdsAsync(sender as Button);
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates WinUI processing state, runs the shared processing service, and restores UI state afterward.
    /// </summary>
    /// <param name="processButton">The process button whose content should reflect start/cancel state.</param>
    /// <returns>A task that completes after processing starts, finishes, fails, or observes cancellation.</returns>
    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records.")]
    private async Task ProcessFormIdsAsync(Button? processButton)
    {
        if (ViewModel.IsProcessing)
        {
            ViewModel.ProgressStatus = "Cancelling...";
            _pluginProcessingService.CancelProcessing();
            return;
        }

        if (processButton != null)
        {
            processButton.Content = "Cancel Processing";
        }

        ViewModel.IsProcessing = true;
        ViewModel.ProgressValue = 0;
        ViewModel.ProgressStatus = "Initializing...";
        ViewModel.ErrorMessages.Clear();

        try
        {
            if (ViewModel.SelectedGame is not { } gameRelease)
            {
                ViewModel.AddErrorMessage("Please select a game from the dropdown first.");
                return;
            }

            var parameters = new ProcessingParameters
            {
                GameDirectory = ViewModel.GameDirectory,
                DatabasePath = ViewModel.DatabasePath,
                GameRelease = gameRelease,
                SelectedPlugins = ViewModel.GetSelectedPlugins(),
                UpdateMode = UpdateModeCheckBox.IsChecked ?? false,
                FormIdListPath = ViewModel.FormIdListPath
            };

            var usingTextFile = !string.IsNullOrEmpty(parameters.FormIdListPath);

            if (!usingTextFile && string.IsNullOrEmpty(parameters.GameDirectory))
            {
                ViewModel.AddErrorMessage("Game directory must be specified when processing plugins");
                return;
            }

            if (!usingTextFile && !parameters.SelectedPlugins.Any())
            {
                ViewModel.AddErrorMessage("No plugins selected");
                return;
            }

            if (string.IsNullOrEmpty(parameters.DatabasePath))
            {
                parameters.DatabasePath = DefaultDatabasePathProvider.CreateDefaultDatabasePath(gameRelease);
                ViewModel.DatabasePath = parameters.DatabasePath;
            }

            var progress = new Progress<(string Message, double? Value)>(update =>
            {
                ViewModel.UpdateProgress(update.Message, update.Value);
            });

            await _pluginProcessingService.ProcessPlugins(parameters, progress);
        }
        catch (OperationCanceledException)
        {
            ViewModel.ProgressStatus = "Processing cancelled by user.";
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Error processing FormIDs: {ex.Message}");
        }
        finally
        {
            ViewModel.IsProcessing = false;
            ViewModel.ProgressStatus = string.Empty;
            if (processButton != null)
            {
                processButton.Content = "Process FormIDs";
            }
        }
    }
}
