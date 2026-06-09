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
        Assert.Contains("new QueuedThreadDispatcher", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("\"The WinUI dispatcher rejected queued work. The window may be closing.\"", dispatcherSource, StringComparison.Ordinal);

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

    /// <summary>
    /// Verifies that WinUI picker button workflow is bound to the UI-neutral picker abstraction.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_ConsumesFileDialogServiceForPickerWorkflow()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var mainWindowSourcePath = Path.Combine(winUiDirectory, "MainWindow.xaml.cs");
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var source = File.ReadAllText(mainWindowSourcePath);
        var xaml = File.ReadAllText(mainWindowXamlPath);

        Assert.Contains("private readonly IFileDialogService _fileDialogService;", source, StringComparison.Ordinal);
        Assert.Contains("IFileDialogService? fileDialogService", source, StringComparison.Ordinal);
        Assert.Contains("_fileDialogService = fileDialogService ?? new WinUiFileDialogService(AppWindow, ViewModel);", source, StringComparison.Ordinal);

        Assert.Contains("await _fileDialogService.SelectGameDirectory()", source, StringComparison.Ordinal);
        Assert.Contains("await _fileDialogService.SelectDatabaseFile()", source, StringComparison.Ordinal);
        Assert.Contains("await _fileDialogService.SelectFormIdListFile()", source, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrEmpty(path))", source, StringComparison.Ordinal);
        Assert.Contains("SetGameDirectory(path);", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.DatabasePath = path;", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.FormIdListPath = path;", source, StringComparison.Ordinal);

        Assert.Contains("Click=\"BrowseDirectory_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnSelectDatabase_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnSelectFormIdList_Click\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that WinUI preserves the migration-critical controls and handlers formerly covered by Avalonia UI tests.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_DefinesMigrationCriticalControlsAndHandlers()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var mainWindowSourcePath = Path.Combine(winUiDirectory, "MainWindow.xaml.cs");
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var source = File.ReadAllText(mainWindowSourcePath);
        var xaml = File.ReadAllText(mainWindowXamlPath);

        Assert.Contains("Title=\"FormID Database Manager\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GameComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BrowseDirectoryButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DatabasePathTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FormIdListPathTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PluginList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AdvancedModeCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UpdateModeCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectAllButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectNoneButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProcessFormIdsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProcessingProgressBar\"", xaml, StringComparison.Ordinal);

        Assert.Contains("Click=\"SelectAll_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"SelectNone_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ProcessFormIds_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Checked=\"AdvancedMode_CheckedChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Unchecked=\"AdvancedMode_CheckedChanged\"", xaml, StringComparison.Ordinal);

        Assert.Contains("_pluginListManager.SelectAll(ViewModel.Plugins);", source, StringComparison.Ordinal);
        Assert.Contains("_pluginListManager.SelectNone(ViewModel.Plugins);", source, StringComparison.Ordinal);
        Assert.Contains("await LoadPluginsForCurrentSelection();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that Phase 5 restores the WinUI processing workflow instead of the deferred placeholder.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_WiresProcessingWorkflow()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var mainWindowSourcePath = Path.Combine(winUiDirectory, "MainWindow.xaml.cs");
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var source = File.ReadAllText(mainWindowSourcePath);
        Assert.DoesNotContain("ProcessingPendingMessage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddInformationMessageOnce", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Processing remains disabled", source, StringComparison.Ordinal);
        Assert.Contains("RequiresUnreferencedCode", source, StringComparison.Ordinal);
        Assert.Contains("ProcessFormIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains("CancelProcessing", source, StringComparison.Ordinal);
        Assert.Contains("\"Cancelling...\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Cancel Processing\"", source, StringComparison.Ordinal);
        Assert.Contains("new ProcessingParameters", source, StringComparison.Ordinal);
        Assert.Contains("GetSelectedPlugins", source, StringComparison.Ordinal);
        Assert.Contains("GameReleaseHelper.GetSafeTableName", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.UpdateProgress", source, StringComparison.Ordinal);
        Assert.Contains("ProcessPlugins(parameters, progress)", source, StringComparison.Ordinal);
        Assert.Contains("\"Error processing FormIDs:", source, StringComparison.Ordinal);
        Assert.Contains("\"Process FormIDs\"", source, StringComparison.Ordinal);

        var xaml = File.ReadAllText(mainWindowXamlPath);
        Assert.Contains("ItemsSource=\"{Binding AvailableGames}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ProcessFormIds_Click\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that Phase 6 keeps binding-critical WinUI state on stable runtime bindings.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_UsesStablePhase6BindingSemantics()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var mainWindowSourcePath = Path.Combine(winUiDirectory, "MainWindow.xaml.cs");
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var source = File.ReadAllText(mainWindowSourcePath);
        var xaml = File.ReadAllText(mainWindowXamlPath);

        Assert.DoesNotContain("x:Bind", xaml, StringComparison.Ordinal);
        Assert.Contains("Root.DataContext = ViewModel;", source, StringComparison.Ordinal);
        Assert.Contains("public MainWindowViewModel ViewModel", source, StringComparison.Ordinal);

        Assert.Contains("ItemsSource=\"{Binding AvailableGames}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedGame, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding GameDirectory, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding DetectedDirectories}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DatabasePath, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FormIdListPath, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PluginFilter, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("ItemsSource=\"{Binding FilteredPlugins}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding Name}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsSelected, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("IsOpen=\"{Binding HasErrorMessages, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ErrorMessages}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsOpen=\"{Binding HasInformationMessages, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding InformationMessages}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("Text=\"{Binding ProgressStatus, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding ProgressValue, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Process FormIDs\"", xaml, StringComparison.Ordinal);
        Assert.Contains("processButton.Content = \"Cancel Processing\";", source, StringComparison.Ordinal);
        Assert.Contains("processButton.Content = \"Process FormIDs\";", source, StringComparison.Ordinal);
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
