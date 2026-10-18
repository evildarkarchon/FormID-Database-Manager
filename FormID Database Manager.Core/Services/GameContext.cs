using System.Collections.Immutable;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Owns the authoritative Game Context: the selected GameRelease, game directory, available directories and Advanced
///     Mode, the four transitions that change them, and the Plugin List refresh or invalidation each transition implies
///     (ADR-0011).
/// </summary>
/// <remarks>
///     <para>
///         UI-thread affinity is part of the contract. Every member is entered from a UI handler, and every await
///         returns to the UI context, so the selection state is read and written on that thread. The latest-intent
///         generation counter is still interlocked, because installed-location lookup and detection finish on the
///         thread pool and are checked against it there in tests that have no UI context.
///     </para>
///     <para>
///         The Plugin List is borrowed: whoever constructs this module owns the Plugin List's lifetime, and
///         <see cref="Dispose" /> leaves it alone. This module writes the ViewModel's Game Context projection and its
///         own messages directly; with one projection there is no separate presentation adapter to earn.
///     </para>
/// </remarks>
internal sealed class GameContext : IDisposable
{
    private readonly IFileDialogService _fileDialogService;
    private readonly GameInstallations _gameInstallations;
    private readonly PluginList _pluginList;
    private readonly MainWindowViewModel _viewModel;

    private GameContextState _current = GameContextState.Empty;
    private int _gameContextVersion;

