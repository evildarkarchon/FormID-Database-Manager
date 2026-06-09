using System;
using System.IO;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Architecture;

public class WinUiPlatformServiceSourceTests
{
    /// <summary>
    /// Verifies that Phase 4 adds native WinUI dispatcher and picker service classes.
    /// </summary>
    [Fact]
    public void WinUiProject_DefinesPlatformServiceClasses()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var dispatcherPath = Path.Combine(winUiDirectory, "Services", "WinUiThreadDispatcher.cs");
        var pickerPath = Path.Combine(winUiDirectory, "Services", "WinUiFileDialogService.cs");

        Assert.True(File.Exists(dispatcherPath), $"WinUI dispatcher service was not found at {dispatcherPath}.");
        Assert.True(File.Exists(pickerPath), $"WinUI picker service was not found at {pickerPath}.");

        var dispatcherSource = File.ReadAllText(dispatcherPath);
        Assert.Contains("public sealed class WinUiThreadDispatcher", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("IThreadDispatcher", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("HasThreadAccess", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("TryEnqueue", dispatcherSource, StringComparison.Ordinal);

        var pickerSource = File.ReadAllText(pickerPath);
        Assert.Contains("public sealed class WinUiFileDialogService", pickerSource, StringComparison.Ordinal);
        Assert.Contains("IFileDialogService", pickerSource, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Windows.Storage.Pickers", pickerSource, StringComparison.Ordinal);
        Assert.Contains("FolderPicker", pickerSource, StringComparison.Ordinal);
        Assert.Contains("FileSavePicker", pickerSource, StringComparison.Ordinal);
        Assert.Contains("FileOpenPicker", pickerSource, StringComparison.Ordinal);
        Assert.Contains("AppWindow.Id", pickerSource, StringComparison.Ordinal);
        Assert.Contains("\".db\"", pickerSource, StringComparison.Ordinal);
        Assert.Contains("\".txt\"", pickerSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the WinUI main window no longer depends on Phase 3 platform-service placeholders.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_WiresPlatformServiceWorkflow()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var mainWindowSourcePath = Path.Combine(winUiDirectory, "MainWindow.xaml.cs");
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var source = File.ReadAllText(mainWindowSourcePath);
        Assert.DoesNotContain("PickerPendingMessage", source, StringComparison.Ordinal);
        Assert.Contains("WinUiThreadDispatcher", source, StringComparison.Ordinal);
        Assert.Contains("WinUiFileDialogService", source, StringComparison.Ordinal);
        Assert.Contains("PluginListManager", source, StringComparison.Ordinal);
        Assert.Contains("PluginProcessingService", source, StringComparison.Ordinal);
        Assert.Contains("CancelProcessing", source, StringComparison.Ordinal);
        Assert.Contains("DirectoryComboBox_SelectionChanged", source, StringComparison.Ordinal);

        var xaml = File.ReadAllText(mainWindowXamlPath);
        Assert.Contains("x:Name=\"DirectoryComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"DirectoryComboBox_SelectionChanged\"", xaml, StringComparison.Ordinal);
    }

    private static string GetWinUiProjectDirectory()
    {
        return Path.Combine(FindRepositoryRoot(), "FormID Database Manager.WinUI");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FormID Database Manager.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from the test output directory.");
    }
}
