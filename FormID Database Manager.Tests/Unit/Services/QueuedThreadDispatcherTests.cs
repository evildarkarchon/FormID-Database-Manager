using System;
using FormID_Database_Manager.Services;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class QueuedThreadDispatcherTests
{
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
