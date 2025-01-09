using Avalonia;
using System;
using System.IO;
using System.Reflection;

namespace FormID_Database_Manager;

class Program
{
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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}