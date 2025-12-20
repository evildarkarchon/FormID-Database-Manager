using System;
using System.IO;
using System.Reflection;
using Avalonia;

namespace FormID_Database_Manager;

internal class Program
{
    /// The entry point for the application. Prepares the application context, sets up assembly resolution,
    /// and starts the Avalonia application with a classic desktop lifetime.
    /// <param name="args">The command-line arguments passed to the application.</param>
    [STAThread]
    public static void Main(string[] args)
    {
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", AppDomain.CurrentDomain.BaseDirectory);
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    /// Configures and builds the Avalonia application instance.
    /// Returns:
    /// An instance of AppBuilder configured with application-specific settings.
    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
