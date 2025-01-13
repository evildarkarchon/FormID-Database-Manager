using Avalonia;
using System;
using System.IO;
using System.Reflection;

namespace FormID_Database_Manager;

class Program
{
    /// The entry point for the application. Prepares the application context, sets up assembly resolution,
    /// and starts the Avalonia application with a classic desktop lifetime.
    /// <param name="args">The command-line arguments passed to the application.</param>
    [STAThread]
    public static void Main(string[] args)
    {
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", AppDomain.CurrentDomain.BaseDirectory);
        AppDomain.CurrentDomain.AssemblyResolve += (_, resolveEventArgs) =>
        {
            var assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs",
                new AssemblyName(resolveEventArgs.Name).Name + ".dll");
            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }

            return null;
        };
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    /// Configures and builds the Avalonia application instance.
    /// Returns:
    /// An instance of AppBuilder configured with application-specific settings.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}