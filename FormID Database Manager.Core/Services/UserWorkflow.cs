using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Owns authoritative Game Context, the UI-neutral user workflow, Plugin List lifetime, and Processing Run
///     coordination.
/// </summary>
public sealed class UserWorkflow : IDisposable
{
    private readonly IFileDialogService _fileDialogService;
    private readonly GameInstallations _gameInstallations;
    private readonly PluginList _pluginList;

    /// <summary>
    ///     Projects <see cref="_pluginList" /> into the ViewModel; built and owned here because this workflow owns the
    ///     Plugin List it observes, so the two lifetimes end together in <see cref="Dispose" />.
    /// </summary>
    private readonly PluginListPresentationAdapter _pluginListPresentationAdapter;

    private readonly IProcessingRunExecutor _processingRunExecutor;

    /// <summary>
    ///     Guards the run activity this workflow holds as its state of record, because a Processing Run reports its
    ///     progress from whatever thread the executor is on while the UI thread reads it to decide what a button press means.
    /// </summary>
    private readonly Lock _runActivityLock = new();

    private readonly MainWindowViewModel _viewModel;
    private bool _disposed;

    private GameContextSnapshot _gameContext;
    private int _gameContextVersion;

    /// <summary>
    ///     The Processing Run's Workflow Activity, owned here rather than read back off the ViewModel.
    /// </summary>
    /// <remarks>
    ///     This has to be the source of truth: an admitted run publishes activity, clears the message lists and writes a
    ///     defaulted database path back before the executor is handed the run, so there is a window in which this
    ///     workflow is committed to a run while the executor's cancellation source does not yet exist. Owning the fact
    ///     closes that window, so a second button press inside it cancels instead of starting a second run. A press that
    ///     Processing Run Admission refuses never sets it.
    /// </remarks>
    private ActivityProjection _runActivity = ActivityProjection.None;

    /// <summary>
    ///     The default database root this workflow was composed with, or null or blank when none could be resolved.
    /// </summary>
    /// <remarks>
    ///     Resolved once by <see cref="Create" /> from the local application data directory and injected, so no other
    ///     production code reads that location on its own and tests can supply a root that is never created.
    /// </remarks>
    internal string? DefaultDatabaseRoot { get; }

