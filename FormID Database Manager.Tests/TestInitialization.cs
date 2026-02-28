using Avalonia;
using Avalonia.Headless;
using Avalonia.ReactiveUI;
using Avalonia.Themes.Fluent;
using FormID_Database_Manager.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace FormID_Database_Manager.Tests;

public class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .UseReactiveUI()
            .LogToTrace();
    }

    public override void Initialize()
    {
        // Register any application resources needed for tests
        Styles.Add(new FluentTheme());

        // Add the BooleanConverter resource that MainWindow expects
        Resources.Add("BooleanConverter", new BooleanConverter());
    }
}
