using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Owns the UI-neutral user workflow and the Plugin List's lifetime, forwards Game Context intents to the Game
///     Context it composes, and captures each Process FormIDs press for the Processing Run Control it composes.
/// </summary>
/// <remarks>
///     Game Context state, its transitions and its Game Installation resolution live in <see cref="GameContext" />
///     (ADR-0011). Whether a Processing Run is active, and everything a run reports, live in
///     <see cref="ProcessingRunControl" /> (ADR-0012); this workflow captures the press's inputs and performs every
///     ViewModel write that module reports, through its sink adapter.
/// </remarks>
public sealed class UserWorkflow : IDisposable
{
    private readonly IFileDialogService _fileDialogService;

    /// <summary>
    ///     The Game Context built in the constructor and retired in <see cref="Dispose" />. It borrows
    ///     <see cref="_pluginList" />, whose lifetime stays with this workflow.
    /// </summary>
    private readonly GameContext _gameContext;

    private readonly PluginList _pluginList;

    /// <summary>
    ///     Projects <see cref="_pluginList" /> into the ViewModel; built and owned here because this workflow owns the
    ///     Plugin List it observes, so the two lifetimes end together in <see cref="Dispose" />.
    /// </summary>
    private readonly PluginListPresentationAdapter _pluginListPresentationAdapter;

    /// <summary>
    ///     The single owner of whether a Processing Run is active, built in the constructor from the executor and the
    ///     default database root, and retired in <see cref="Dispose" />. It owns the executor.
    /// </summary>
    private readonly ProcessingRunControl _processingRunControl;

    private readonly MainWindowViewModel _viewModel;
    private bool _disposed;

    /// <summary>
    ///     The default database root this workflow was composed with, or null or blank when none could be resolved.
    /// </summary>
    /// <remarks>
    ///     Resolved once by <see cref="Create" /> from the local application data directory and injected, so no other
    ///     production code reads that location on its own and tests can supply a root that is never created. It is read
    ///     back from the Processing Run Control it was handed to, which is the only holder of it.
    /// </remarks>
    internal string? DefaultDatabaseRoot => _processingRunControl.DefaultDatabaseRoot;

