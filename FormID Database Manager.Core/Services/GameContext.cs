using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Owns the authoritative Game Context: the selected GameRelease, game directory, the Plugin List Source built from
///     it, available directories and Advanced Mode, the four transitions that change them, and the Plugin List refresh
///     or invalidation each transition implies (ADR-0011).
/// </summary>
/// <remarks>
///     <para>
///         The source is built when a directory is adopted, so this module never holds a directory that cannot be
///         used. Browse and a detected-directory pick share one adoption step: build the source; if that fails, refuse
///         with a <see cref="DirectoryRefused" /> notice and change nothing; if the source equals the current one, do
///         nothing; otherwise adopt it and refresh. "The same directory" therefore means source equality, so a game
///         root and its Data directory are one pick, and the ComboBox echo of this module's own reported state is a
///         no-op.
///     </para>
///     <para>
///         Browse alone has one exception to the equal-source no-op: when the Plugin List for that source is in its
///         failed state, an equal Browse is the user's explicit retry and invalidates and refreshes again. A Browse
///         with a different source invalidates the Plugin List first, then adopts and refreshes.
///     </para>
///     <para>
///         UI-thread affinity is part of the contract. Every member is entered from a UI handler, and every await
///         returns to the UI context, so the selection state is read and written on that thread. The latest-intent
///         generation counter is still interlocked, because installed-location lookup and detection finish on the
///         thread pool and are checked against it there in tests that have no UI context.
///     </para>
///     <para>
///         The Plugin List is borrowed: whoever constructs this module owns the Plugin List's lifetime, and
///         <see cref="Dispose" /> leaves it alone. This module writes nothing the user sees: it reports every
///         published state and every typed <see cref="GameContextNotice" /> to an <see cref="IGameContextSink" />,
///         through a sink adapter that the User Workflow composes, and it holds no wording, because
///         <see cref="GameContextPresentation" /> renders the notices (ADR-0013, ADR-0014).
///     </para>
/// </remarks>
internal sealed class GameContext : IDisposable
{
    private readonly IFileDialogService _fileDialogService;
    private readonly GameInstallations _gameInstallations;
    private readonly PluginList _pluginList;
    private readonly IGameContextSink _sink;

    private GameContextState _current = GameContextState.Empty;
    private int _gameContextVersion;

