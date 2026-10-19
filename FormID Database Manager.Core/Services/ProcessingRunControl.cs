using System.Diagnostics;
using FormID_Database_Manager.ViewModels;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Processing Run Control: the single owner of whether a Processing Run is active (ADR-0012). It decides each press
///     of Process FormIDs — start the run Processing Run Admission admits, report a refusal, or cancel the active run —
///     and reports everything a run shows the user to its sink, writing none of it itself.
/// </summary>
/// <remarks>
///     <para>
///         Inputs arrive already captured, so this module reads no Game Context, Plugin List, or ViewModel. Every
///         sentence it reports is rendered by <see cref="ProcessingRunPresentation" />; it contains no wording of its
///         own.
///     </para>
///     <para>
///         It owns the executor it is composed with: it is the executor's only caller, and it creates every token the
///         executor is handed, which is what keeps cancellation precisely attributable (ADR-0007).
///     </para>
/// </remarks>
internal sealed class ProcessingRunControl : IDisposable
{
    private readonly string? _defaultDatabaseRoot;
    private readonly IProcessingRunExecutor _executor;

    /// <summary>
    ///     Guards the run activity this module holds as its state of record, the active run's cancellation source, and
    ///     disposal, because a Processing Run reports its progress from whatever thread the executor is on while the UI
    ///     thread decides what a press means.
    /// </summary>
    /// <remarks>
    ///     Every sink call happens while this lock is held. Releasing it first would let two reports racing from the UI
    ///     thread and the executor thread reach the sink in the opposite order to which they were sequenced here,
    ///     leaving a stale status on the channel (#60). Holding it across the call is safe because the sink's adapter
    ///     either posts to the dispatcher without running anything else, or runs on the dispatcher and raises
    ///     notifications whose handlers may re-enter this module — and the lock is re-entrant, so a handler pressing the
    ///     button again still finds the run it is nested inside active.
    /// </remarks>
    private readonly Lock _lock = new();

    private readonly IProcessingRunControlSink _sink;

    private bool _disposed;

    /// <summary>
    ///     The active Processing Run's cancellation source, or null while no run is active. Guarded by
    ///     <see cref="_lock" />; non-null exactly while <see cref="_runActivity" /> is active.
    /// </summary>
    /// <remarks>
    ///     It is created inside the lock the moment admission admits a press, before anything about the run reaches the
    ///     sink, so a press re-entering from any startup report finds a token to cancel and the run really is cancelled
    ///     (#104). It is cancelled only while the lock is held, and released by the run that owns it only after
    ///     clearing it under the lock, so a cancel can never reach a disposed source.
    /// </remarks>
    private CancellationTokenSource? _runCancellationSource;

    /// <summary>
    ///     The Processing Run's rendered Workflow Activity, held here rather than read back from the sink.
    /// </summary>
    /// <remarks>
    ///     It is the rendered activity, not a typed fact, because a report rendered with no new percentage carries the
    ///     one already shown forward, and only the rendered value knows what that is (ADR-0012). A press that admission
    ///     refuses never makes it active.
    /// </remarks>
    private ActivityProjection _runActivity = ActivityProjection.None;

