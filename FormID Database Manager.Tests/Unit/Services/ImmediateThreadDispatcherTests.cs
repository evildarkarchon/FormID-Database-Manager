using System;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class ImmediateThreadDispatcherTests
{
    /// <summary>
    /// Verifies that immediate dispatch preserves async exception observation semantics.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenActionThrows_ReturnsFaultedTask()
    {
        var dispatcher = new ImmediateThreadDispatcher();
        var expected = new InvalidOperationException("callback failed");
        var task = Task.CompletedTask;

        var synchronousException = Record.Exception((Action)(() =>
        {
            task = dispatcher.InvokeAsync(() => throw expected);
        }));

        Assert.Null(synchronousException);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Same(expected, actual);
    }
}
