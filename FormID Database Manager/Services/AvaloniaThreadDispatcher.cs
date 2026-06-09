using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace FormID_Database_Manager.Services;

public class AvaloniaThreadDispatcher : IThreadDispatcher
{
    public async Task InvokeAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    public bool CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }
}
