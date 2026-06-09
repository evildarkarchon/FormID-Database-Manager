using System;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Microsoft.UI.Dispatching;

namespace FormID_Database_Manager.WinUI.Services;

/// <summary>
/// Marshals core ViewModel updates through a WinUI window's <see cref="DispatcherQueue"/>.
/// </summary>
public sealed class WinUiThreadDispatcher : IThreadDispatcher
{
    private readonly QueuedThreadDispatcher _dispatcher;
    private readonly DispatcherQueue _dispatcherQueue;

    /// <summary>
    /// Creates a dispatcher that targets the queue owned by the WinUI main window.
    /// </summary>
    /// <param name="dispatcherQueue">The dispatcher queue captured from the owning WinUI window.</param>
    public WinUiThreadDispatcher(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _dispatcher = new QueuedThreadDispatcher(
            () => _dispatcherQueue.HasThreadAccess,
            action => _dispatcherQueue.TryEnqueue(() => action()),
            "The WinUI dispatcher rejected queued work. The window may be closing.");
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        return _dispatcher.InvokeAsync(action);
    }

    /// <inheritdoc />
    public void Post(Action action)
    {
        _dispatcher.Post(action);
    }

    /// <inheritdoc />
    public bool CheckAccess()
    {
        return _dispatcher.CheckAccess();
    }
}
