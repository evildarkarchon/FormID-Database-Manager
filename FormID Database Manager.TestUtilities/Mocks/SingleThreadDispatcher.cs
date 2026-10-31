using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.TestUtilities.Mocks;

/// <summary>
///     An <see cref="IThreadDispatcher" /> that owns one dedicated thread and runs every posted action on it, in the
///     order posted, the way the WinUI dispatcher does.
/// </summary>
/// <remarks>
///     <para>
///         Use this rather than <see cref="SynchronousThreadDispatcher" /> when a test calls the ViewModel from many
///         threads at once. <see cref="SynchronousThreadDispatcher" /> always reports access, so nothing is posted and
///         concurrent callers would mutate dispatcher-owned collections at the same time, which production never does.
///         Here only the owning thread has access, so concurrent callers are serialized onto it.
///     </para>
///     <para>
///         An action that throws does not stop the thread: the exception is recorded in <see cref="Faults" />, since a
///         posted action has no caller left to report to.
///     </para>
/// </remarks>
public sealed class SingleThreadDispatcher : IThreadDispatcher, IDisposable
{
    private readonly ConcurrentQueue<Exception> _faults = new();
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    /// <summary>
    ///     Starts the dispatcher's owning thread.
    /// </summary>
    public SingleThreadDispatcher()
    {
        _thread = new Thread(RunQueue) { IsBackground = true, Name = nameof(SingleThreadDispatcher) };
        _thread.Start();
    }

    /// <summary>
    ///     Gets the exceptions thrown by posted actions, in the order they were thrown.
    /// </summary>
    public IReadOnlyCollection<Exception> Faults => _faults;

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _queue.Add(action);
    }

    /// <inheritdoc />
    /// <returns><see langword="true" /> only on the dispatcher's own thread.</returns>
    public bool CheckAccess()
    {
        return Environment.CurrentManagedThreadId == _thread.ManagedThreadId;
    }

    /// <summary>
    ///     Blocks until every action posted before this call has run.
    /// </summary>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <exception cref="TimeoutException">The queue did not drain within <paramref name="timeout" />.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Called from the dispatcher's own thread, which would deadlock.
    /// </exception>
    public void WaitForIdle(TimeSpan timeout)
    {
        if (CheckAccess())
        {
            throw new InvalidOperationException("Cannot wait for the dispatcher from its own thread.");
        }

        // The queue is first-in first-out, so once this marker runs every action posted before it has run too.
        using var drained = new ManualResetEventSlim();
        Post(drained.Set);
        if (!drained.Wait(timeout))
        {
            throw new TimeoutException($"The dispatcher did not drain within {timeout}.");
        }
    }

    /// <summary>
    ///     Stops accepting work, lets the owning thread finish what is queued, and waits for it to exit.
    /// </summary>
    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join();
        _queue.Dispose();
    }

    private void RunQueue()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _faults.Enqueue(ex);
            }
        }
    }
}
