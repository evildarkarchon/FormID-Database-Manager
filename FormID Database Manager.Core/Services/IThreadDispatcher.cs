using System;
using System.Threading.Tasks;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Marshals work to the thread or scheduler that owns UI-bound state.
/// </summary>
public interface IThreadDispatcher
{
    /// <summary>
    /// Runs the action on the dispatcher and completes when the action has finished.
    /// </summary>
    /// <param name="action">The work to run on the dispatcher.</param>
    /// <returns>A task that completes after the action has run.</returns>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Queues the action on the dispatcher without waiting for it to run.
    /// </summary>
    /// <param name="action">The work to queue on the dispatcher.</param>
    void Post(Action action);

    /// <summary>
    /// Returns whether the caller is already on the dispatcher's owning thread.
    /// </summary>
    /// <returns><see langword="true"/> when the caller can update dispatcher-owned state directly.</returns>
    bool CheckAccess();
}

internal sealed class ImmediateThreadDispatcher : IThreadDispatcher
{
    /// <summary>
    /// Runs the action synchronously for non-UI callers that do not supply a platform dispatcher.
    /// </summary>
    /// <param name="action">The work to run immediately.</param>
    /// <returns>A completed task after the action has run.</returns>
    public Task InvokeAsync(Action action)
    {
        try
        {
            action();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    /// <summary>
    /// Runs the action synchronously for non-UI callers that do not supply a platform dispatcher.
    /// </summary>
    /// <param name="action">The work to run immediately.</param>
    public void Post(Action action)
    {
        action();
    }

    /// <summary>
    /// Reports direct access because this dispatcher does not own a separate UI thread.
    /// </summary>
    /// <returns>Always returns <see langword="true"/>.</returns>
    public bool CheckAccess()
    {
        return true;
    }
}
