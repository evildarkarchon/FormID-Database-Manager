namespace FormID_Database_Manager.Services;

/// <summary>
///     Adapts a UI queue with access checks and fire-and-forget enqueueing to the shared dispatcher contract.
/// </summary>
internal sealed class QueuedThreadDispatcher : IThreadDispatcher
{
    private readonly Func<bool> _checkAccess;
    private readonly string _rejectionMessage;
    private readonly Func<Action, bool> _tryEnqueue;

    /// <summary>
    ///     Creates a dispatcher around queue primitives supplied by a UI platform adapter.
    /// </summary>
    /// <param name="checkAccess">Returns whether the caller owns the target UI queue.</param>
    /// <param name="tryEnqueue">Attempts to queue work on the target UI queue and reports rejection.</param>
    /// <param name="rejectionMessage">The exception message used when the queue rejects work.</param>
    public QueuedThreadDispatcher(
        Func<bool> checkAccess,
        Func<Action, bool> tryEnqueue,
        string rejectionMessage)
    {
        _checkAccess = checkAccess ?? throw new ArgumentNullException(nameof(checkAccess));
        _tryEnqueue = tryEnqueue ?? throw new ArgumentNullException(nameof(tryEnqueue));
        _rejectionMessage = string.IsNullOrWhiteSpace(rejectionMessage)
            ? "The dispatcher rejected queued work."
            : rejectionMessage;
    }

    /// <summary>
    ///     Queues fire-and-forget work on the target UI queue.
    /// </summary>
    /// <param name="action">The work to queue.</param>
    /// <exception cref="InvalidOperationException">Thrown when the queue rejects the work.</exception>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_tryEnqueue(action))
        {
            // Fire-and-forget work cannot report through a Task, so fail immediately on queue rejection.
            throw CreateRejectedException();
        }
    }

    /// <summary>
    ///     Returns whether the caller owns the target UI queue.
    /// </summary>
    /// <returns><see langword="true" /> when work can run directly on the current thread.</returns>
    public bool CheckAccess()
    {
        return _checkAccess();
    }

    private InvalidOperationException CreateRejectedException()
    {
        return new InvalidOperationException(_rejectionMessage);
    }
}
