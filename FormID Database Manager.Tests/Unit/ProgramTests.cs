using System.Reflection;
using Avalonia;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit;

public class ProgramTests
{
    [Fact]
    public void BuildAvaloniaApp_ReturnsConfiguredBuilder()
    {
        var method = typeof(Program).GetMethod("BuildAvaloniaApp",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var builder = method.Invoke(null, null);

        Assert.NotNull(builder);
        Assert.IsType<AppBuilder>(builder);
    }
}