    /// <summary>
    ///     Creates an empty Game Context with no GameRelease, no directory and Advanced Mode off.
    /// </summary>
    /// <param name="gameInstallations">The Game Installation resolution module, used for detection and location.</param>
    /// <param name="fileDialogService">The platform picker adapter Browse opens.</param>
    /// <param name="pluginList">The Plugin List this module refreshes and invalidates, borrowed rather than owned.</param>
    /// <param name="viewModel">The ViewModel that receives the Game Context projection and its messages.</param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <remarks>Construction projects nothing and touches neither the installation probe nor the Plugin List.</remarks>
    internal GameContext(
        GameInstallations gameInstallations,
        IFileDialogService fileDialogService,
        PluginList pluginList,
        MainWindowViewModel viewModel)
    {
        _gameInstallations = gameInstallations ?? throw new ArgumentNullException(nameof(gameInstallations));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _pluginList = pluginList ?? throw new ArgumentNullException(nameof(pluginList));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    /// <summary>
    ///     Gets the authoritative Game Context as one immutable value.
    /// </summary>
    /// <remarks>
    ///     This is ahead of the ViewModel's projection when the ViewModel's dispatcher queues it, so a caller that must
    ///     act on what the user actually chose, such as a run deciding a button press, reads this rather than the
    ///     ViewModel.
    /// </remarks>
    internal GameContextState Current => _current;

    /// <summary>
    ///     Retires the current generation so late lookup, detection or picker work cannot publish state or messages.
    /// </summary>
    /// <remarks>
    ///     The borrowed Plugin List is not disposed here. Repeated calls only retire again, which is harmless.
    /// </remarks>
    public void Dispose()
    {
        BeginGameContextDirectoryTransition();
    }

    /// <summary>
    ///     Makes an explicit GameRelease selection authoritative before resolving its installed locations.
    /// </summary>
    /// <param name="selectedGameRelease">The selected GameRelease, or null when the selection is cleared.</param>
    /// <returns>A task that completes after current installed-location resolution and Plugin List refresh finish.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The selected GameRelease is unsupported.</exception>
    /// <exception cref="ObjectDisposedException">The borrowed Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>Current lookup and fatal discovery failures propagate unchanged; failures from retired lookups are ignored.</remarks>
    internal async Task SelectGameReleaseAsync(GameRelease? selectedGameRelease)
    {
        // An equal release is a no-op, which is also what makes the game ComboBox's echo of this module's own
        // projection harmless.
        if (_current.SelectedGameRelease == selectedGameRelease)
        {
            return;
        }

        var gameContextVersion = BeginGameContextDirectoryTransition();
        _current = _current with
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
    ///     Makes an explicit detected-directory selection authoritative before refreshing the Plugin List.
    /// </summary>
    /// <param name="selectedDirectory">The selected directory, or null when the selection is cleared.</param>
    /// <returns>A task that completes after current Plugin List discovery finishes.</returns>
    /// <exception cref="ArgumentException">A complete context contains a whitespace-only directory.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The authoritative GameRelease is unsupported.</exception>
    /// <exception cref="ObjectDisposedException">The borrowed Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>Programming and fatal Plugin discovery failures propagate unchanged.</remarks>
    internal Task SelectDetectedDirectoryAsync(string? selectedDirectory)
    {
        var domainDirectory = ToDomainDirectory(selectedDirectory);
        // The directory ComboBox echoes this module's own projection back as an intent, and this comparison is what
        // makes that echo a no-op.
        if (string.Equals(
                _current.SelectedGameDirectory,
                domainDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var gameContextVersion = BeginGameContextDirectoryTransition();
        _current = _current with { SelectedGameDirectory = domainDirectory };
        ProjectGameContext();
        return RefreshPluginsForCurrentGameContextAsync(gameContextVersion);
    }

    /// <summary>
    ///     Makes an explicit Advanced Mode value authoritative and refreshes the current Plugin List source.
    /// </summary>
    /// <param name="advancedMode">The Advanced Mode value selected by the user.</param>
    /// <returns>A task that completes after applicable Plugin List discovery finishes.</returns>
    /// <exception cref="ArgumentException">The complete Game Context contains an invalid directory.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The Advanced Mode or authoritative GameRelease is unsupported, or discovery returns an unsupported result.
    /// </exception>
    /// <exception cref="OperationCanceledException">Current Plugin discovery propagates an unexpected cancellation.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <exception cref="ObjectDisposedException">The borrowed Plugin List has been disposed.</exception>
    /// <remarks>
    ///     The authoritative snapshot is projected before discovery starts. Programming and fatal discovery failures propagate
    ///     unchanged, and Plugin List discovery may continue off the caller thread.
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

        if (_current.AdvancedMode == advancedMode)
        {
            return Task.CompletedTask;
        }

        // Display mode changes do not retire location resolution; a current lookup reads this latest snapshot
        // when it eventually requests Plugin List refresh.
        _current = _current with { AdvancedMode = advancedMode };
        ProjectGameContext();
        return RefreshPluginsForCurrentGameContextAsync();
    }

    /// <summary>
    ///     Opens the game-directory picker as a latest-intent operation, detects a missing GameRelease, and refreshes the
    ///     Plugin List.
    /// </summary>
    /// <returns>A task that completes after picker handling and plugin refresh finish.</returns>
    /// <exception cref="ObjectDisposedException">The borrowed Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>Current unexpected picker, detection, and fatal discovery failures propagate to WinUI's final boundary.</remarks>
    internal async Task BrowseGameDirectoryAsync()
    {
        // Opening the picker is itself an intent, so cancellation must still retire older asynchronous resolution.
        var gameContextVersion = BeginGameContextDirectoryTransition();
        var result = await _fileDialogService.SelectGameDirectory();
        if (!IsLatestGameContextDirectoryTransition(gameContextVersion))
        {
            return;
        }

        if (!result.TryGetSelectedPath(out var path))
        {
            if (result.Kind == FileDialogResultKind.Failure)
            {
                _viewModel.AddErrorMessage($"Error selecting game directory: {result.ErrorMessage}");
            }

            return;
        }

        await ApplyBrowsedDirectorySelectedAsync(path, gameContextVersion);
    }

    /// <summary>
    ///     Resolves installed locations without blocking the UI and publishes only while the initiating intent is current.
    /// </summary>
    /// <param name="selectedGame">The GameRelease whose installed locations are requested.</param>
    /// <param name="gameContextVersion">The resolution generation that owns any resulting state or messages.</param>
    /// <remarks>Current lookup and fatal discovery failures propagate unchanged; failures from retired lookups are ignored.</remarks>
    private async Task ResolveInstalledLocationsAsync(GameRelease selectedGame, int gameContextVersion)
    {
        ImmutableArray<string> folders;
        try
        {
            // Install-record lookup touches registry and file system state, and the module is synchronous by design,
            // so placing it off the UI thread is this caller's job.
            folders = await Task.Run(() => _gameInstallations.GetInstalledDirectories(selectedGame));
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

        if (folders.IsEmpty)
        {
            _viewModel.AddInformationMessage(
                $"No installed locations found for {selectedGame}. Use Browse to select a directory.");
            return;
        }

        ApplyDetectedFolders(folders);
        await RefreshPluginsForCurrentGameContextAsync(gameContextVersion);
    }

    /// <summary>
    ///     Applies one current Browse result while preserving an explicit release and treating suggestions as non-binding.
    /// </summary>
    /// <param name="path">The user-selected game root or Data directory.</param>
    /// <param name="gameContextVersion">The Browse resolution generation allowed to publish resulting state.</param>
    /// <returns>A task that completes after applicable detection and Plugin List discovery finish.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The detected or selected GameRelease is unsupported.</exception>
    /// <exception cref="ObjectDisposedException">The borrowed Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>
    ///     A path detection cannot use at all is presented as an error describing that failure; other current unexpected
    ///     detection and fatal discovery failures propagate unchanged.
    /// </remarks>
    private async Task ApplyBrowsedDirectorySelectedAsync(string path, int gameContextVersion)
    {
        if (_current.SelectedGameRelease is null)
        {
            _current = _current with { AvailableDirectories = ImmutableArray<string>.Empty };
        }

        _current = _current with { SelectedGameDirectory = ToDomainDirectory(path) };
        // Projected before the latest check below, because projection handlers can re-enter and start a newer intent.
        ProjectGameContext();
        if (!IsLatestGameContextDirectoryTransition(gameContextVersion))
        {
            return;
        }

        // Browse is an explicit retry even for the same path, so a failed retry must not retain old confirmation.
        _pluginList.Invalidate();

        if (_current.SelectedGameRelease is null)
        {
            GameRelease? detectedGame;
            try
            {
                // Detection probes the file system roughly a dozen times, which a slow or network directory can turn
                // into a visible hang, and the module is synchronous by design — so this caller places it off the
                // UI thread, exactly as it does for installed-location lookup (ADR-0002).
                detectedGame = await Task.Run(() => _gameInstallations.Detect(path));
            }
            catch (Exception) when (!IsLatestGameContextDirectoryTransition(gameContextVersion))
            {
                // A detector overtaken by a newer intent cannot escape into WinUI's current error boundary.
                return;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                // Detection returns null for exactly one reason — no known game master file — and throws for a path
                // it cannot use at all. Reporting that as "could not detect game" would tell the user to pick a game
                // when the path is what is wrong (ADR-0002), so the actual failure is surfaced instead.
                _viewModel.AddErrorMessage($"Could not read the selected directory: {ex.Message}");
                return;
            }

            if (!IsLatestGameContextDirectoryTransition(gameContextVersion))
            {
                return;
            }

            if (detectedGame.HasValue)
            {
                _current = _current with { SelectedGameRelease = detectedGame.Value };
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
    ///     Refreshes the exact complete Game Context or explicitly invalidates the borrowed Plugin List.
    /// </summary>
    /// <param name="expectedGameContextVersion">An optional directory-transition generation that must still be current.</param>
    /// <returns>A task that completes when applicable Plugin discovery finishes.</returns>
    /// <exception cref="ArgumentException">The held game directory cannot become a Plugin List Source.</exception>
    private async Task RefreshPluginsForCurrentGameContextAsync(int? expectedGameContextVersion = null)
    {
        if (expectedGameContextVersion.HasValue &&
            !IsLatestGameContextDirectoryTransition(expectedGameContextVersion.Value))
        {
            return;
        }

        if (_current.SelectedGameRelease is not { } game ||
            _current.SelectedGameDirectory is not { } gameDirectory)
        {
            _pluginList.Invalidate();
            return;
        }

        // The Plugin List no longer validates a directory, so the source is built here, immediately before the
        // refresh, keeping today's ArgumentException for an unusable directory until the Game Context builds it
        // when the directory is adopted (ADR-0011).
        await _pluginList.RefreshAsync(
            PluginListSource.Create(game, gameDirectory),
            _current.AdvancedMode);
    }

    /// <summary>
    ///     Starts a new directory-changing generation, retiring every older one.
    /// </summary>
    /// <returns>The generation the caller now owns.</returns>
    private int BeginGameContextDirectoryTransition()
    {
        // Directory discovery can finish after a later user action, so each directory-changing
        // transition gets a generation that retires older lookup results before they touch state.
        return Interlocked.Increment(ref _gameContextVersion);
    }

    /// <summary>
    ///     Reports whether a generation is still the latest, read from whichever thread the work finished on.
    /// </summary>
    /// <param name="gameContextVersion">The generation the caller owns.</param>
    /// <returns>True when no newer intent, and no disposal, has retired it.</returns>
    private bool IsLatestGameContextDirectoryTransition(int gameContextVersion)
    {
        return gameContextVersion == Volatile.Read(ref _gameContextVersion);
    }

    /// <summary>
    ///     Publishes a non-empty installed-location result as the complete ordered available-directory snapshot.
    /// </summary>
    /// <param name="folders">The installed locations in record order.</param>
    private void ApplyDetectedFolders(ImmutableArray<string> folders)
    {
        _current = _current with { SelectedGameDirectory = folders[0], AvailableDirectories = folders };
        ProjectGameContext();
    }

    /// <summary>
    ///     Publishes the complete authoritative Game Context through the ViewModel's one restricted projection seam.
    /// </summary>
    private void ProjectGameContext()
    {
        _viewModel.ApplyGameContextProjection(
            _current.SelectedGameRelease,
            _current.SelectedGameDirectory,
            _current.AvailableDirectories,
            _current.AdvancedMode);
    }

    /// <summary>
    ///     Converts the presentation value to the nullable directory used by the domain snapshot.
    /// </summary>
    /// <param name="directory">The existing presentation directory value.</param>
    /// <returns>The domain directory, or null when the presentation value represents absence.</returns>
    private static string? ToDomainDirectory(string? directory)
    {
        return string.IsNullOrEmpty(directory) ? null : directory;
    }
}

/// <summary>
///     The complete authoritative Game Context as one immutable value.
/// </summary>
/// <param name="SelectedGameRelease">The selected GameRelease, when one has been accepted.</param>
/// <param name="SelectedGameDirectory">The selected domain directory, or null while the context is incomplete.</param>
/// <param name="AvailableDirectories">The complete ordered available-directory set.</param>
/// <param name="AdvancedMode">The current Advanced Mode value.</param>
internal sealed record GameContextState(
    GameRelease? SelectedGameRelease,
    string? SelectedGameDirectory,
    ImmutableArray<string> AvailableDirectories,
    AdvancedMode AdvancedMode)
{
    /// <summary>
    ///     Gets the state a new Game Context starts in: nothing selected and Advanced Mode off.
    /// </summary>
    internal static GameContextState Empty { get; } = new(
        null,
        null,
        ImmutableArray<string>.Empty,
        AdvancedMode.Off);
}