    /// <summary>
    ///     Creates the module that decides presses and owns the active run.
    /// </summary>
    /// <param name="executor">
    ///     The stateless executor each admitted run is handed to, with the token this module owns. Its lifetime
    ///     transfers here.
    /// </param>
    /// <param name="defaultDatabaseRoot">
    ///     The root admission defaults a real run's database under, or null or blank when composition could not resolve
    ///     one.
    /// </param>
    /// <param name="sink">The adapter that performs every write this module reports.</param>
    /// <exception cref="ArgumentNullException"><paramref name="executor" /> or <paramref name="sink" /> is null.</exception>
    internal ProcessingRunControl(
        IProcessingRunExecutor executor,
        string? defaultDatabaseRoot,
        IProcessingRunControlSink sink)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _defaultDatabaseRoot = defaultDatabaseRoot;
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <summary>
    ///     Cancels the active Processing Run, if any, without waiting for it to end. Repeated calls do nothing.
    /// </summary>
    /// <exception cref="AggregateException">A callback registered on the active run's token throws.</exception>
    /// <remarks>
    ///     Not waiting is what keeps closing the window from hanging on a run; the run still reports how it ended and
    ///     releases its own cancellation source as it finishes. Disposal is recorded before cancelling, so a throwing
    ///     cancellation callback still leaves the module disposed.
    /// </remarks>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _runCancellationSource?.Cancel();
        }
    }

    /// <summary>
    ///     Decides one press of Process FormIDs from the inputs the caller captured, and runs it when admission admits
    ///     it.
    /// </summary>
    /// <param name="inputs">Everything the caller captured at the press, before anything was published.</param>
    /// <returns>
    ///     A task that completes once the press has been reported: at once for a refusal or a cancellation request, and
    ///     after the run has ended and handed the progress channel back for an admitted press. There is no typed result;
    ///     a press is observed through what the sink received.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs" /> is null.</exception>
    /// <exception cref="ObjectDisposedException">The module was disposed. Nothing is reported.</exception>
    /// <remarks>
    ///     A run failure does not fault the task: it is rendered to the errors. Anything thrown while the press is
    ///     decided — by admission or by a re-entrant sink notification — is a programming error and faults the task
    ///     rather than being shown as a run failure.
    /// </remarks>
    public async Task PressAsync(ProcessingRunInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (DecidePress(inputs) is not { } admitted)
        {
            return;
        }

        await RunAdmittedAsync(admitted.Request, admitted.CancellationSource);
    }

    /// <summary>
    ///     Decides a press inside the lock: cancels the active run, reports a refusal, or starts the admitted run.
    /// </summary>
    /// <param name="inputs">The captured inputs.</param>
    /// <returns>
    ///     The admitted request and the cancellation source created for it, which the caller now owns; or null when the
    ///     press cancelled the active run or was refused, and has already been fully reported.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The module was disposed.</exception>
    /// <remarks>
    ///     The run-active check, admission and the transition into an active run happen together, so a second press
    ///     arriving before the executor has the run cancels the run in flight rather than starting another. Nothing is
    ///     caught here: admission has no exception mode for a directory (ADR-0011).
    /// </remarks>
    private AdmittedPress? DecidePress(ProcessingRunInputs inputs)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_runCancellationSource is { } activeRun)
            {
                // Cancelled inside the lock, so the run this press found active is the run it cancels. A press after
                // cancellation was already requested cancels an already-cancelled source, which changes nothing.
                activeRun.Cancel();
                // The cancellation-requested status is what the run is doing now, not how it ended, so it belongs on the
                // transient channel; the acknowledgement is the cancelled outcome's, in the message lists (#60). It
                // carries no percentage, so the bar stays where the run left it. The message lists are left alone, so
                // the user keeps reading the messages of the run being cancelled.
                var cancelling = ProcessingRunPresentation.Render(new ProcessingRunCancellationRequested());
                ReportRunActivityLocked(cancelling.Status, cancelling.Value);
                return null;
            }

            var decision = ProcessingRunAdmission.Admit(inputs, _defaultDatabaseRoot);
            switch (decision)
            {
                case RefusedRun refused:
                    // A refused press never became a run, so it takes no ownership of the gate and publishes no
                    // starting activity: the rendered reason is the only thing it leaves behind (ADR-0010). It still
                    // starts the message lists over, as every press that does not cancel does.
                    _sink.ClearMessageLists();
                    _sink.AddErrorMessage(ProcessingRunPresentation.Render(refused.Reason));
                    return null;
                case AdmittedRun admitted:
                    return StartAdmittedRunLocked(admitted, inputs.DatabasePath);
                default:
                    throw new UnreachableException($"Unsupported admission decision: {decision}.");
            }
        }
    }

    /// <summary>
    ///     Makes an admitted press the active run and reports its start, with the caller holding <see cref="_lock" />.
    /// </summary>
    /// <param name="admitted">The admitted request and any database path admission defaulted.</param>
    /// <param name="capturedDatabasePath">
    ///     The database path captured with the press, reported with a defaulted path so the sink's adapter can keep an
    ///     edit made since.
    /// </param>
    /// <returns>The admitted request with the cancellation source created for it.</returns>
    private AdmittedPress StartAdmittedRunLocked(AdmittedRun admitted, string? capturedDatabasePath)
    {
        // The run's token and its active activity both exist before the first report reaches the sink: the list clear,
        // the starting activity and the defaulted path all raise notifications that can re-enter a press, and that
        // press must find an active run with a token to cancel rather than a run with nothing to cancel yet (#104).
        var cancellationSource = new CancellationTokenSource();
        _runCancellationSource = cancellationSource;
        // This is the transition into an active run, so it sets IsActive itself rather than going through
        // ReportRunActivityLocked, which never changes whether a run is active. The starting status renders 0 percent;
        // the carry-forward only applies if a future render omits one.
        var rendered = ProcessingRunPresentation.Render(new ProcessingRunStarting());
        var starting = new ActivityProjection(true, rendered.Status, rendered.Value ?? _runActivity.Value);
        _runActivity = starting;

        // Every press that does not cancel starts the message lists over first, so what the user reads afterwards is
        // only what this press produced.
        _sink.ClearMessageLists();
        // A press re-entering from the list clear has already reported its cancellation-requested activity, which is
        // what the run is doing now; publishing the starting activity after it would put the older status back.
        if (_runActivity == starting)
        {
            _sink.ReportRunActivity(starting);
        }

        // The admitted run uses its default whatever happens next, and the defaulted path is reported even when a
        // re-entrant press already cancelled the run, because admission chose it for this press (ADR-0012).
        if (admitted.DefaultedDatabasePath is { } defaultedDatabasePath)
        {
            _sink.ReportDefaultedDatabasePath(defaultedDatabasePath, capturedDatabasePath);
        }

        return new AdmittedPress(admitted.Request, cancellationSource);
    }

    /// <summary>
    ///     Executes a run this module has already made active, reports how it ended, and hands the progress channel
    ///     back.
    /// </summary>
    /// <param name="request">The admitted request.</param>
    /// <param name="cancellationSource">
    ///     The run's cancellation source, created by <see cref="StartAdmittedRunLocked" />. This method's
    ///     <c>finally</c> clears and releases it, which is only correct for the press that created it.
    /// </param>
    /// <returns>A task that completes after the run finishes, fails, or observes cancellation.</returns>
    private async Task RunAdmittedAsync(ProcessingRunRequest request, CancellationTokenSource cancellationSource)
    {
        try
        {
            // The token may already be cancelled by a press that re-entered during startup; the executor still enters
            // its worker and returns the cancelled outcome, so that press ends with the normal acknowledgement.
            var outcome = await _executor.ExecuteAsync(
                request,
                new RunProgressReporter(this, cancellationSource),
                cancellationSource.Token);
            ReportOutcome(outcome);
        }
        // Every run failure takes this one route. Which failure is shown unwrapped — an Unresolvable Master, whose own
        // message already says what to do — is the failure renderer's decision, not a catch clause's (ADR-0006).
        catch (Exception ex)
        {
            lock (_lock)
            {
                _sink.AddErrorMessage(ProcessingRunPresentation.RenderFailure(ex));
            }
        }
        finally
        {
            lock (_lock)
            {
                // Returning to no activity hands the progress channel back rather than blanking it: a Plugin List
                // refresh still scanning underneath this run keeps showing its own progress, by the ViewModel's
                // precedence rule.
                _runActivity = ActivityProjection.None;
                _sink.ReportRunActivity(_runActivity);
                // Cleared together with the activity, so no press can find the run inactive yet still holding a
                // source, nor cancel the source after the release below.
                _runCancellationSource = null;
            }

            // Released only after the executor has returned, so nothing still reads the token, and outside the lock
            // because every cancel happens under it and now finds no source.
            cancellationSource.Dispose();
        }
    }

    /// <summary>
    ///     Renders how the run ended and reports everything that outcome implies.
    /// </summary>
    /// <param name="outcome">The outcome the executor returned.</param>
    /// <remarks>
    ///     <see cref="ProcessingRunPresentation" /> is a pure renderer, so the order the run's terminal facts reach the
    ///     user is decided here: the messages first and the transient status last. An inactive rendered activity means
    ///     the outcome has nothing to say on that channel — a cancelled run's acknowledgement is a terminal fact, and
    ///     reporting it there would only have it erased as the run hands the channel back (#60).
    /// </remarks>
    private void ReportOutcome(ProcessingRunOutcome outcome)
    {
        var rendered = ProcessingRunPresentation.Render(outcome);

        lock (_lock)
        {
            foreach (var message in rendered.WarningMessages)
            {
                _sink.AddWarningMessage(message);
            }

            foreach (var message in rendered.ErrorMessages)
            {
                _sink.AddErrorMessage(message);
            }

            foreach (var message in rendered.InformationMessages)
            {
                _sink.AddInformationMessage(message);
            }

            if (rendered.Activity.IsActive)
            {
                ReportRunActivityLocked(rendered.Activity.Status, rendered.Activity.Value);
            }
        }
    }

    /// <summary>
    ///     Renders one transient report from a run's progress channel and reports it, unless that run has ended.
    /// </summary>
    /// <param name="runProgress">What the run said it is doing.</param>
    /// <param name="reportingRun">The cancellation source of the run that reported, which identifies it.</param>
    /// <remarks>
    ///     A report from a run that is no longer the active one is dropped, so a finished run never puts a stale status
    ///     back on the channel.
    /// </remarks>
    private void ReportProgress(ProcessingRunProgress runProgress, CancellationTokenSource reportingRun)
    {
        var rendered = ProcessingRunPresentation.Render(runProgress);

        lock (_lock)
        {
            if (!ReferenceEquals(_runCancellationSource, reportingRun))
            {
                return;
            }

            ReportRunActivityLocked(rendered.Status, rendered.Value);
        }
    }

    /// <summary>
    ///     Reports new activity for the active run, carrying the last percentage forward when the render omits one,
    ///     with the caller holding <see cref="_lock" />.
    /// </summary>
    /// <param name="status">The already-rendered status text.</param>
    /// <param name="value">The rendered percentage, or null to keep the percentage already shown.</param>
    /// <remarks>
    ///     Whether a run is active is never changed here, and nothing is reported while no run is active.
    /// </remarks>
    private void ReportRunActivityLocked(string status, double? value)
    {
        if (!_runActivity.IsActive)
        {
            return;
        }

        _runActivity = _runActivity with { Status = status, Value = value ?? _runActivity.Value };
        _sink.ReportRunActivity(_runActivity);
    }

    /// <summary>
    ///     An admitted press: the request to run and the cancellation source created for it inside the lock.
    /// </summary>
    /// <param name="Request">The request admission built.</param>
    /// <param name="CancellationSource">The run's cancellation source, released by the press that runs it.</param>
    private sealed record AdmittedPress(ProcessingRunRequest Request, CancellationTokenSource CancellationSource);

    /// <summary>
    ///     The progress reporter one run is handed, which tags each report with the run it came from.
    /// </summary>
    /// <param name="owner">The module the run belongs to.</param>
    /// <param name="run">The run's cancellation source, used as its identity.</param>
    private sealed class RunProgressReporter(ProcessingRunControl owner, CancellationTokenSource run)
        : IProgress<ProcessingRunProgress>
    {
        /// <inheritdoc />
        public void Report(ProcessingRunProgress value)
        {
            owner.ReportProgress(value, run);
        }
    }
}

