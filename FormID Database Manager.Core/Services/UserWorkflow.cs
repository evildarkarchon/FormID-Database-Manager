using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Owns authoritative Game Context, the UI-neutral user workflow, Plugin List lifetime, and Processing Run coordination.
/// </summary>
public sealed class UserWorkflow : IDisposable
{
    private readonly IFileDialogService _fileDialogService;
    private readonly GameDetectionService _gameDetectionService;
    private readonly IGameLocationService _gameLocationService;
    private readonly PluginList _pluginList;
    private readonly IProcessingRunExecutor _processingRunExecutor;
    private readonly MainWindowViewModel _viewModel;
    private GameContextSnapshot _gameContext;
    private bool _disposed;
    private int _gameContextVersion;

    /// <summary>
    /// Creates a workflow module that coordinates existing Core modules behind one UI-neutral interface.
    /// </summary>
    /// <param name="viewModel">The binding-state projection updated by workflow transitions.</param>
    /// <param name="fileDialogService">The platform picker adapter.</param>
    /// <param name="gameDetectionService">The game detection module.</param>
    /// <param name="gameLocationService">The installed-location lookup adapter.</param>
    /// <param name="pluginList">The authoritative Plugin List whose lifetime transfers to this workflow.</param>
    /// <param name="processingRunExecutor">The owned Processing Run executor.</param>
    internal UserWorkflow(
        MainWindowViewModel viewModel,
        IFileDialogService fileDialogService,
        GameDetectionService gameDetectionService,
        IGameLocationService gameLocationService,
        PluginList pluginList,
        IProcessingRunExecutor processingRunExecutor)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _gameDetectionService = gameDetectionService ?? throw new ArgumentNullException(nameof(gameDetectionService));
        _gameLocationService = gameLocationService ?? throw new ArgumentNullException(nameof(gameLocationService));
        _pluginList = pluginList ?? throw new ArgumentNullException(nameof(pluginList));
        _processingRunExecutor = processingRunExecutor ??
                                 throw new ArgumentNullException(nameof(processingRunExecutor));
        _gameContext = new GameContextSnapshot(
            null,
            null,
            ImmutableArray<string>.Empty,
            AdvancedMode.Off);
    }

    /// <summary>
    /// Makes an explicit GameRelease selection authoritative before resolving its installed locations.
    /// </summary>
    /// <param name="selectedGameRelease">The selected GameRelease, or null when the selection is cleared.</param>
    /// <returns>A task that completes after current installed-location resolution and Plugin List refresh finish.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The selected GameRelease is unsupported.</exception>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>Current lookup and fatal discovery failures propagate unchanged; failures from retired lookups are ignored.</remarks>
    internal async Task SelectGameReleaseAsync(GameRelease? selectedGameRelease)
    {
        if (_gameContext.SelectedGameRelease == selectedGameRelease)
        {
            return;
        }

        var gameContextVersion = BeginGameContextDirectoryTransition();
        _gameContext = _gameContext with
        {
            SelectedGameRelease = selectedGameRelease,
            SelectedGameDirectory = null,
            AvailableDirectories = ImmutableArray<string>.Empty
        };
        ProjectGameContext();
        _pluginList.Invalidate();

        if (selectedGameRelease is { } gameRelease)
        {
            await ResolveInstalledLocationsAsync(gameRelease, gameContextVersion);
        }
    }

    /// <summary>
    /// Makes an explicit detected-directory selection authoritative before refreshing the Plugin List.
    /// </summary>
    /// <param name="selectedDirectory">The selected directory, or null when the selection is cleared.</param>
    /// <returns>A task that completes after current Plugin List discovery finishes.</returns>
    /// <exception cref="ArgumentException">A complete context contains a whitespace-only directory.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The authoritative GameRelease is unsupported.</exception>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>Programming and fatal Plugin discovery failures propagate unchanged.</remarks>
    internal Task SelectDetectedDirectoryAsync(string? selectedDirectory)
    {
        var domainDirectory = ToDomainDirectory(selectedDirectory);
        if (string.Equals(
                _gameContext.SelectedGameDirectory,
                domainDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var gameContextVersion = BeginGameContextDirectoryTransition();
        _gameContext = _gameContext with { SelectedGameDirectory = domainDirectory };
        ProjectGameContext();
        return RefreshPluginsForCurrentGameContextAsync(gameContextVersion);
    }

    /// <summary>
    /// Makes an explicit Advanced Mode value authoritative and refreshes the current Plugin List source.
    /// </summary>
    /// <param name="advancedMode">The Advanced Mode value selected by the user.</param>
    /// <returns>A task that completes after applicable Plugin List discovery finishes.</returns>
    /// <exception cref="ArgumentException">The complete Game Context contains an invalid directory.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The Advanced Mode or authoritative GameRelease is unsupported, or discovery returns an unsupported result.
    /// </exception>
    /// <exception cref="OperationCanceledException">Current Plugin discovery propagates an unexpected cancellation.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    /// <remarks>
    /// The authoritative snapshot is projected before discovery starts. Programming and fatal discovery failures propagate
    /// unchanged, and Plugin List discovery may continue off the caller thread.
    /// </remarks>
    internal Task SetAdvancedModeAsync(AdvancedMode advancedMode)
    {
        if (advancedMode is not AdvancedMode.Off and not AdvancedMode.On)
        {
            throw new ArgumentOutOfRangeException(
                nameof(advancedMode),
                advancedMode,
                "Unsupported Advanced Mode value.");
        }

        if (_gameContext.AdvancedMode == advancedMode)
        {
            return Task.CompletedTask;
        }

        // Display mode changes do not retire location resolution; a current lookup reads this latest snapshot
        // when it eventually requests Plugin List refresh.
        _gameContext = _gameContext with { AdvancedMode = advancedMode };
        ProjectGameContext();
        return RefreshPluginsForCurrentGameContextAsync();
    }

    /// <summary>
    /// Opens the game-directory picker as a latest-intent operation, detects a missing GameRelease, and refreshes the Plugin List.
    /// </summary>
    /// <returns>A task that completes after picker handling and plugin refresh finish.</returns>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>Current unexpected picker, detection, and fatal discovery failures propagate to WinUI's final boundary.</remarks>
    public async Task BrowseGameDirectoryAsync()
    {
        // Opening the picker is itself an intent, so cancellation must still retire older asynchronous resolution.
        var gameContextVersion = BeginGameContextDirectoryTransition();
        var result = await _fileDialogService.SelectGameDirectory();
        if (!IsLatestGameContextDirectoryTransition(gameContextVersion))
        {
            return;
        }

        if (!TryGetSelectedPath(result, "Error selecting game directory", out var path))
        {
            return;
        }

        await ApplyBrowsedDirectorySelectedAsync(path, gameContextVersion);
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
    /// Selects every currently loaded plugin.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    public void SelectAllPlugins()
    {
        ApplyWholeListSelection(true);
    }

    /// <summary>
    /// Clears selection for every currently loaded plugin.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    public void SelectNoPlugins()
    {
        ApplyWholeListSelection(false);
    }

    /// <summary>
    /// Submits one user-activated, versioned Plugin selection change to the authoritative Plugin List.
    /// </summary>
    /// <param name="membershipVersion">The confirmed membership version displayed when the user acted.</param>
    /// <param name="pluginName">The projected Plugin name the user activated.</param>
    /// <param name="isSelected">The desired selection state reported by the checkbox.</param>
    /// <exception cref="ArgumentException"><paramref name="pluginName" /> is empty or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    internal void SetPluginSelection(long membershipVersion, string pluginName, bool isSelected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        _pluginList.Apply(new PluginSelectionByNameIntent(membershipVersion, pluginName, isSelected));
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
            _processingRunExecutor.Cancel();
            return;
        }

        _viewModel.ProcessButtonText = "Cancel Processing";
        _viewModel.IsProcessing = true;
        _viewModel.ProgressValue = 0;
        _viewModel.ProgressStatus = "Initializing...";
        _viewModel.ErrorMessages.Clear();
        _viewModel.WarningMessages.Clear();

        try
        {
            // Projection notifications are reentrant, so every validation and source check for this run must use
            // the same authoritative Game Context even if a new user intent is accepted before request creation ends.
            var gameContext = _gameContext;
            if (gameContext.SelectedGameRelease is not { } gameRelease)
            {
                _viewModel.AddErrorMessage("Please select a game from the dropdown first.");
                return;
            }

            var formIdListPath = _viewModel.FormIdListPath;
            var confirmedPluginList = string.IsNullOrWhiteSpace(formIdListPath)
                ? GetConfirmedPluginListFor(gameContext)
                : null;
            var databasePath = _viewModel.DatabasePath;
            if (string.IsNullOrEmpty(databasePath))
            {
                databasePath = DefaultDatabasePathProvider.CreateDefaultDatabasePath(gameRelease);
                _viewModel.DatabasePath = databasePath;
            }

            var request = CreateProcessingRunRequest(
                gameContext,
                formIdListPath,
                databasePath,
                confirmedPluginList);
            var progress = new ProcessingRunProgressAdapter(ApplyProcessingRunEvent);

            await _processingRunExecutor.ExecuteAsync(request, progress);
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
        // Retire resolution before disposing collaborators so late lookup or picker work cannot publish into torn-down state.
        Interlocked.Increment(ref _gameContextVersion);
        _processingRunExecutor.Cancel();
        _processingRunExecutor.Dispose();
        _pluginList.Dispose();
    }

    /// <summary>
    /// Resolves installed locations without blocking the UI and publishes only while the initiating intent is current.
    /// </summary>
    /// <param name="selectedGame">The GameRelease whose installed locations are requested.</param>
    /// <param name="gameContextVersion">The resolution generation that owns any resulting state or messages.</param>
    /// <remarks>Current lookup and fatal discovery failures propagate unchanged; failures from retired lookups are ignored.</remarks>
    private async Task ResolveInstalledLocationsAsync(GameRelease selectedGame, int gameContextVersion)
    {
        List<string> folders;
        try
        {
            // Mutagen game-location lookup touches registry and file system state, so keep it off the UI thread.
            folders = await Task.Run(() => _gameLocationService.GetGameFolders(selectedGame));
        }
        catch (Exception) when (!IsLatestGameContextDirectoryTransition(gameContextVersion))
        {
            // An older lookup cannot surface failures after a newer release or directory intent becomes authoritative.
            return;
        }

        if (!IsLatestGameContextDirectoryTransition(gameContextVersion))
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
        await RefreshPluginsForCurrentGameContextAsync(gameContextVersion);
    }

    /// <summary>
    /// Applies one current Browse result while preserving an explicit release and treating suggestions as non-binding.
    /// </summary>
    /// <param name="path">The user-selected game root or Data directory.</param>
    /// <param name="gameContextVersion">The Browse resolution generation allowed to publish resulting state.</param>
    /// <returns>A task that completes after applicable detection and Plugin List discovery finish.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The detected or selected GameRelease is unsupported.</exception>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>Current unexpected detection and fatal discovery failures propagate unchanged.</remarks>
    private async Task ApplyBrowsedDirectorySelectedAsync(string path, int gameContextVersion)
    {
        if (_gameContext.SelectedGameRelease is null)
        {
            _gameContext = _gameContext with { AvailableDirectories = ImmutableArray<string>.Empty };
        }

        _gameContext = _gameContext with { SelectedGameDirectory = ToDomainDirectory(path) };
        ProjectGameContext();
        if (!IsLatestGameContextDirectoryTransition(gameContextVersion))
        {
            return;
        }

        // Browse is an explicit retry even for the same path, so a failed retry must not retain old confirmation.
        _pluginList.Invalidate();

        if (_gameContext.SelectedGameRelease is null)
        {
            GameRelease? detectedGame;
            try
            {
                detectedGame = _gameDetectionService.DetectGame(path);
            }
            catch (Exception) when (!IsLatestGameContextDirectoryTransition(gameContextVersion))
            {
                // A detector overtaken by a newer intent cannot escape into WinUI's current error boundary.
                return;
            }

            if (!IsLatestGameContextDirectoryTransition(gameContextVersion))
            {
                return;
            }

            if (detectedGame.HasValue)
            {
                _gameContext = _gameContext with { SelectedGameRelease = detectedGame.Value };
                ProjectGameContext();
            }
            else
            {
                _viewModel.AddErrorMessage(
                    "Could not detect game from directory. Please select a game from the dropdown.");
                return;
            }
        }

        await RefreshPluginsForCurrentGameContextAsync(gameContextVersion);
    }

    /// <summary>
    /// Refreshes the exact complete Game Context or explicitly invalidates the authoritative Plugin List.
    /// </summary>
    /// <param name="expectedGameContextVersion">An optional directory-transition generation that must still be current.</param>
    /// <returns>A task that completes when applicable Plugin discovery finishes.</returns>
    private async Task RefreshPluginsForCurrentGameContextAsync(int? expectedGameContextVersion = null)
    {
        if (expectedGameContextVersion.HasValue &&
            !IsLatestGameContextDirectoryTransition(expectedGameContextVersion.Value))
        {
            return;
        }

        if (_gameContext.SelectedGameRelease is not { } game ||
            _gameContext.SelectedGameDirectory is not { } gameDirectory)
        {
            _pluginList.Invalidate();
            return;
        }

        await _pluginList.RefreshAsync(
            game,
            gameDirectory,
            _gameContext.AdvancedMode);
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

    private int BeginGameContextDirectoryTransition()
    {
        // Directory discovery can finish after a later user action, so each directory-changing
        // transition gets a generation that retires older lookup results before they touch state.
        return Interlocked.Increment(ref _gameContextVersion);
    }

    private bool IsLatestGameContextDirectoryTransition(int gameContextVersion)
    {
        return gameContextVersion == Volatile.Read(ref _gameContextVersion);
    }

    /// <summary>
    /// Publishes a non-empty installed-location result as the complete ordered available-directory snapshot.
    /// </summary>
    /// <param name="folders">The installed locations in discovery order.</param>
    private void ApplyDetectedFolders(IReadOnlyList<string> folders)
    {
        var availableDirectories = folders.ToImmutableArray();
        var selectedDirectory = folders[0];
        _gameContext = _gameContext with
        {
            SelectedGameDirectory = selectedDirectory,
            AvailableDirectories = availableDirectories
        };
        ProjectGameContext();
    }

    /// <summary>
    /// Publishes the complete authoritative Game Context through the ViewModel's one restricted projection seam.
    /// </summary>
    private void ProjectGameContext()
    {
        _viewModel.ApplyGameContextProjection(
            _gameContext.SelectedGameRelease,
            _gameContext.SelectedGameDirectory,
            _gameContext.AvailableDirectories,
            _gameContext.AdvancedMode);
    }

    /// <summary>
    /// Converts the presentation value to the nullable directory used by the domain snapshot.
    /// </summary>
    /// <param name="directory">The existing presentation directory value.</param>
    /// <returns>The domain directory, or null when the presentation value represents absence.</returns>
    private static string? ToDomainDirectory(string? directory)
    {
        return string.IsNullOrEmpty(directory) ? null : directory;
    }

    /// <summary>
    /// Submits a versioned whole-list selection intent against one captured confirmed membership.
    /// </summary>
    /// <param name="isSelected">Whether every confirmed Plugin should be selected.</param>
    private void ApplyWholeListSelection(bool isSelected)
    {
        var confirmed = _pluginList.Current.Confirmed;
        if (confirmed is null)
        {
            return;
        }

        _pluginList.Apply(new PluginSelectionForAllIntent(confirmed.MembershipVersion, isSelected));
    }

    /// <summary>
    /// Captures confirmation only when it belongs to the authoritative Plugin List Source for this Processing Run.
    /// </summary>
    /// <param name="gameContext">The one authoritative Game Context captured for run creation.</param>
    /// <returns>
    /// The matching immutable confirmation, or null when the context is incomplete or confirmation does not match its
    /// Plugin List Source.
    /// </returns>
    /// <exception cref="ArgumentException">The captured directory cannot identify a normalized Plugin List Source.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The captured GameRelease is unsupported.</exception>
    private ConfirmedPluginList? GetConfirmedPluginListFor(GameContextSnapshot gameContext)
    {
        if (gameContext.SelectedGameRelease is not { } gameRelease ||
            gameContext.SelectedGameDirectory is not { } gameDirectory ||
            string.IsNullOrWhiteSpace(gameDirectory))
        {
            return null;
        }

        var expectedSource = PluginListSource.Create(gameRelease, gameDirectory);
        var confirmedPluginList = _pluginList.Current.Confirmed;
        return confirmedPluginList?.Source == expectedSource ? confirmedPluginList : null;
    }

    /// <summary>
    /// Creates one immutable Processing Run request, using a single confirmed Plugin List snapshot for Plugin runs.
    /// </summary>
    /// <param name="gameContext">The one authoritative Game Context captured for run creation.</param>
    /// <param name="formIdListPath">The captured optional FormID text-file path.</param>
    /// <param name="databasePath">The resolved Store path for the run.</param>
    /// <param name="confirmedPluginList">The one captured confirmation for a Plugin run, when available.</param>
    /// <returns>A validated immutable Processing Run request.</returns>
    /// <exception cref="InvalidOperationException">The captured Game Context has no selected GameRelease.</exception>
    /// <exception cref="ProcessingRunValidationException">The captured request facts do not define a valid Processing Run.</exception>
    private ProcessingRunRequest CreateProcessingRunRequest(
        GameContextSnapshot gameContext,
        string formIdListPath,
        string databasePath,
        ConfirmedPluginList? confirmedPluginList)
    {
        var selectedGameRelease = gameContext.SelectedGameRelease ??
                                  throw new InvalidOperationException(
                                      "Processing Run creation requires an authoritative GameRelease.");
        var updateMode = _viewModel.UpdateMode ? UpdateMode.ReplacePluginRecords : UpdateMode.Append;

        if (!string.IsNullOrWhiteSpace(formIdListPath))
        {
            return new FormIdTextProcessingRunRequest(
                formIdListPath,
                databasePath,
                selectedGameRelease,
                updateMode);
        }

        if (confirmedPluginList is null)
        {
            // Preserve legacy validation order without allowing an unconfirmed Plugin request to reach execution.
            return new PluginProcessingRunRequest(
                gameContext.SelectedGameDirectory ?? string.Empty,
                databasePath,
                selectedGameRelease,
                [],
                updateMode);
        }

        return new PluginProcessingRunRequest(
            PluginListSource.Create(
                selectedGameRelease,
                gameContext.SelectedGameDirectory ?? string.Empty).DataDirectory,
            databasePath,
            selectedGameRelease,
            confirmedPluginList.SelectedPluginNames,
            updateMode);
    }

    private void ApplyProcessingRunEvent(ProcessingRunEvent runEvent)
    {
        if (runEvent.Kind == ProcessingRunEventKind.Error)
        {
            _viewModel.AddErrorMessage(runEvent.Message);
            return;
        }

        if (runEvent.Kind == ProcessingRunEventKind.Warning)
        {
            _viewModel.AddWarningMessage(runEvent.Message);
            return;
        }

        _viewModel.UpdateProgress(runEvent.Message, runEvent.Value);
    }

    /// <summary>
    /// Retains the complete User Workflow-owned Game Context as one immutable value.
    /// </summary>
    /// <param name="SelectedGameRelease">The selected GameRelease, when one has been accepted.</param>
    /// <param name="SelectedGameDirectory">The selected domain directory, or null while the context is incomplete.</param>
    /// <param name="AvailableDirectories">The complete ordered available-directory set.</param>
    /// <param name="AdvancedMode">The current Advanced Mode value.</param>
    private sealed record GameContextSnapshot(
        GameRelease? SelectedGameRelease,
        string? SelectedGameDirectory,
        ImmutableArray<string> AvailableDirectories,
        AdvancedMode AdvancedMode);

    private sealed class ProcessingRunProgressAdapter(Action<ProcessingRunEvent> handler) : IProgress<ProcessingRunEvent>
    {
        public void Report(ProcessingRunEvent value)
        {
            handler(value);
        }
    }
}