    /// <summary>
    ///     Creates an empty Game Context with no GameRelease, no directory and Advanced Mode off.
    /// </summary>
    /// <param name="gameInstallations">The Game Installation resolution module, used for detection and location.</param>
    /// <param name="fileDialogService">The platform picker adapter Browse opens.</param>
    /// <param name="pluginList">The Plugin List this module refreshes and invalidates, borrowed rather than owned.</param>
    /// <param name="sink">Where every published state and every notice is reported.</param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <remarks>Construction reports nothing and touches neither the installation probe nor the Plugin List.</remarks>
    internal GameContext(
        GameInstallations gameInstallations,
        IFileDialogService fileDialogService,
        PluginList pluginList,
        IGameContextSink sink)
    {
        _gameInstallations = gameInstallations ?? throw new ArgumentNullException(nameof(gameInstallations));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _pluginList = pluginList ?? throw new ArgumentNullException(nameof(pluginList));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
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
    ///     Retires the current generation so late lookup, detection or picker work cannot report a state or a notice.
    /// </summary>
    /// <remarks>
    ///     The borrowed Plugin List is not disposed here. Repeated calls only retire again, which is harmless.
    /// </remarks>
    public void Dispose()
    {
        StartLatestIntentGeneration();
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
        // reported state harmless.
        if (_current.SelectedGameRelease == selectedGameRelease)
        {
            return;
        }

        var gameContextVersion = StartLatestIntentGeneration();
        Publish(_current with
        {
            SelectedGameRelease = selectedGameRelease,
            SelectedGameDirectory = null,
            PluginListSource = null,
            AvailableDirectories = ImmutableArray<string>.Empty
        });
        _pluginList.Invalidate();

        if (selectedGameRelease is { } gameRelease)
        {
            await ResolveInstalledLocationsAsync(gameRelease, gameContextVersion);
        }
    }

    /// <summary>
    ///     Adopts an explicit detected-directory selection and refreshes the Plugin List for it, or clears the directory
    ///     and invalidates the Plugin List when the selection is cleared.
    /// </summary>
    /// <param name="selectedDirectory">The selected directory, or null when the selection is cleared.</param>
    /// <returns>A task that completes after current Plugin List discovery finishes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The authoritative GameRelease is unsupported.</exception>
    /// <exception cref="ObjectDisposedException">The borrowed Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>
    ///     <para>
    ///         Adoption builds the Plugin List Source first. A directory that cannot become one is refused with a
    ///         <see cref="DirectoryRefused" /> notice and changes nothing; a source equal to the current one is a
    ///         no-op, which is what makes an equivalent spelling (game root or Data directory) and the directory
    ///         ComboBox's echo of this module's own reported state harmless. Install-record directories were checked
    ///         when they were listed, so a pick from the detected list is never refused.
    ///     </para>
    ///     <para>
    ///         With no GameRelease there is nothing to build a source with and no detected list to pick from, so a
    ///         non-empty directory is ignored rather than held. Programming and fatal Plugin discovery failures
    ///         propagate unchanged.
    ///     </para>
    /// </remarks>
    internal Task SelectDetectedDirectoryAsync(string? selectedDirectory)
    {
        if (string.IsNullOrEmpty(selectedDirectory))
        {
            // The empty string is how the presentation represents absence, and clearing an already-absent directory
            // (including the echo of a cleared projection) is a no-op.
            if (_current.SelectedGameDirectory is null)
            {
                return Task.CompletedTask;
            }

            var clearingVersion = StartLatestIntentGeneration();
            Publish(_current with { SelectedGameDirectory = null, PluginListSource = null });
            return RefreshPluginsForCurrentGameContextAsync(clearingVersion);
        }

        // A directory is only adopted once a GameRelease is known; the shell offers no detected directory without one.
        if (_current.SelectedGameRelease is not { } gameRelease ||
            !TryCreateSourceOrRefuse(gameRelease, selectedDirectory, out var source))
        {
            return Task.CompletedTask;
        }

        if (source == _current.PluginListSource)
        {
            return Task.CompletedTask;
        }

        var gameContextVersion = StartLatestIntentGeneration();
        Adopt(selectedDirectory, source, gameRelease);
        return RefreshPluginsForCurrentGameContextAsync(gameContextVersion);
    }

    /// <summary>
    ///     Makes an explicit Advanced Mode value authoritative and refreshes the current Plugin List source.
    /// </summary>
    /// <param name="advancedMode">The Advanced Mode value selected by the user.</param>
    /// <returns>A task that completes after applicable Plugin List discovery finishes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The Advanced Mode or authoritative GameRelease is unsupported, or discovery returns an unsupported result.
    /// </exception>
    /// <exception cref="OperationCanceledException">Current Plugin discovery propagates an unexpected cancellation.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <exception cref="ObjectDisposedException">The borrowed Plugin List has been disposed.</exception>
    /// <remarks>
    ///     The authoritative snapshot is reported before discovery starts. Programming and fatal discovery failures
    ///     propagate unchanged, and Plugin List discovery may continue off the caller thread.
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
        Publish(_current with { AdvancedMode = advancedMode });
        return RefreshPluginsForCurrentGameContextAsync();
    }

