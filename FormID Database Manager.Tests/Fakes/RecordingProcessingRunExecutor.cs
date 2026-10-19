#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     Records every run it is handed, together with the cancellation token and progress reporter that came with it,
///     and ends each run the way the real executor would: cancelled when that token was cancelled by the time the run
///     ends, and otherwise with the configured failure or outcome.
/// </summary>
/// <remarks>
///     Issue #104, under parent #102. Tests observe cancellation through the token the run received rather than by
///     counting calls, so a test passes only if the cancel really reached the run. A run can be held at
///     <see cref="Gate" /> so a test can act — press again, dispose — while it is still in flight. Issue #105 moved it
///     out of the User Workflow tests so the Processing Run Control tests drive the same fake.
/// </remarks>
internal sealed class RecordingProcessingRunExecutor : IProcessingRunExecutor
{
    /// <summary>
    ///     Creates an executor that records the runs it is handed into its own <see cref="Requests" /> list.
    /// </summary>
    public RecordingProcessingRunExecutor()
        : this([])
    {
    }

    /// <summary>
    ///     Creates an executor that records the runs it is handed into a list the test already holds.
    /// </summary>
    /// <param name="requests">The list that receives each run's request, in run order.</param>
    public RecordingProcessingRunExecutor(List<ProcessingRunRequest> requests)
    {
        Requests = requests ?? throw new ArgumentNullException(nameof(requests));
    }

    /// <summary>
    ///     The request each run was handed, in run order.
    /// </summary>
    public List<ProcessingRunRequest> Requests { get; }

    /// <summary>
    ///     The cancellation token each run was handed, in run order.
    /// </summary>
    public List<CancellationToken> ReceivedTokens { get; } = [];

    /// <summary>
    ///     The progress reporter each run was handed, in run order, so a test can report through it after the run ended.
    /// </summary>
    public List<IProgress<ProcessingRunProgress>?> ReceivedProgress { get; } = [];

    /// <summary>
    ///     Whether each run's token was already cancelled at the moment the run reached this executor, in run order.
    /// </summary>
    public List<bool> CancelledOnArrival { get; } = [];

    /// <summary>
    ///     When set, each run reports its progress and then waits for this task before ending, so it stays in
    ///     flight until the test completes it.
    /// </summary>
    public Task? Gate { get; set; }

    /// <summary>
    ///     Work run inside each run after its progress is reported and before it waits at <see cref="Gate" />, with
    ///     the run's token — for example to register a cancellation callback on it.
    /// </summary>
    public Action<CancellationToken>? OnExecute { get; set; }

    /// <summary>
    ///     The failure raised by <see cref="ExecuteAsync" /> after any configured events are reported, standing in
    ///     for a run that ends in a terminal failure rather than an outcome.
    /// </summary>
    public Exception? ExecuteFailure { get; set; }

    /// <summary>
    ///     How the run ends when it does not fail.
    /// </summary>
    /// <remarks>
    ///     The default is an empty text import, which adds no messages. Plans now require a non-empty
    ///     authoritative selection, so an empty plan cannot stand in for an uneventful outcome.
    /// </remarks>
    public ProcessingRunOutcome Outcome { get; set; } =
        new FormIdTextRunOutcome(new FormIdTextFileImportResult(0, 0));

    /// <summary>
    ///     The progress each run reports, synchronously and in order, as soon as it is handed the run.
    /// </summary>
    public List<ProcessingRunProgress> ProgressToReport { get; } = [];

    /// <inheritdoc />
    public Task<ProcessingRunOutcome> ExecuteAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        ReceivedTokens.Add(cancellationToken);
        ReceivedProgress.Add(progress);
        CancelledOnArrival.Add(cancellationToken.IsCancellationRequested);
        foreach (var runEvent in ProgressToReport)
        {
            progress?.Report(runEvent);
        }

        OnExecute?.Invoke(cancellationToken);

        if (Gate is { } gate)
        {
            return EndAfterGateAsync(gate, cancellationToken);
        }

        // A failure is returned as a faulted task rather than thrown, as an async executor would surface it.
        try
        {
            return Task.FromResult(End(cancellationToken));
        }
        catch (Exception ex)
        {
            return Task.FromException<ProcessingRunOutcome>(ex);
        }
    }

    /// <summary>
    ///     Ends a held run once its gate opens.
    /// </summary>
    /// <param name="gate">The task the run waits for.</param>
    /// <param name="cancellationToken">The run's token, read only once the gate has opened.</param>
    /// <returns>How the run ended.</returns>
    private async Task<ProcessingRunOutcome> EndAfterGateAsync(Task gate, CancellationToken cancellationToken)
    {
        // The gate ignores the token on purpose: the test, not the cancel, decides when the run ends, so a cancel
        // that never reached the token shows up as a completed outcome rather than as a hang.
        await gate.ConfigureAwait(false);
        return End(cancellationToken);
    }

    /// <summary>
    ///     Decides how a run ends, cancellation first, exactly as the real executor attributes it (ADR-0007).
    /// </summary>
    /// <param name="cancellationToken">The token the run was handed.</param>
    /// <returns>The cancelled outcome, or the configured outcome.</returns>
    /// <exception cref="Exception">The configured <see cref="ExecuteFailure" />, when the run was not cancelled.</exception>
    private ProcessingRunOutcome End(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new CancelledRunOutcome();
        }

        if (ExecuteFailure is { } failure)
        {
            throw failure;
        }

        return Outcome;
    }
}
