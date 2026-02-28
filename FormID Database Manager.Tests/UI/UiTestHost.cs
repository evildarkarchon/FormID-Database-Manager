using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace FormID_Database_Manager.Tests.UI;

internal static class UiTestHost
{
    public static async Task WithWindowAsync<TWindow>(Func<TWindow> createWindow, Func<TWindow, Task> testAction)
        where TWindow : MainWindow
    {
        var window = await ShowWindowAsync(createWindow);
        try
        {
            await testAction(window);
        }
        finally
        {
            window.Close();
            await FlushUiAsync();
        }
    }

    public static async Task WithWindowAsync<TWindow>(Func<TWindow> createWindow, Action<TWindow> testAction)
        where TWindow : MainWindow
    {
        var window = await ShowWindowAsync(createWindow);
        try
        {
            testAction(window);
        }
        finally
        {
            window.Close();
            await FlushUiAsync();
        }
    }

    public static async Task<TWindow> ShowWindowAsync<TWindow>(Func<TWindow> createWindow)
        where TWindow : MainWindow
    {
        var window = createWindow();
        window.Show();
        await FlushUiAsync();
        return window;
    }

    public static async Task FlushUiAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}
