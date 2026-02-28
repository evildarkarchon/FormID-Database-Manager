using Avalonia.Headless.XUnit;
using Xunit;

namespace FormID_Database_Manager.Tests.UI;

[Collection("UI Tests")]
public class AppStartupTests
{
    [AvaloniaFact]
    public void App_Initialize_LoadsApplicationResources()
    {
        var app = new App();

        app.Initialize();

        Assert.NotEmpty(app.Styles);
    }
}
