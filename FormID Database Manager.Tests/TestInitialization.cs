using Avalonia;
using Avalonia.Headless;
using Avalonia.ReactiveUI;
using System.Runtime.CompilerServices;

[assembly: AvaloniaTestApplication(typeof(FormID_Database_Manager.Tests.TestApp))]

namespace FormID_Database_Manager.Tests
{
    public class TestApp : Application
    {
        [ModuleInitializer]
        public static void InitializeTests()
        {
            // Build and initialize the Avalonia app for headless testing
            BuildAvaloniaApp()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<TestApp>()
                .UsePlatformDetect()
                .UseReactiveUI()
                .LogToTrace();
        }

        public override void Initialize()
        {
            // Register any application resources needed for tests
            Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
            
            // Add the BooleanConverter resource that MainWindow expects
            Resources.Add("BooleanConverter", new FormID_Database_Manager.BooleanConverter());
        }
    }
}