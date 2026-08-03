namespace FormID_Database_Manager.Services;

/// <summary>
/// Marshals work to the thread or scheduler that owns UI-bound state.
/// </summary>
public interface IThreadDispatcher
{
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
