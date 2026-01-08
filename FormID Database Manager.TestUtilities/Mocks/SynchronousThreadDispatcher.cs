using System;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.TestUtilities.Mocks;

/// <summary>
/// A synchronous implementation of <see cref="IThreadDispatcher"/> for testing purposes.
/// Executes all actions immediately on the calling thread, avoiding deadlocks that can occur
/// when the Avalonia UI dispatcher isn't being pumped (e.g., in non-[AvaloniaFact] tests).
/// </summary>
public class SynchronousThreadDispatcher : IThreadDispatcher
{
    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Post(Action action)
    {
        action();
    }

    /// <inheritdoc />
    public bool CheckAccess()
    {
        // Always return true since we execute synchronously on the calling thread
        return true;
    }
}
