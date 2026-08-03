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

/// <remarks>
/// Currently unreferenced. This was the ViewModel's silent fallback for a caller that supplied no dispatcher; that
/// fallback is gone because it let a caller opt out of the UI-thread marshalling invariant every projection depends on.
/// The class is kept as the no-marshalling implementation for a future caller that genuinely owns no UI thread.
/// </remarks>
internal sealed class ImmediateThreadDispatcher : IThreadDispatcher
{
    /// <summary>
    /// Runs the action synchronously, because this dispatcher owns no separate thread to queue it on.
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