    /// <summary>
    ///     Creates a workflow module that coordinates existing Core modules behind one UI-neutral interface.
    /// </summary>
    /// <param name="viewModel">The binding-state projection updated by workflow transitions.</param>
    /// <param name="dispatcher">
    ///     The dispatcher that owns <paramref name="viewModel" />, used by the Plugin List presentation adapter this
    ///     workflow builds and owns.
    /// </param>
    /// <param name="fileDialogService">The platform picker adapter.</param>
    /// <param name="gameInstallations">The Game Installation resolution module, used for detection and location.</param>
    /// <param name="pluginList">The authoritative Plugin List whose lifetime transfers to this workflow.</param>
    /// <param name="processingRunExecutor">The owned Processing Run executor.</param>
    /// <param name="defaultDatabaseRoot">
    ///     The root under which a real run with no chosen database path gets its default database, or null or blank when
    ///     composition could not resolve one. It is only used to compute a path; nothing is created under it here.
    /// </param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <remarks>
    ///     The Plugin List stays a parameter rather than being built here because tests observe it directly through its
    ///     own public interface. The presentation adapter subscribes to it, and queues its first projection, as the last
    ///     construction step, so a failed argument check never leaves a subscribed adapter behind. An unresolved
    ///     default root is accepted rather than rejected, because it only matters to a run that needs a default, and
    ///     that run reports it.
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
        _gameInstallations = gameInstallations ?? throw new ArgumentNullException(nameof(gameInstallations));
        _pluginList = pluginList ?? throw new ArgumentNullException(nameof(pluginList));
        _processingRunExecutor = processingRunExecutor ??
                                 throw new ArgumentNullException(nameof(processingRunExecutor));
        DefaultDatabaseRoot = defaultDatabaseRoot;
        _gameContext = new GameContextSnapshot(
            null,
            null,
            ImmutableArray<string>.Empty,
            AdvancedMode.Off);
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
            DisposeIgnoringFailure(processingRunExecutor);
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
    ///     The steps run in a fixed order: detach the Plugin List presentation adapter, retire Game Context resolution,
    ///     cancel the Processing Run executor, dispose the executor, then dispose the Plugin List. Every owned
    ///     collaborator is retired even when an earlier step fails. Cleanup after a failure is best-effort, matching
    ///     Plugin ingestion overlay cleanup: the first failure keeps its identity, and a standalone cleanup failure still
    ///     propagates because no other path reports it.
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
        // during teardown — for example when cancelling the executor lets the dispatcher run — and a refresh
        // publishing from another thread could queue a new one. Disposing the Plugin List itself publishes no state,
        // so these queued-projection and cross-thread publisher windows are the only ones this ordering guards.
        RetireCollaborator(_pluginListPresentationAdapter.Dispose, ref primaryException);
        // Retire resolution before cancelling and disposing the executor and Plugin List so late lookup or picker work
        // cannot publish into torn-down state.
        RetireCollaborator(() => Interlocked.Increment(ref _gameContextVersion), ref primaryException);
        RetireCollaborator(_processingRunExecutor.Cancel, ref primaryException);
        RetireCollaborator(_processingRunExecutor.Dispose, ref primaryException);
        RetireCollaborator(_pluginList.Dispose, ref primaryException);
        if (primaryException is not null)
        {
            // Rethrow through the dispatch info so the caller still sees the original throw site.
            ExceptionDispatchInfo.Throw(primaryException);
        }
    }

    /// <summary>
    ///     Makes an explicit GameRelease selection authoritative before resolving its installed locations.
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
    ///     Makes an explicit detected-directory selection authoritative before refreshing the Plugin List.
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
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
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
    ///     Opens the game-directory picker as a latest-intent operation, detects a missing GameRelease, and refreshes the
    ///     Plugin List.
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
    /// <remarks>
    ///     Inputs are fixed when this workflow accepts the run, before any startup notification can edit them.
    ///     How the run ended — cancelled or failed — is reported to the message lists, never to the progress channel,
    ///     because this method's own cleanup hands that channel back as soon as the run ends.
    /// </remarks>
    public async Task ProcessFormIdsAsync()
    {
        // The run-active check, Processing Run Admission and the transition into an active run happen together, so a
        // second press arriving before the executor has the run cancels the run in flight rather than starting another.
        var press = DecideRunPress();
        if (press is RunPress.CancelActiveRun)
        {
            // "Cancelling..." is what the run is doing now, not how it ended, so it belongs on the transient channel:
            // it is deliberately not the acknowledgement, which the cancellation handler below writes instead (#60).
            ReportRunActivity("Cancelling...", null);
            _processingRunExecutor.Cancel();
            return;
        }

        // Every press that does not cancel starts the message lists over, whether it is refused, fails admission or
        // becomes a run, so what the user reads afterwards is only what this press produced.
        _viewModel.ErrorMessages.Clear();
        _viewModel.WarningMessages.Clear();

        switch (press)
        {
            case RunPress.AdmissionFailed failed:
                // Admission throws only when no Plugin List Source can be built from the captured Game Context, such
                // as a malformed directory Browse kept. It threw before any activity was published, so the gate is
                // still free and there is no activity to hand back; the failure is shown as the unexpected one it is,
                // exactly as it was before admission ran inside the gate (ADR-0002).
                _viewModel.AddErrorMessage(ProcessingRunPresentation.RenderUnexpectedFailure(failed.Failure));
                return;
            case RunPress.Refused refused:
                // A refused press never became a run, so it took no ownership of the gate and has no activity to hand
                // back: the rendered reason is the only thing it leaves behind (ADR-0010).
                _viewModel.AddErrorMessage(ProcessingRunPresentation.Render(refused.Reason));
                return;
            case RunPress.Admitted admitted:
                await ExecuteAdmittedRunAsync(admitted);
                return;
            default:
                throw new UnreachableException($"Unsupported run press: {press}.");
        }
    }

    /// <summary>
    ///     Executes a run this workflow has already admitted and published activity for, reports how it ended, and
    ///     hands the progress channel back.
    /// </summary>
    /// <param name="admitted">The admitted run and the database path captured with it.</param>
    /// <returns>A task that completes after the run finishes, fails, or observes cancellation.</returns>
    /// <remarks>
    ///     The caller must already own the run gate's activity: this method's <c>finally</c> clears it, which is only
    ///     correct for the press that published it.
    /// </remarks>
    private async Task ExecuteAdmittedRunAsync(RunPress.Admitted admitted)
    {
        try
        {
            // The admitted run still uses its default, but a reentrant edit made during startup notifications must
            // survive for the next run, so the default is written back only while the box still holds the capture.
            if (admitted.Run.DefaultedDatabasePath is { } defaultedDatabasePath &&
                string.Equals(_viewModel.DatabasePath, admitted.CapturedDatabasePath, StringComparison.Ordinal))
            {
                _viewModel.DatabasePath = defaultedDatabasePath;
            }

            var progress = new ProcessingRunProgressAdapter(ApplyProcessingRunProgress);

            var outcome = await _processingRunExecutor.ExecuteAsync(admitted.Run.Request, progress);
            ApplyProcessingRunOutcome(outcome);
        }
        // The failure whose own message already tells the user what is wrong and what to do about it, shown
        // unwrapped: the generic "Error processing FormIDs" prefix below would only bury it (ADR-0006).
        catch (UnresolvableMasterException ex)
        {
            _viewModel.AddErrorMessage(ex.Message);
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage(ProcessingRunPresentation.RenderUnexpectedFailure(ex));
        }
        finally
        {
            // Clearing run activity hands the progress channel back rather than blanking it: a Plugin List refresh
            // still scanning underneath this run keeps showing its own progress, by the ViewModel's precedence rule.
            ProjectRunActivity(ActivityProjection.None);
        }
    }

    /// <summary>
    ///     Decides what one press of Process FormIDs is, inside the run gate: a cancellation request when a run is
    ///     already active, and otherwise whatever Processing Run Admission makes of the inputs captured now.
    /// </summary>
    /// <returns>
    ///     <see cref="RunPress.CancelActiveRun" /> when a run is already active;
    ///     <see cref="RunPress.AdmissionFailed" /> when admission could not build a Plugin List Source from the captured
    ///     Game Context; <see cref="RunPress.Refused" /> when admission refused the press; or
    ///     <see cref="RunPress.Admitted" />, the only case in which run activity was published and the caller now owns a
    ///     new run.
    /// </returns>
    /// <remarks>
    ///     Capture, admission and activity publication share the run gate, so nothing published in between can change
    ///     what was admitted, and a refused press never becomes an active run. Only admission's own failure is captured:
    ///     anything a re-entrant activity notification throws still propagates, as it did before admission existed.
    /// </remarks>
    private RunPress DecideRunPress()
    {
        lock (_runActivityLock)
        {
            if (_runActivity.IsActive)
            {
                return new RunPress.CancelActiveRun();
            }

            // Both activity and message-list notifications can re-enter the workflow. Capture every input before
            // either is published so later edits belong to the next run, including changes to the Plugin List source.
            // The whole Plugin List state is captured, not just its confirmation, so admission can tell a list still
            // loading from one that failed to load.
            var inputs = new ProcessingRunInputs(
                _gameContext.SelectedGameRelease,
                _gameContext.SelectedGameDirectory,
                _pluginList.Current,
                _viewModel.FormIdListPath,
                _viewModel.DatabasePath,
                _viewModel.UpdateMode ? UpdateMode.ReplacePluginRecords : UpdateMode.Append,
                _viewModel.DryRun);
            RunAdmissionDecision decision;
            try
            {
                decision = ProcessingRunAdmission.Admit(inputs, DefaultDatabaseRoot);
            }
            catch (ArgumentException ex)
            {
                // Admission runs before any activity is published, so the gate is still free here; the caller reports
                // the failure after the lock is released, as it reports a refusal.
                return new RunPress.AdmissionFailed(ex);
            }

            switch (decision)
            {
                case AdmittedRun admitted:
                    ProjectRunActivityLocked(new ActivityProjection(true, "Initializing...", 0));
                    return new RunPress.Admitted(admitted, inputs.DatabasePath);
                case RefusedRun refused:
                    return new RunPress.Refused(refused.Reason);
                default:
                    throw new UnreachableException($"Unsupported admission decision: {decision}.");
            }
        }
    }

    /// <summary>
    ///     Makes one complete run report authoritative and projects it.
    /// </summary>
    /// <param name="runActivity">The complete run report to publish.</param>
    private void ProjectRunActivity(ActivityProjection runActivity)
    {
        lock (_runActivityLock)
        {
            ProjectRunActivityLocked(runActivity);
        }
    }

    /// <summary>
    ///     Publishes new run activity, carrying the last reported progress value forward when the report omits one.
    /// </summary>
    /// <param name="status">The already-rendered status text for the run.</param>
    /// <param name="value">The reported progress percentage, or null to keep the value already on screen.</param>
    /// <remarks>
    ///     Whether the run is active is never changed here: a report arriving after the run has ended stays inactive and
    ///     therefore stays off the channel.
    /// </remarks>
    private void ReportRunActivity(string status, double? value)
    {
        lock (_runActivityLock)
        {
            ProjectRunActivityLocked(_runActivity with { Status = status, Value = value ?? _runActivity.Value });
        }
    }

    /// <summary>
    ///     Stores one run report and projects it, with the caller already holding <see cref="_runActivityLock" />.
    /// </summary>
    /// <param name="runActivity">The complete run report that becomes authoritative.</param>
    /// <remarks>
    ///     The projection happens under the lock on purpose. Releasing first would let two reports racing from the UI
    ///     thread and the executor thread reach the ViewModel in the opposite order to which they were sequenced here,
    ///     leaving a stale status on the channel. Holding it across the call is safe because a projection either posts to
    ///     the dispatcher without running anything else, or runs on the dispatcher and raises notifications whose handlers
    ///     may re-enter this workflow — and the lock is reentrant, so a handler pressing the process button still sees the
    ///     run it is nested inside as active.
    /// </remarks>
    private void ProjectRunActivityLocked(ActivityProjection runActivity)
    {
        _runActivity = runActivity;
        _viewModel.ApplyRunActivityProjection(runActivity);
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
    /// <exception cref="ObjectDisposedException">The workflow-owned Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered Plugin List refresh cancellation callback throws.</exception>
    /// <remarks>
    ///     A path detection cannot use at all is presented as an error describing that failure; other current unexpected
    ///     detection and fatal discovery failures propagate unchanged.
    /// </remarks>
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
    ///     Refreshes the exact complete Game Context or explicitly invalidates the authoritative Plugin List.
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

        if (_gameContext.SelectedGameRelease is not { } game ||
            _gameContext.SelectedGameDirectory is not { } gameDirectory)
        {
            _pluginList.Invalidate();
            return;
        }

        // The Plugin List no longer validates a directory, so the source is built here, immediately before the
        // refresh, keeping today's ArgumentException for an unusable directory until the Game Context builds it
        // when the directory is adopted (ADR-0011).
        await _pluginList.RefreshAsync(
            PluginListSource.Create(game, gameDirectory),
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
    ///     Publishes a non-empty installed-location result as the complete ordered available-directory snapshot.
    /// </summary>
    /// <param name="folders">The installed locations in record order.</param>
    private void ApplyDetectedFolders(ImmutableArray<string> folders)
    {
        _gameContext = _gameContext with { SelectedGameDirectory = folders[0], AvailableDirectories = folders };
        ProjectGameContext();
    }

    /// <summary>
    ///     Publishes the complete authoritative Game Context through the ViewModel's one restricted projection seam.
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
    ///     Converts the presentation value to the nullable directory used by the domain snapshot.
    /// </summary>
    /// <param name="directory">The existing presentation directory value.</param>
    /// <returns>The domain directory, or null when the presentation value represents absence.</returns>
    private static string? ToDomainDirectory(string? directory)
    {
        return string.IsNullOrEmpty(directory) ? null : directory;
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
    ///     Renders how the run ended and performs every ViewModel write that outcome implies.
    /// </summary>
    /// <param name="outcome">The outcome returned by the Processing Run this workflow started.</param>
    /// <remarks>
    ///     <see cref="ProcessingRunPresentation" /> is a pure renderer, so the order the run's terminal facts reach the
    ///     user is decided here: the message lists first and the transient status last, matching the order the executor
    ///     reported them in before it stopped doing its own wording. An inactive projection means the outcome has nothing
    ///     to say on that channel — a cancelled run's acknowledgement is a terminal fact, and writing it here would only
    ///     have it erased by this method's caller as it hands the channel back (#60).
    /// </remarks>
    private void ApplyProcessingRunOutcome(ProcessingRunOutcome outcome)
    {
        var rendered = ProcessingRunPresentation.Render(outcome);

        foreach (var message in rendered.WarningMessages)
        {
            _viewModel.AddWarningMessage(message);
        }

        foreach (var message in rendered.ErrorMessages)
        {
            _viewModel.AddErrorMessage(message);
        }

        foreach (var message in rendered.InformationMessages)
        {
            _viewModel.AddInformationMessage(message);
        }

        if (rendered.Activity.IsActive)
        {
            ReportRunActivity(rendered.Activity.Status, rendered.Activity.Value);
        }
    }

    /// <summary>
    ///     Renders one transient report from the run's progress channel and applies it.
    /// </summary>
    /// <param name="runProgress">What the run said it is doing.</param>
    /// <remarks>
    ///     A run reports transient progress here and nothing else: how it ended is rendered from the outcome it returns.
    ///     Applying the render through <see cref="ReportRunActivity" /> is what keeps a report arriving after the run has
    ///     ended off the channel, and what carries the last percentage forward for a report that implies no new one.
    /// </remarks>
    private void ApplyProcessingRunProgress(ProcessingRunProgress runProgress)
    {
        var rendered = ProcessingRunPresentation.Render(runProgress);
        ReportRunActivity(rendered.Status, rendered.Value);
    }

    /// <summary>
    ///     Retains the complete User Workflow-owned Game Context as one immutable value.
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

    /// <summary>
    ///     What one press of Process FormIDs turned out to be once the run gate decided it.
    /// </summary>
    /// <remarks>
    ///     The constructor is private, so the four cases nested here are the only ones and the caller's default branch is
    ///     unreachable. Only <see cref="Admitted" /> means run activity was published and the caller owns a new run.
    /// </remarks>
    private abstract record RunPress
    {
        private RunPress()
        {
        }

        /// <summary>A run was already active, so the press requests its cancellation instead.</summary>
        public sealed record CancelActiveRun : RunPress;

        /// <summary>Admission could not build a Plugin List Source from the captured Game Context.</summary>
        /// <param name="Failure">The failure admission raised, shown to the user as an unexpected one.</param>
        public sealed record AdmissionFailed(ArgumentException Failure) : RunPress;

        /// <summary>Processing Run Admission refused the press before any run existed.</summary>
        /// <param name="Reason">Why the press was refused.</param>
        public sealed record Refused(RunRejectionReason Reason) : RunPress;

        /// <summary>Processing Run Admission admitted the press, and run activity has been published for it.</summary>
        /// <param name="Run">The admitted request and any database path admission defaulted.</param>
        /// <param name="CapturedDatabasePath">
        ///     The database path captured with the press, which guards writing a defaulted path back to the ViewModel.
        /// </param>
        public sealed record Admitted(AdmittedRun Run, string? CapturedDatabasePath) : RunPress;
    }

    private sealed class ProcessingRunProgressAdapter(Action<ProcessingRunProgress> handler)
        : IProgress<ProcessingRunProgress>
    {
        public void Report(ProcessingRunProgress value)
        {
            handler(value);
        }
    }
}