    /// <summary>
    ///     Opens the game-directory picker as a latest-intent operation, detects a missing GameRelease, and refreshes the
    ///     Plugin List.
    /// </summary>
    /// <returns>A task that completes after picker handling and plugin refresh finish.</returns>
    /// <exception cref="ObjectDisposedException">The borrowed Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>
    ///     An unusable directory, or one with no detectable game, is refused with a message rather than thrown, and
    ///     changes nothing. Current unexpected picker, detection, and fatal discovery failures propagate to WinUI's final
    ///     boundary.
    /// </remarks>
    internal async Task BrowseGameDirectoryAsync()
    {
        // Opening the picker is itself an intent, so cancellation must still retire older asynchronous resolution.
        var gameContextVersion = StartLatestIntentGeneration();
        var result = await _fileDialogService.SelectGameDirectory();
        if (!IsLatestIntentGeneration(gameContextVersion))
        {
            return;
        }

        if (!result.TryGetSelectedPath(out var path))
        {
            if (result.Kind == FileDialogResultKind.Failure)
            {
                // A failure result always carries its text; the fallback only satisfies the nullable property's type
                // and renders exactly what the old interpolation of a null did.
                _sink.ReportNotice(new GameDirectoryPickerFailed(result.ErrorMessage ?? string.Empty));
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
        catch (Exception) when (!IsLatestIntentGeneration(gameContextVersion))
        {
            // An older lookup cannot surface failures after a newer release or directory intent becomes authoritative.
            return;
        }

        if (!IsLatestIntentGeneration(gameContextVersion))
        {
            return;
        }

        // Each install-record directory gets its source when it is listed, so a pick from the detected list can never
        // be refused; directories that cannot become a source are left out rather than offered (ADR-0011).
        var usable = ImmutableArray.CreateBuilder<(string Directory, PluginListSource Source)>(folders.Length);
        foreach (var folder in folders)
        {
            if (TryCreateSourceSilently(selectedGame, folder, out var source, out _))
            {
                usable.Add((folder, source));
            }
        }

        if (usable.Count == 0)
        {
            _sink.ReportNotice(new NoInstalledLocationsFound(selectedGame));
            return;
        }

        ApplyDetectedFolders(selectedGame, usable.ToImmutable());
        await RefreshPluginsForCurrentGameContextAsync(gameContextVersion);
    }

    /// <summary>
    ///     Applies one current Browse result: detects a missing GameRelease, builds the Plugin List Source, and adopts the
    ///     directory only when both succeed, preserving an explicit release and treating suggestions as non-binding.
    /// </summary>
    /// <param name="path">The user-selected game root or Data directory.</param>
    /// <param name="gameContextVersion">The Browse resolution generation allowed to publish resulting state.</param>
    /// <returns>A task that completes after applicable detection and Plugin List discovery finish.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The detected or selected GameRelease is unsupported.</exception>
    /// <exception cref="ObjectDisposedException">The borrowed Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>
    ///     <para>
    ///         Nothing changes until a GameRelease is known and the source is built: a path detection or the source
    ///         cannot use is refused with a <see cref="DirectoryRefused" /> notice, and a directory with no detectable
    ///         game is refused with a <see cref="NoGameDetected" /> notice, both leaving the Game Context and the
    ///         Plugin List as they were. Other current unexpected detection and fatal discovery failures propagate
    ///         unchanged.
    ///     </para>
    ///     <para>
    ///         An accepted Browse whose source equals the current one is a no-op, exactly like a detected-directory
    ///         pick, unless the Plugin List for that source is in its failed state: then Browse is the user's explicit
    ///         retry, so it invalidates and refreshes again. A Browse with a different source invalidates first,
    ///         then adopts and refreshes.
    ///     </para>
    /// </remarks>
    private async Task ApplyBrowsedDirectorySelectedAsync(string path, int gameContextVersion)
    {
        var gameRelease = _current.SelectedGameRelease;
        if (gameRelease is null)
        {
            GameRelease? detectedGame;
            try
            {
                // Detection probes the file system roughly a dozen times, which a slow or network directory can turn
                // into a visible hang, and the module is synchronous by design — so this caller places it off the
                // UI thread, exactly as it does for installed-location lookup (ADR-0002).
                detectedGame = await Task.Run(() => _gameInstallations.Detect(path));
            }
            catch (Exception) when (!IsLatestIntentGeneration(gameContextVersion))
            {
                // A detector overtaken by a newer intent cannot escape into WinUI's current error boundary.
                return;
            }
            catch (Exception ex) when (IsUnusableDirectoryFailure(ex))
            {
                // Detection returns null for exactly one reason — no known game master file — and throws for a path
                // it cannot use at all. Reporting that as "could not detect game" would tell the user to pick a game
                // when the path is what is wrong (ADR-0002), so the actual failure is surfaced instead.
                RefuseDirectory(ex);
                return;
            }

            if (!IsLatestIntentGeneration(gameContextVersion))
            {
                return;
            }

            if (detectedGame is null)
            {
                // A directory is only adopted once a GameRelease is known, so this one is refused rather than kept.
                _sink.ReportNotice(new NoGameDetected());
                return;
            }

            gameRelease = detectedGame.Value;
        }

        if (!TryCreateSourceOrRefuse(gameRelease.Value, path, out var source))
        {
            return;
        }

        if (source == _current.PluginListSource && !_pluginList.Current.HasFailedFor(source))
        {
            return;
        }

        // Browse is an explicit retry, so a failed retry must not retain old confirmation. Invalidating notifies the
        // Plugin List's observers, which can re-enter and start a newer intent, hence the latest check after it.
        _pluginList.Invalidate();
        if (!IsLatestIntentGeneration(gameContextVersion))
        {
            return;
        }

        if (source != _current.PluginListSource)
        {
            Adopt(path, source, gameRelease.Value);
            // A report can re-enter and start a newer intent, so the generation is checked again.
            if (!IsLatestIntentGeneration(gameContextVersion))
            {
                return;
            }
        }

        await _pluginList.RefreshAsync(source, _current.AdvancedMode);
    }

    /// <summary>
    ///     Refreshes the held Plugin List Source or explicitly invalidates the borrowed Plugin List when none is held.
    /// </summary>
    /// <param name="expectedGameContextVersion">An optional directory-transition generation that must still be current.</param>
    /// <returns>A task that completes when applicable Plugin discovery finishes.</returns>
    /// <remarks>
    ///     The source was built when its directory was adopted, so nothing here can fail for an unusable directory.
    /// </remarks>
    private async Task RefreshPluginsForCurrentGameContextAsync(int? expectedGameContextVersion = null)
    {
        if (expectedGameContextVersion.HasValue &&
            !IsLatestIntentGeneration(expectedGameContextVersion.Value))
        {
            return;
        }

        if (_current.PluginListSource is not { } source)
        {
            _pluginList.Invalidate();
            return;
        }

        await _pluginList.RefreshAsync(source, _current.AdvancedMode);
    }

    /// <summary>
    ///     Builds the Plugin List Source for a picked directory, or refuses the pick with the one unusable-directory
    ///     notice when it cannot become one.
    /// </summary>
    /// <param name="gameRelease">The GameRelease the directory would be adopted with.</param>
    /// <param name="directory">The picked game root or Data directory.</param>
    /// <param name="source">The built source, when this returns true.</param>
    /// <returns>True when the source was built; false when the pick was refused and reported.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The GameRelease is unsupported, which is a programming error.</exception>
    private bool TryCreateSourceOrRefuse(
        GameRelease gameRelease,
        string directory,
        [NotNullWhen(true)] out PluginListSource? source)
    {
        if (TryCreateSourceSilently(gameRelease, directory, out source, out var failure))
        {
            return true;
        }

        RefuseDirectory(failure);
        return false;
    }

    /// <summary>
    ///     Builds the Plugin List Source for a directory, capturing an unusable-directory failure instead of throwing it
    ///     and reporting nothing, so the caller decides whether the failure is worth a message.
    /// </summary>
    /// <param name="gameRelease">The GameRelease the directory would be adopted with.</param>
    /// <param name="directory">A game root or Data directory.</param>
    /// <param name="source">The built source, when this returns true.</param>
    /// <param name="failure">Why the directory cannot become a source, when this returns false.</param>
    /// <returns>True when the source was built.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The GameRelease is unsupported, which is a programming error.</exception>
    private static bool TryCreateSourceSilently(
        GameRelease gameRelease,
        string directory,
        [NotNullWhen(true)] out PluginListSource? source,
        [NotNullWhen(false)] out Exception? failure)
    {
        try
        {
            // The only place Game Context code builds a source, so "the same directory" always means source equality
            // and canonicalization stays inside PluginListSource (ADR-0002, ADR-0011).
            source = PluginListSource.Create(gameRelease, directory);
            failure = null;
            return true;
        }
        catch (Exception ex) when (IsUnusableDirectoryFailure(ex))
        {
            source = null;
            failure = ex;
            return false;
        }
    }

    /// <summary>
    ///     Reports whether an exception from detection or source building means the directory itself is unusable.
    /// </summary>
    /// <param name="exception">The exception thrown for a picked directory.</param>
    /// <returns>True for a malformed, unsupported or unreadable path; false for anything else.</returns>
    /// <remarks>
    ///     <see cref="ArgumentOutOfRangeException" /> is an <see cref="ArgumentException" />, but here it only ever means
    ///     an unsupported GameRelease, which is a programming error the user cannot fix by picking another directory, so
    ///     it is left to propagate.
    /// </remarks>
    private static bool IsUnusableDirectoryFailure(Exception exception)
    {
        return exception is ArgumentException and not ArgumentOutOfRangeException
            or NotSupportedException
            or IOException;
    }

    /// <summary>
    ///     Reports a refused directory pick with the one notice every unusable directory shares.
    /// </summary>
    /// <param name="failure">Why the directory cannot be used.</param>
    private void RefuseDirectory(Exception failure)
    {
        _sink.ReportNotice(new DirectoryRefused(failure.Message));
    }

    /// <summary>
    ///     Makes a directory and the source built from it authoritative together, with the GameRelease that built it, and
    ///     reports the result.
    /// </summary>
    /// <param name="displayedDirectory">The directory as picked, which is what the UI shows.</param>
    /// <param name="source">The source built from that directory.</param>
    /// <param name="gameRelease">The GameRelease the source was built with.</param>
    /// <remarks>
    ///     Directory and source are written in one step so the invariant — both present or both absent, and both only
    ///     with a GameRelease — holds in every published state. Callers check their generation afterwards, because
    ///     a report can re-enter and start a newer intent.
    /// </remarks>
    private void Adopt(string displayedDirectory, PluginListSource source, GameRelease gameRelease)
    {
        Publish(_current with
        {
            SelectedGameRelease = gameRelease,
            SelectedGameDirectory = displayedDirectory,
            PluginListSource = source
        });
    }

    /// <summary>
    ///     Starts a new latest-intent generation, retiring every older one so its late lookup, detection or picker work
    ///     can no longer publish state or messages.
    /// </summary>
    /// <returns>The generation the caller now owns.</returns>
    /// <remarks>
    ///     Every directory-changing transition starts one, and so do <see cref="Dispose" /> (to retire everything in
    ///     flight) and a GameRelease change (to retire an older installed-location lookup). Advanced Mode changes do
    ///     not, because they leave the directory alone.
    /// </remarks>
    private int StartLatestIntentGeneration()
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
    private bool IsLatestIntentGeneration(int gameContextVersion)
    {
        return gameContextVersion == Volatile.Read(ref _gameContextVersion);
    }

    /// <summary>
    ///     Publishes a non-empty set of usable installed locations as the complete ordered available-directory snapshot,
    ///     adopting the first one.
    /// </summary>
    /// <param name="gameRelease">The GameRelease the locations were looked up and their sources built for.</param>
    /// <param name="folders">The usable installed locations in record order, each with the source built from it.</param>
    private void ApplyDetectedFolders(
        GameRelease gameRelease,
        ImmutableArray<(string Directory, PluginListSource Source)> folders)
    {
        _current = _current with
        {
            AvailableDirectories = [.. folders.Select(static folder => folder.Directory)]
        };
        Adopt(folders[0].Directory, folders[0].Source, gameRelease);
    }

    /// <summary>
    ///     Makes a new Game Context authoritative and reports it, in that order, as one step.
    /// </summary>
    /// <param name="next">The complete next state, usually <c>_current with { … }</c>.</param>
    /// <remarks>
    ///     The state is written before reporting so a report that re-enters reads the new state, which is what makes
    ///     the ComboBox echo a no-op. A report can also start a newer intent, so callers keep their own generation
    ///     checks after this returns; none happen inside it.
    /// </remarks>
    private void Publish(GameContextState next)
    {
        _current = next;
        _sink.ReportState(_current);
    }
}

/// <summary>
///     Where Game Context reports what the user selected, and why a pick was refused or nothing was found (ADR-0013).
/// </summary>
/// <remarks>
///     <para>
///         The contract is UI-thread-affine, like the rest of Game Context: every report is made from the UI thread,
///         and no lock is held while reporting. Game Context has no ordering to protect across threads, unlike
///         Processing Run Control's sink.
///     </para>
///     <para>
///         A sink adapter that the User Workflow composes implements it over the ViewModel, and tests implement it by
///         recording, so the seam has two adapters (ADR-0014).
///     </para>
/// </remarks>
internal interface IGameContextSink
{
    /// <summary>
    ///     Receives the complete authoritative Game Context, on every publication.
    /// </summary>
    /// <param name="state">
    ///     The state just made authoritative, including the Plugin List Source, which nothing shows the user.
    /// </param>
    /// <remarks>
    ///     Game Context has already written <paramref name="state" /> when this is called, so an implementation whose
    ///     handlers re-enter Game Context, such as a ComboBox echoing the projected selection back as an intent, reads
    ///     the new state, and the echo is a no-op. A re-entrant call may also start a newer intent; Game Context checks
    ///     its generation again after this returns.
    /// </remarks>
    void ReportState(GameContextState state);

    /// <summary>
    ///     Receives one thing Game Context has to tell the user, as facts rather than wording.
    /// </summary>
    /// <param name="notice">The notice, which <see cref="GameContextPresentation" /> renders.</param>
    /// <remarks>Retired work reports no notice, so every notice belongs to the latest intent.</remarks>
    void ReportNotice(GameContextNotice notice);
}

/// <summary>
///     The complete authoritative Game Context as one immutable value.
/// </summary>
/// <param name="SelectedGameRelease">The selected GameRelease, when one has been accepted.</param>
/// <param name="SelectedGameDirectory">
///     The displayed game directory as the user picked it, or null while the context is incomplete. It is kept beside
///     <paramref name="PluginListSource" /> because the source stores only the canonical Data directory, while the UI
///     shows, and the available directories contain, game roots.
/// </param>
/// <param name="PluginListSource">
///     The Plugin List Source built from the displayed directory when it was adopted, or null exactly when
///     <paramref name="SelectedGameDirectory" /> is null.
/// </param>
/// <param name="AvailableDirectories">
///     The complete ordered available-directory set, holding only install-record directories that made a source.
/// </param>
/// <param name="AdvancedMode">The current Advanced Mode value.</param>
/// <remarks>
///     Invariant, held by every state <see cref="GameContext" /> publishes: the displayed directory and the source are
///     both present or both absent, and both require a GameRelease (ADR-0011).
/// </remarks>
internal sealed record GameContextState(
    GameRelease? SelectedGameRelease,
    string? SelectedGameDirectory,
    PluginListSource? PluginListSource,
    ImmutableArray<string> AvailableDirectories,
    AdvancedMode AdvancedMode)
{
    /// <summary>
    ///     Gets the state a new Game Context starts in: nothing selected and Advanced Mode off.
    /// </summary>
    internal static GameContextState Empty { get; } = new(
        null,
        null,
        null,
        ImmutableArray<string>.Empty,
        AdvancedMode.Off);
}

/// <summary>
///     Something Game Context tells the user: why a pick was refused, or why nothing was found.
/// </summary>
/// <remarks>
///     A closed set of four value records, so the things Game Context can say are visible in one place and a test can
///     compare a reported notice against an expected one directly. The notices carry facts, not wording;
///     <see cref="GameContextPresentation" /> renders them (ADR-0013).
/// </remarks>
internal abstract record GameContextNotice;

/// <summary>
///     The game-directory picker failed instead of returning a directory or a cancellation.
/// </summary>
/// <param name="ErrorMessage">The picker's own error text.</param>
internal sealed record GameDirectoryPickerFailed(string ErrorMessage) : GameContextNotice;

/// <summary>
///     A GameRelease was selected, but none of its installed locations can become a Plugin List Source.
/// </summary>
/// <param name="GameRelease">The selected GameRelease whose locations were looked up.</param>
internal sealed record NoInstalledLocationsFound(GameRelease GameRelease) : GameContextNotice;

/// <summary>
///     A picked directory was refused because it cannot be used at all, and the Game Context was left as it was.
/// </summary>
/// <param name="FailureMessage">
///     The failure's message. It is a string rather than the exception so the notice keeps value equality and a test can
///     build the expected notice without holding the same exception instance.
/// </param>
internal sealed record DirectoryRefused(string FailureMessage) : GameContextNotice;

/// <summary>
///     A browsed directory with no GameRelease selected was refused because no game was detected in it.
/// </summary>
internal sealed record NoGameDetected : GameContextNotice;