    /// <summary>
    ///     Creates a workflow module that coordinates existing Core modules behind one UI-neutral interface.
    /// </summary>
    /// <param name="viewModel">The binding-state projection updated by workflow transitions.</param>
    /// <param name="dispatcher">
    ///     The dispatcher that owns <paramref name="viewModel" />, used by the Plugin List presentation adapter this
    ///     workflow builds and owns.
    /// </param>
    /// <param name="fileDialogService">The platform picker adapter.</param>
    /// <param name="gameInstallations">
    ///     The Game Installation resolution module, handed straight to the Game Context built here; this workflow keeps
    ///     no reference to it.
    /// </param>
    /// <param name="pluginList">The authoritative Plugin List whose lifetime transfers to this workflow.</param>
    /// <param name="processingRunExecutor">
    ///     The stateless Processing Run executor, handed straight to the Processing Run Control built here, which owns
    ///     it and hands each admitted run its token; this workflow keeps no reference to it.
    /// </param>
    /// <param name="defaultDatabaseRoot">
    ///     The root under which a real run with no chosen database path gets its default database, or null or blank when
    ///     composition could not resolve one. It is only used to compute a path; nothing is created under it here.
    /// </param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <remarks>
    ///     The Plugin List stays a parameter rather than being built here because tests observe it directly through its
    ///     own public interface. The Game Context and Processing Run Control are built after every argument check and
    ///     before the adapter; they only store their dependencies, so building them subscribes to nothing. The
    ///     presentation adapter subscribes to the Plugin List, and queues its first projection, as the last construction
    ///     step, so a failed argument check never leaves a subscribed adapter behind. An unresolved default root is
    ///     accepted rather than rejected, because it only matters to a run that needs a default, and that run reports it.
    /// </remarks>
    internal UserWorkflow(
        MainWindowViewModel viewModel,
        IThreadDispatcher dispatcher,
        IFileDialogService fileDialogService,
        GameInstallations gameInstallations,
        PluginList pluginList,
        IProcessingRunExecutor processingRunExecutor,
        string? defaultDatabaseRoot)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        var checkedDispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        var checkedGameInstallations =
            gameInstallations ?? throw new ArgumentNullException(nameof(gameInstallations));
        _pluginList = pluginList ?? throw new ArgumentNullException(nameof(pluginList));
        var checkedProcessingRunExecutor = processingRunExecutor ??
                                           throw new ArgumentNullException(nameof(processingRunExecutor));
        _gameContext = new GameContext(checkedGameInstallations, _fileDialogService, _pluginList, _viewModel);
        _processingRunControl = new ProcessingRunControl(
            checkedProcessingRunExecutor,
            defaultDatabaseRoot,
            new ViewModelProcessingRunControlSink(_viewModel));
        _pluginListPresentationAdapter = new PluginListPresentationAdapter(_pluginList, _viewModel, checkedDispatcher);
    }

    /// <summary>
    ///     Builds the production User Workflow and the main window's Core object graph from the platform-supplied pieces.
    /// </summary>
    /// <param name="viewModel">The binding-state projection updated by workflow transitions.</param>
    /// <param name="dispatcher">The dispatcher that owns <paramref name="viewModel" />.</param>
    /// <param name="fileDialogService">The platform picker adapter.</param>
    /// <returns>
    ///     A workflow that owns every collaborator built here, including the Plugin List presentation adapter, and
    ///     retires them all in its own <see cref="Dispose" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    ///     <para>
    ///         This is the only production composition. It wires Game Installation resolution over the production
    ///         installation probe, one Game Load Orders instance shared by the Plugin List and Plugin Ingestion
    ///         (ADR-0008), and the Processing Run executor over Plugin Ingestion and the production FormID Record Store
    ///         session opener, which keeps Store opening and disposal per-run inside the executor (ADR-0001). Every
    ///         constructor involved only stores its dependencies, so building the graph reads no game installation,
    ///         load order, or file.
    ///     </para>
    ///     <para>
    ///         Composition lives here rather than in a separate composition module because such a module fails the
    ///         deletion test: it would hold three references and make three dispose calls, which is shallow. This
    ///         workflow already owns the Plugin List's lifetime, so owning the rest of the graph concentrates the
    ///         concept instead of spreading it across a pass-through.
    ///     </para>
    /// </remarks>
    public static UserWorkflow Create(
        MainWindowViewModel viewModel,
        IThreadDispatcher dispatcher,
        IFileDialogService fileDialogService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(fileDialogService);

        // One Game Load Orders instance serves both caller roles. Sharing it is economy rather than a correctness
        // rule: Plugin Ingestion's own constructor already pairs it with the matched production overlay reader.
        var gameLoadOrders = new GameLoadOrders();
        var processingRunExecutor = new ProcessingRunExecutor(
            new PluginIngestion(gameLoadOrders),
            new FormIdRecordStoreSessionOpener());
        var pluginList = new PluginList(gameLoadOrders);

        // The user's profile location is read here and only here, once, so the workflow and everything it calls
        // receive the default database root as a value. Resolving it creates nothing; the FormID Record Store creates
        // the database's folder when a real run opens it (ADR-0001). A blank result is passed through unchanged and
        // only surfaces if a run actually needs a default path.
        var defaultDatabaseRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        try
        {
            return new UserWorkflow(
                viewModel,
                dispatcher,
                fileDialogService,
                new GameInstallations(new GameInstallationProbe()),
                pluginList,
                processingRunExecutor,
                defaultDatabaseRoot);
        }
        catch
        {
            // Ownership only transfers once the constructor returns, so a constructor failure would otherwise leak the
            // disposables built above. Cleanup is best-effort and must not replace the constructor's exception.
            DisposeIgnoringFailure(pluginList);
            throw;
        }
    }

    /// <summary>
    ///     Disposes a collaborator while unwinding a failed composition, discarding any cleanup failure.
    /// </summary>
    /// <param name="disposable">The collaborator built by <see cref="Create" /> whose ownership never transferred.</param>
    private static void DisposeIgnoringFailure(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch
        {
            // Swallowed on purpose: the caller is already rethrowing the composition failure, which is the one worth
            // reporting, and a cleanup failure here has no other observer.
        }
    }

    /// <summary>
    ///     Detaches Plugin List presentation, cancels processing, and releases resources owned by the workflow.
    /// </summary>
    /// <remarks>
    ///     The steps run in a fixed order: detach the Plugin List presentation adapter, retire the Game Context, retire
    ///     Processing Run Control, which cancels the active Processing Run, then dispose the Plugin List. Cancelling does
    ///     not wait for the run to end, so closing the window cannot hang on it; the run still releases its own
    ///     cancellation source as it ends. Every owned collaborator is retired even when an earlier step fails. Cleanup
    ///     after a failure is best-effort, matching Plugin ingestion overlay cleanup: the first failure keeps its
    ///     identity, and a standalone cleanup failure still propagates because no other path reports it.
    /// </remarks>
    /// <exception cref="AggregateException">A registered Processing Run cancellation callback throws.</exception>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Exception? primaryException = null;
        // Detach presentation before anything else, and in particular before the workflow retires its authoritative
        // Plugin List. A projection already queued on the dispatcher would otherwise still write to the ViewModel
        // during teardown — for example when cancelling the active run lets the dispatcher run — and a refresh
        // publishing from another thread could queue a new one. Disposing the Plugin List itself publishes no state,
        // so these queued-projection and cross-thread publisher windows are the only ones this ordering guards.
        RetireCollaborator(_pluginListPresentationAdapter.Dispose, ref primaryException);
        // Retire the Game Context before cancelling the run and disposing the Plugin List so late lookup or picker
        // work cannot publish into torn-down state. It borrows the Plugin List, so retiring it disposes nothing.
        RetireCollaborator(_gameContext.Dispose, ref primaryException);
        RetireCollaborator(_processingRunControl.Dispose, ref primaryException);
        RetireCollaborator(_pluginList.Dispose, ref primaryException);
        if (primaryException is not null)
        {
            // Rethrow through the dispatch info so the caller still sees the original throw site.
            ExceptionDispatchInfo.Throw(primaryException);
        }
    }

    /// <summary>
    ///     Forwards an explicit GameRelease selection to the Game Context.
    /// </summary>
    /// <param name="selectedGameRelease">The selected GameRelease, or null when the selection is cleared.</param>
    /// <returns>A task that completes after current installed-location resolution and Plugin List refresh finish.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The selected GameRelease is unsupported.</exception>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>See <see cref="GameContext.SelectGameReleaseAsync" /> for the transition itself.</remarks>
    internal Task SelectGameReleaseAsync(GameRelease? selectedGameRelease)
    {
        return _gameContext.SelectGameReleaseAsync(selectedGameRelease);
    }

    /// <summary>
    ///     Forwards an explicit detected-directory selection to the Game Context.
    /// </summary>
    /// <param name="selectedDirectory">The selected directory, or null when the selection is cleared.</param>
    /// <returns>A task that completes after current Plugin List discovery finishes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The authoritative GameRelease is unsupported.</exception>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>See <see cref="GameContext.SelectDetectedDirectoryAsync" /> for the transition itself.</remarks>
    internal Task SelectDetectedDirectoryAsync(string? selectedDirectory)
    {
        return _gameContext.SelectDetectedDirectoryAsync(selectedDirectory);
    }

    /// <summary>
    ///     Forwards an explicit Advanced Mode value to the Game Context.
    /// </summary>
    /// <param name="advancedMode">The Advanced Mode value selected by the user.</param>
    /// <returns>A task that completes after applicable Plugin List discovery finishes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The Advanced Mode or authoritative GameRelease is unsupported, or discovery returns an unsupported result.
    /// </exception>
    /// <exception cref="OperationCanceledException">Current Plugin discovery propagates an unexpected cancellation.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    /// <remarks>See <see cref="GameContext.SetAdvancedModeAsync" /> for the transition itself.</remarks>
    internal Task SetAdvancedModeAsync(AdvancedMode advancedMode)
    {
        return _gameContext.SetAdvancedModeAsync(advancedMode);
    }

    /// <summary>
    ///     Forwards a Browse press to the Game Context, which opens the game-directory picker itself.
    /// </summary>
    /// <returns>A task that completes after picker handling and plugin refresh finish.</returns>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>
    ///     The picker is opened by the Game Context rather than here, because opening it must retire older lookups
    ///     before the dialog returns (ADR-0011). Current unexpected picker, detection, and fatal discovery failures
    ///     propagate to WinUI's final boundary.
    /// </remarks>
    public Task BrowseGameDirectoryAsync()
    {
        return _gameContext.BrowseGameDirectoryAsync();
    }

    /// <summary>
    ///     Selects a database path without overwriting state when the picker is cancelled.
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
    ///     Selects an optional FormID text file without overwriting state when the picker is cancelled.
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
    ///     Selects every currently loaded plugin.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    public void SelectAllPlugins()
    {
        ApplyWholeListSelection(true);
    }

    /// <summary>
    ///     Clears selection for every currently loaded plugin.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    public void SelectNoPlugins()
    {
        ApplyWholeListSelection(false);
    }

    /// <summary>
    ///     Submits one user-activated, versioned Plugin selection change to the authoritative Plugin List.
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
    ///     Starts processing or requests cancellation for the active processing run.
    /// </summary>
    /// <returns>A task that completes after processing starts, finishes, fails, or observes cancellation.</returns>
    /// <exception cref="ObjectDisposedException">The workflow has been disposed.</exception>
    /// <remarks>
    ///     Inputs are fixed here, before Processing Run Control publishes anything about the press, so an edit made
    ///     from a startup notification belongs to the next run. Whether the press starts a run, is refused, or cancels
    ///     the active run is Processing Run Control's decision (ADR-0012); what it reports reaches the ViewModel through
    ///     this workflow's sink adapter.
    /// </remarks>
    public async Task ProcessFormIdsAsync()
    {
        // Capture needs no lock: every press arrives on the UI thread, and a press re-entering from a notification
        // only has to find the run active, which Processing Run Control decides under its own lock. The whole Plugin
        // List state is captured, not just its confirmation, so admission can tell a list still loading from one that
        // failed to load. The Game Context is read once, so the release and Plugin List Source always come from the
        // same state.
        var gameContext = _gameContext.Current;
        var inputs = new ProcessingRunInputs(
            gameContext.SelectedGameRelease,
            gameContext.PluginListSource,
            _pluginList.Current,
            _viewModel.FormIdListPath,
            _viewModel.DatabasePath,
            _viewModel.UpdateMode ? UpdateMode.ReplacePluginRecords : UpdateMode.Append,
            _viewModel.DryRun);

        await _processingRunControl.PressAsync(inputs);
    }

    /// <summary>
    ///     Runs one disposal step, keeping the first failure as the exception the caller finally observes.
    /// </summary>
    /// <param name="step">The cleanup action to run.</param>
    /// <param name="primaryException">
    ///     The failure already in flight, replaced only when <paramref name="step" /> raises the first one.
    /// </param>
    private static void RetireCollaborator(Action step, ref Exception? primaryException)
    {
        try
        {
            step();
        }
        catch (Exception ex) when (primaryException is null)
        {
            // The first failure becomes the primary exception, but later steps still run so nothing is left live.
            primaryException = ex;
        }
        catch
        {
            // A later cleanup failure cannot replace the primary exception's identity.
        }
    }

    private static bool IsFailure(FileDialogResult result)
    {
        return result.Kind == FileDialogResultKind.Failure;
    }

    /// <summary>
    ///     Reads one database or FormID-list picker result, reporting a picker failure in this workflow's own wording.
    /// </summary>
    /// <param name="result">The picker outcome.</param>
    /// <param name="failureContext">The prefix of the error shown when the picker failed.</param>
    /// <param name="path">The selected path when this returns true; otherwise null.</param>
    /// <returns>True only when the user selected a path.</returns>
    /// <remarks>
    ///     The "selected a non-empty path" check is <see cref="FileDialogResult.TryGetSelectedPath" />, shared with the
    ///     Game Context's Browse; only the failure wording is this caller's.
    /// </remarks>
    private bool TryGetSelectedPath(
        FileDialogResult result,
        string failureContext,
        [NotNullWhen(true)] out string? path)
    {
        if (result.TryGetSelectedPath(out path))
        {
            return true;
        }

        if (IsFailure(result))
        {
            _viewModel.AddErrorMessage($"{failureContext}: {result.ErrorMessage}");
        }

        return false;
    }

    /// <summary>
    ///     Submits a versioned whole-list selection intent against one captured confirmed membership.
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
    ///     The User Workflow's adapter on the Processing Run Control sink: it performs every ViewModel write a Processing
    ///     Run reports (ADR-0012).
    /// </summary>
    /// <param name="viewModel">The ViewModel the reports are written to.</param>
    /// <remarks>
    ///     Processing Run Control calls each member while holding its lock, so these writes reach the ViewModel in the
    ///     order they were sequenced (#60). Any of them may raise a notification that re-enters a press.
    /// </remarks>
    private sealed class ViewModelProcessingRunControlSink(MainWindowViewModel viewModel) : IProcessingRunControlSink
    {
        /// <inheritdoc />
        public void ClearMessageLists()
        {
            viewModel.ErrorMessages.Clear();
            viewModel.WarningMessages.Clear();
        }

        /// <inheritdoc />
        /// <remarks>
        ///     The admitted run still uses its default, but a re-entrant edit made during startup notifications must
        ///     survive for the next run, so the default is written back only while the box still holds the capture.
        /// </remarks>
        public void ReportDefaultedDatabasePath(string defaultedDatabasePath, string? capturedDatabasePath)
        {
            if (string.Equals(viewModel.DatabasePath, capturedDatabasePath, StringComparison.Ordinal))
            {
                viewModel.DatabasePath = defaultedDatabasePath;
            }
        }

        /// <inheritdoc />
        public void ReportRunActivity(ActivityProjection runActivity)
        {
            viewModel.ApplyRunActivityProjection(runActivity);
        }

        /// <inheritdoc />
        public void AddWarningMessage(string message)
        {
            viewModel.AddWarningMessage(message);
        }

        /// <inheritdoc />
        public void AddErrorMessage(string message)
        {
            viewModel.AddErrorMessage(message);
        }

        /// <inheritdoc />
        public void AddInformationMessage(string message)
        {
            viewModel.AddInformationMessage(message);
        }
    }
}
