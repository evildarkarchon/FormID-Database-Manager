using System.Diagnostics.CodeAnalysis;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Owns the UI-neutral user workflow for selecting game inputs, refreshing plugins, and starting processing.
/// </summary>
public sealed class UserWorkflow : IDisposable
{
    private readonly IFileDialogService _fileDialogService;
    private readonly GameDetectionService _gameDetectionService;
    private readonly IGameLocationService _gameLocationService;
    private readonly PluginListManager _pluginListManager;
    private readonly ProcessingRun _processingRun;
    private readonly MainWindowViewModel _viewModel;
    private bool _disposed;
    private int _gameSelectionVersion;
    private string? _programmaticDirectorySelectionToIgnore;
    private GameRelease? _programmaticGameSelectionToIgnore;

    /// <summary>
    /// Creates a workflow module that coordinates existing Core modules behind one UI-neutral interface.
    /// </summary>
    /// <param name="viewModel">The binding-state projection updated by workflow transitions.</param>
    /// <param name="fileDialogService">The platform picker adapter.</param>
    /// <param name="gameDetectionService">The game detection module.</param>
    /// <param name="gameLocationService">The installed-location lookup adapter.</param>
    /// <param name="pluginListManager">The plugin list module.</param>
    /// <param name="processingRun">The Processing Run module.</param>
    public UserWorkflow(
        MainWindowViewModel viewModel,
        IFileDialogService fileDialogService,
        GameDetectionService gameDetectionService,
        IGameLocationService gameLocationService,
        PluginListManager pluginListManager,
        ProcessingRun processingRun)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _gameDetectionService = gameDetectionService ?? throw new ArgumentNullException(nameof(gameDetectionService));
        _gameLocationService = gameLocationService ?? throw new ArgumentNullException(nameof(gameLocationService));
        _pluginListManager = pluginListManager ?? throw new ArgumentNullException(nameof(pluginListManager));
        _processingRun = processingRun ?? throw new ArgumentNullException(nameof(processingRun));
    }

    /// <summary>
    /// Handles game selection by clearing stale state, loading installed folders, and refreshing plugins.
    /// </summary>
    /// <returns>A task that completes after the latest applicable selection has been applied.</returns>
    public async Task SelectGameAsync()
    {
        if (_programmaticGameSelectionToIgnore == _viewModel.SelectedGame)
        {
            _programmaticGameSelectionToIgnore = null;
            return;
        }

        _programmaticGameSelectionToIgnore = null;

        if (_viewModel.SelectedGame is not { } selectedGame)
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
            _viewModel.AddInformationMessage(
                $"No installed locations found for {selectedGame}. Use Browse to select a directory.");
            return;
        }

        ApplyDetectedFolders(folders);
        await RefreshPluginsForCurrentSelectionAsync();
    }

    /// <summary>
    /// Handles detected-directory selection by refreshing plugins for the current game and directory.
    /// </summary>
    /// <returns>A task that completes after plugin refresh finishes or is skipped.</returns>
    public Task SelectDetectedDirectoryAsync()
    {
        if (_programmaticDirectorySelectionToIgnore is { } ignoredDirectory)
        {
            _programmaticDirectorySelectionToIgnore = null;

            if (string.Equals(ignoredDirectory, _viewModel.GameDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }
        }

        return RefreshPluginsForCurrentSelectionAsync();
    }

    /// <summary>
    /// Selects a game directory, detects the game when needed, and refreshes the plugin list.
    /// </summary>
    /// <returns>A task that completes after picker handling and plugin refresh finish.</returns>
    public async Task BrowseGameDirectoryAsync()
    {
        var result = await _fileDialogService.SelectGameDirectory();
        if (!TryGetSelectedPath(result, "Error selecting game directory", out var path))
        {
            return;
        }

        Interlocked.Increment(ref _gameSelectionVersion);
        SetGameDirectoryFromWorkflow(path);

        if (_viewModel.SelectedGame is null)
        {
            var detectedGame = _gameDetectionService.DetectGame(path);
            if (detectedGame.HasValue)
            {
                _programmaticGameSelectionToIgnore = detectedGame.Value;
                _viewModel.SelectedGame = detectedGame.Value;
            }
            else
            {
                _viewModel.AddErrorMessage(
                    "Could not detect game from directory. Please select a game from the dropdown.");
                return;
            }
        }

        await RefreshPluginsForCurrentSelectionAsync();
    }

    /// <summary>
    /// Selects a database path without overwriting state when the picker is cancelled.
    /// </summary>
    /// <returns>A task that completes after picker handling finishes.</returns>
    public async Task SelectDatabaseAsync()
    {
        var result = await _fileDialogService.SelectDatabaseFile();
        if (TryGetSelectedPath(result, "Error selecting database", out var path))
        {
            _viewModel.DatabasePath = path;
        }
    }

    /// <summary>
    /// Selects an optional FormID text file without overwriting state when the picker is cancelled.
    /// </summary>
    /// <returns>A task that completes after picker handling finishes.</returns>
    public async Task SelectFormIdListAsync()
    {
        var result = await _fileDialogService.SelectFormIdListFile();
        if (TryGetSelectedPath(result, "Error selecting FormID list file", out var path))
        {
            _viewModel.FormIdListPath = path;
        }
    }

    /// <summary>
    /// Refreshes the plugin list when both a game and directory are available.
    /// </summary>
    /// <returns>A task that completes when plugin refresh finishes or is skipped.</returns>
    public async Task RefreshPluginsForCurrentSelectionAsync()
    {
        if (_viewModel.SelectedGame is not { } game || string.IsNullOrEmpty(_viewModel.GameDirectory))
        {
            return;
        }

        await _pluginListManager.RefreshPluginList(
            _viewModel.GameDirectory,
            game,
            _viewModel.Plugins,
            _viewModel.AdvancedMode);
    }

    /// <summary>
    /// Selects every currently loaded plugin.
    /// </summary>
    public void SelectAllPlugins()
    {
        _pluginListManager.SelectAll(_viewModel.Plugins);
    }

    /// <summary>
    /// Clears selection for every currently loaded plugin.
    /// </summary>
    public void SelectNoPlugins()
    {
        _pluginListManager.SelectNone(_viewModel.Plugins);
    }

    /// <summary>
    /// Starts processing or requests cancellation for the active processing run.
    /// </summary>
    /// <returns>A task that completes after processing starts, finishes, fails, or observes cancellation.</returns>
    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records.")]
    public async Task ProcessFormIdsAsync()
    {
        if (_viewModel.IsProcessing)
        {
            _viewModel.ProgressStatus = "Cancelling...";
            _processingRun.Cancel();
            return;
        }

        _viewModel.ProcessButtonText = "Cancel Processing";
        _viewModel.IsProcessing = true;
        _viewModel.ProgressValue = 0;
        _viewModel.ProgressStatus = "Initializing...";
        _viewModel.ErrorMessages.Clear();

        try
        {
            if (_viewModel.SelectedGame is not { } gameRelease)
            {
                _viewModel.AddErrorMessage("Please select a game from the dropdown first.");
                return;
            }

            var databasePath = _viewModel.DatabasePath;
            if (string.IsNullOrEmpty(databasePath))
            {
                databasePath = DefaultDatabasePathProvider.CreateDefaultDatabasePath(gameRelease);
                _viewModel.DatabasePath = databasePath;
            }

            var request = CreateProcessingRunRequest(gameRelease, databasePath);
            var progress = new Progress<ProcessingRunEvent>(ApplyProcessingRunEvent);

            await _processingRun.ExecuteAsync(request, progress);
        }
        catch (ProcessingRunValidationException ex)
        {
            _viewModel.AddErrorMessage(ex.Message);
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
            _viewModel.ProcessButtonText = "Process FormIDs";
        }
    }

    /// <summary>
    /// Cancels processing and releases processing resources owned by the workflow.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _processingRun.Cancel();
        _processingRun.Dispose();
    }

    private static bool IsFailure(FileDialogResult result)
    {
        return result.Kind == FileDialogResultKind.Failure;
    }

    private bool TryGetSelectedPath(FileDialogResult result, string failureContext, out string path)
    {
        path = string.Empty;

        if (result.Kind == FileDialogResultKind.Success && !string.IsNullOrEmpty(result.Path))
        {
            path = result.Path;
            return true;
        }

        if (IsFailure(result))
        {
            _viewModel.AddErrorMessage($"{failureContext}: {result.ErrorMessage}");
        }

        return false;
    }

    private bool IsLatestGameSelection(int gameSelectionVersion)
    {
        return gameSelectionVersion == Volatile.Read(ref _gameSelectionVersion);
    }

    private void ResetGameSelectionState()
    {
        _viewModel.GameDirectory = string.Empty;
        _viewModel.DetectedDirectories.Clear();
        _viewModel.Plugins.Clear();
    }

    private void ApplyDetectedFolders(IReadOnlyList<string> folders)
    {
        _viewModel.DetectedDirectories.Clear();

        if (folders.Count > 1)
        {
            foreach (var folder in folders)
            {
                _viewModel.DetectedDirectories.Add(folder);
            }
        }

        SetGameDirectoryFromWorkflow(folders[0]);
    }

    private void SetGameDirectoryFromWorkflow(string path)
    {
        _programmaticDirectorySelectionToIgnore = path;
        _viewModel.GameDirectory = path;
    }

    private ProcessingRunRequest CreateProcessingRunRequest(GameRelease gameRelease, string databasePath)
    {
        var updateMode = _viewModel.UpdateMode ? UpdateMode.ReplacePluginRecords : UpdateMode.Append;

        if (!string.IsNullOrWhiteSpace(_viewModel.FormIdListPath))
        {
            return new FormIdTextProcessingRunRequest(
                _viewModel.FormIdListPath,
                databasePath,
                gameRelease,
                updateMode);
        }

        return new PluginProcessingRunRequest(
            _viewModel.GameDirectory,
            databasePath,
            gameRelease,
            _viewModel.GetSelectedPlugins().Select(plugin => plugin.Name),
            updateMode);
    }

    private void ApplyProcessingRunEvent(ProcessingRunEvent runEvent)
    {
        if (runEvent.Kind == ProcessingRunEventKind.Error)
        {
            _viewModel.AddErrorMessage(runEvent.Message);
            return;
        }

        _viewModel.UpdateProgress(runEvent.Message, runEvent.Value);
    }
}