/// <summary>
///     Where Processing Run Control reports everything a Processing Run shows the user, already rendered and in the
///     order it sequenced them.
/// </summary>
/// <remarks>
///     Processing Run Control calls every member while holding its lock, so an implementation sees reports from the UI
///     thread and the executor's thread in sequence (#60). A member may raise notifications that re-enter a press. The
///     User Workflow implements it over the ViewModel, and tests implement it by recording (ADR-0012).
/// </remarks>
internal interface IProcessingRunControlSink
{
    /// <summary>
    ///     Clears the error and warning message lists at the start of a press that does not cancel.
    /// </summary>
    void ClearMessageLists();

    /// <summary>
    ///     Reports the database path admission chose for a press that had none.
    /// </summary>
    /// <param name="defaultedDatabasePath">The path the admitted run uses.</param>
    /// <param name="capturedDatabasePath">
    ///     The database path captured with the press, so an implementation can keep an edit made since rather than
    ///     overwrite it.
    /// </param>
    void ReportDefaultedDatabasePath(string defaultedDatabasePath, string? capturedDatabasePath);

    /// <summary>
    ///     Applies the run's rendered Workflow Activity; <see cref="ActivityProjection.None" /> hands the progress
    ///     channel back.
    /// </summary>
    /// <param name="runActivity">The rendered run activity.</param>
    void ReportRunActivity(ActivityProjection runActivity);

    /// <summary>
    ///     Adds one rendered warning message.
    /// </summary>
    /// <param name="message">The rendered message.</param>
    void AddWarningMessage(string message);

    /// <summary>
    ///     Adds one rendered error message.
    /// </summary>
    /// <param name="message">The rendered message.</param>
    void AddErrorMessage(string message);

    /// <summary>
    ///     Adds one rendered information message.
    /// </summary>
    /// <param name="message">The rendered message.</param>
    void AddInformationMessage(string message);
}
