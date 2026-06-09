using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class QueuedThreadDispatcherTests
{
    /// <summary>
    /// Verifies that dispatcher-owned callers execute awaited work without queueing.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenCallerHasAccess_RunsActionImmediately()
    {
        var enqueueCalls = 0;
        var dispatcher = new QueuedThreadDispatcher(
            () => true,
            _ =>
            {
                enqueueCalls++;
                return true;
            },
            "Dispatcher rejected queued work.");

        var invoked = false;

        await dispatcher.InvokeAsync(() => invoked = true);

        Assert.True(invoked);
        Assert.Equal(0, enqueueCalls);
    }

    /// <summary>
    /// Verifies that background callers receive a task that completes after queued work runs.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenCallerNeedsDispatch_CompletesAfterQueuedActionRuns()
    {
        var queuedActions = new Queue<Action>();
        var dispatcher = new QueuedThreadDispatcher(
            () => false,
            action =>
            {
                queuedActions.Enqueue(action);
                return true;
            },
            "Dispatcher rejected queued work.");

        var invoked = false;
        var task = dispatcher.InvokeAsync(() => invoked = true);

        Assert.False(task.IsCompleted);
        Assert.Single(queuedActions);

        queuedActions.Dequeue().Invoke();
        await task;

        Assert.True(invoked);
    }

    /// <summary>
    /// Verifies that exceptions thrown by queued callbacks propagate to awaiting callers.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenQueuedActionThrows_CompletesWithException()
    {
        var queuedActions = new Queue<Action>();
        var expected = new InvalidOperationException("callback failed");
        var dispatcher = new QueuedThreadDispatcher(
            () => false,
            action =>
            {
                queuedActions.Enqueue(action);
                return true;
            },
            "Dispatcher rejected queued work.");

        var task = dispatcher.InvokeAsync(() => throw expected);
        queuedActions.Dequeue().Invoke();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Same(expected, actual);
    }

    /// <summary>
    /// Verifies that rejected queued work fails deterministically instead of hanging.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenQueueRejectsWork_CompletesWithDispatcherException()
    {
        var dispatcher = new QueuedThreadDispatcher(
            () => false,
            _ => false,
            "Dispatcher rejected queued work.");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.InvokeAsync(() => { }));

        Assert.Contains("Dispatcher rejected queued work", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that fire-and-forget posts are not silently dropped when the queue rejects them.
    /// </summary>
    [Fact]
    public void Post_WhenQueueRejectsWork_ThrowsDispatcherException()
    {
        var dispatcher = new QueuedThreadDispatcher(
            () => false,
            _ => false,
            "Dispatcher rejected queued work.");

        void Act()
        {
            dispatcher.Post(() => { });
        }

        var exception = Assert.Throws<InvalidOperationException>((Action)Act);

        Assert.Contains("Dispatcher rejected queued work", exception.Message, StringComparison.Ordinal);
    }
}
