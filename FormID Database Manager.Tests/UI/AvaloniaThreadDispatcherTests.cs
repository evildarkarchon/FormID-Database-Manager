using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FormID_Database_Manager.Services;
using Xunit;

namespace FormID_Database_Manager.Tests.UI;

[Collection("UI Tests")]
public class AvaloniaThreadDispatcherTests
{
    [AvaloniaFact]
    public async Task InvokeAsync_RunsActionOnUiThread()
    {
        var dispatcher = new AvaloniaThreadDispatcher();
        var invoked = false;

        await dispatcher.InvokeAsync(() => invoked = true);

        Assert.True(invoked);
    }

    [AvaloniaFact]
    public async Task Post_QueuesAction()
    {
        var dispatcher = new AvaloniaThreadDispatcher();
        var invoked = false;

        dispatcher.Post(() => invoked = true);
        await UiTestHost.FlushUiAsync();

        Assert.True(invoked);
    }

    [AvaloniaFact]
    public async Task CheckAccess_ReturnsTrueOnUiThread()
    {
        var dispatcher = new AvaloniaThreadDispatcher();
        var access = false;

        await dispatcher.InvokeAsync(() => access = dispatcher.CheckAccess());

        Assert.True(access);
    }
}
