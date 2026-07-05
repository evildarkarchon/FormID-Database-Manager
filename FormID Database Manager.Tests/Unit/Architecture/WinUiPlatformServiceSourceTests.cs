using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
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
        Assert.Contains("\"The WinUI dispatcher rejected queued work. The window may be closing.\"", dispatcherSource,
            StringComparison.Ordinal);

        var pickerSource = File.ReadAllText(pickerPath);
        Assert.Contains("public sealed class WinUiFileDialogService", pickerSource, StringComparison.Ordinal);
        Assert.Contains("IFileDialogService", pickerSource, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Windows.Storage.Pickers", pickerSource, StringComparison.Ordinal);
        Assert.Contains("FolderPicker", pickerSource, StringComparison.Ordinal);
        Assert.Contains("FileSavePicker", pickerSource, StringComparison.Ordinal);
        Assert.Contains("FileOpenPicker", pickerSource, StringComparison.Ordinal);
        Assert.Contains("FileDialogResult", pickerSource, StringComparison.Ordinal);
        Assert.Contains("AppWindow.Id", pickerSource, StringComparison.Ordinal);
        Assert.Contains("\".db\"", pickerSource, StringComparison.Ordinal);
        Assert.Contains("\".txt\"", pickerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindowViewModel", pickerSource, StringComparison.Ordinal);
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
        Assert.Contains("UserWorkflow", source, StringComparison.Ordinal);
        Assert.Contains("new UserWorkflow(", source, StringComparison.Ordinal);
        Assert.Contains("PluginListManager", source, StringComparison.Ordinal);
        Assert.Contains("PluginProcessingService", source, StringComparison.Ordinal);
        Assert.Contains("DirectoryComboBox_SelectionChanged", source, StringComparison.Ordinal);

        var xaml = File.ReadAllText(mainWindowXamlPath);
        Assert.Contains("AutomationProperties.AutomationId=\"DirectoryComboBox\"", xaml, StringComparison.Ordinal);
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

        Assert.Contains("IFileDialogService? fileDialogService", source, StringComparison.Ordinal);
        Assert.Contains("new WinUiFileDialogService(AppWindow)", source, StringComparison.Ordinal);
        Assert.Contains("fileDialogService ?? new WinUiFileDialogService(AppWindow)", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("InitializeWindow();", StringComparison.Ordinal) <
            source.IndexOf("new UserWorkflow(", StringComparison.Ordinal),
            "The production workflow should be constructed after InitializeComponent creates the WinUI window.");

        Assert.Contains("await _userWorkflow.BrowseGameDirectoryAsync()", source, StringComparison.Ordinal);
        Assert.Contains("await _userWorkflow.SelectDatabaseAsync()", source, StringComparison.Ordinal);
        Assert.Contains("await _userWorkflow.SelectFormIdListAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await _fileDialogService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.DatabasePath = path;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.FormIdListPath = path;", source, StringComparison.Ordinal);

        Assert.Contains("Click=\"BrowseDirectory_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnSelectDatabase_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnSelectFormIdList_Click\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that WinUI preserves the migration-critical controls and handlers formerly covered by legacy UI tests.
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
        Assert.Contains("AutomationProperties.AutomationId=\"GameComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"BrowseDirectoryButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"DatabasePathTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"FormIdListPathTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"PluginList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"AdvancedModeCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"UpdateModeCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SelectAllButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SelectNoneButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ProcessFormIdsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ProcessingProgressBar\"", xaml, StringComparison.Ordinal);

        Assert.Contains("Click=\"SelectAll_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"SelectNone_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ProcessFormIds_Click\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AdvancedMode_CheckedChanged", source + xaml, StringComparison.Ordinal);

        Assert.Contains("_userWorkflow.SelectAllPlugins();", source, StringComparison.Ordinal);
        Assert.Contains("_userWorkflow.SelectNoPlugins();", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PropertyChanged += ViewModel_PropertyChanged;", source, StringComparison.Ordinal);
        Assert.Contains("await _userWorkflow.RefreshPluginsForCurrentSelectionAsync();", source, StringComparison.Ordinal);
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
        Assert.Contains("await _userWorkflow.ProcessFormIdsAsync();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProcessingParameters", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultDatabasePathProvider.CreateDefaultDatabasePath", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.GetCurrentDirectory()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("processButton.Content", source, StringComparison.Ordinal);

        var xaml = File.ReadAllText(mainWindowXamlPath);
        Assert.Contains("ItemsSource=\"{Binding AvailableGames}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ProcessFormIds_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding ProcessButtonText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
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
        Assert.Contains("Text=\"{Binding PluginFilter, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml,
            StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding UpdateMode, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding AdvancedMode, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("ItemsSource=\"{Binding FilteredPlugins}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding Name}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsSelected, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("IsOpen=\"{Binding HasErrorMessages, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ErrorMessages}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsOpen=\"{Binding HasInformationMessages, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding InformationMessages}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("Text=\"{Binding ProgressStatus, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding ProgressValue, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding ProcessButtonText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that bottom notifications are the last footer row so progress controls cannot cover them.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_KeepsBottomNotificationsAfterProcessingControls()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var xaml = File.ReadAllText(mainWindowXamlPath);
        var processButtonIndex = xaml.IndexOf(
            "AutomationProperties.AutomationId=\"ProcessFormIdsButton\"",
            StringComparison.Ordinal);
        var informationBarIndex = xaml.IndexOf("IsOpen=\"{Binding HasInformationMessages, Mode=OneWay}\"",
            StringComparison.Ordinal);

        Assert.Contains("<StackPanel Grid.Row=\"5\" Margin=\"0,0,0,4\" Spacing=\"8\">", xaml, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(xaml, "CornerRadius=\"4\""));
        Assert.Equal(2, CountOccurrences(xaml, "Margin=\"0,0,0,8\""));
        Assert.True(
            processButtonIndex >= 0 && processButtonIndex < informationBarIndex,
            "The progress/action footer should be declared before the bottom notification bars.");
    }

    /// <summary>
    /// Verifies that Phase 10 records responsive-layout resources and uses them in the main shell.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_DefinesPhase10ResponsiveLayoutContract()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var appXamlPath = Path.Combine(winUiDirectory, "App.xaml");
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var appXaml = File.ReadAllText(appXamlPath);
        var xaml = File.ReadAllText(mainWindowXamlPath);

        Assert.Contains("x:Key=\"Phase10WideWidth\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Phase10MediumWidth\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Phase10NarrowWidth\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PluginListMinHeight\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinWidth\" Value=\"0\" />", appXaml, StringComparison.Ordinal);

        Assert.DoesNotContain("x:Name=\"WorkflowScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,Auto,*,Auto,Auto,Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"GameDirectoryTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"2*,*,Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"{StaticResource PluginListMinHeight}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Orientation=\"Horizontal\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ProgressStatusTextBlock\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that Phase 10 exposes UI Automation metadata for controls whose content is insufficient.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_DefinesPhase10AutomationMetadata()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var xaml = File.ReadAllText(mainWindowXamlPath);

        Assert.Contains("x:Name=\"GameLabel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LabeledBy=\"{Binding ElementName=GameLabel}\"", xaml,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GameDirectoryLabel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LabeledBy=\"{Binding ElementName=GameDirectoryLabel}\"", xaml,
            StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Detected installed directory\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DatabasePathLabel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LabeledBy=\"{Binding ElementName=DatabasePathLabel}\"", xaml,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FormIdListPathLabel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LabeledBy=\"{Binding ElementName=FormIdListPathLabel}\"", xaml,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PluginFilterLabel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LabeledBy=\"{Binding ElementName=PluginFilterLabel}\"", xaml,
            StringComparison.Ordinal);

        Assert.Contains("AutomationProperties.Name=\"Plugin selection list\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.HelpText=\"Use the arrow keys to move through plugins and Space to toggle a plugin checkbox.\"",
            xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Name}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Processing status\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Processing progress\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Error messages\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Information messages\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that Phase 10 keeps primary commands reachable through access keys and workflow tab order.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_DefinesPhase10KeyboardAccess()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var xaml = File.ReadAllText(mainWindowXamlPath);

        Assert.Contains("AutomationProperties.AutomationId=\"GameComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TabIndex=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"BrowseDirectoryButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AccessKey=\"B\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SelectDatabaseButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AccessKey=\"D\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SelectListFileButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AccessKey=\"L\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"PluginList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TabIndex=\"9\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SelectAllButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AccessKey=\"A\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SelectNoneButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AccessKey=\"N\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ProcessFormIdsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AccessKey=\"P\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that Phase 10 leaves text scaling enabled and constrains long visible text.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_KeepsTextScalingAndLongTextResilient()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var appXamlPath = Path.Combine(winUiDirectory, "App.xaml");
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var source = File.ReadAllText(appXamlPath) + File.ReadAllText(mainWindowXamlPath);

        Assert.DoesNotContain("IsTextScaleFactorEnabled=\"False\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsTextScaleFactorEnabled=\"false\"", source, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"WrapWholeWords\"", source, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"ScrollViewer.HorizontalScrollBarVisibility\" Value=\"Auto\" />",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that Phase 10 keeps plugin selection on a constrained native ListView.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_KeepsPluginListVirtualizationFriendly()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var xaml = File.ReadAllText(mainWindowXamlPath);
        var pluginListIndex = xaml.IndexOf(
            "AutomationProperties.AutomationId=\"PluginList\"",
            StringComparison.Ordinal);
        var pluginBorderIndex =
            xaml.LastIndexOf(
                "Style=\"{StaticResource PluginListBorderStyle}\"",
                pluginListIndex,
                StringComparison.Ordinal);

        Assert.True(pluginListIndex >= 0,
            "The plugin list should remain an automation-addressable native WinUI ListView.");
        Assert.True(pluginBorderIndex >= 0, "The plugin list should remain inside the constrained styled border.");

        var pluginContainerSegment = xaml[pluginBorderIndex..pluginListIndex];
        Assert.DoesNotContain("<ScrollViewer", pluginContainerSegment, StringComparison.Ordinal);
        Assert.Contains("<ListView", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding FilteredPlugins}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"{StaticResource PluginListMinHeight}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsSelected, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that Phase 10 app surfaces stay resource-driven instead of introducing hard-coded colors.
    /// </summary>
    [Fact]
    public void WinUiMainWindow_UsesThemeResourcesForPhase10Surfaces()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var appXamlPath = Path.Combine(winUiDirectory, "App.xaml");
        var mainWindowXamlPath = Path.Combine(winUiDirectory, "MainWindow.xaml");

        var source = File.ReadAllText(appXamlPath) + File.ReadAllText(mainWindowXamlPath);

        Assert.Contains("Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\"", source,
            StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\"", source, StringComparison.Ordinal);
        Assert.Contains("{ThemeResource CardBackgroundFillColorDefaultBrush}", source, StringComparison.Ordinal);
        Assert.Contains("{ThemeResource CardStrokeColorDefaultBrush}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"#", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"#", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderBrush=\"#", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Fill=\"#", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the unpackaged self-contained release lane is defined in the base project config and that
    /// legacy publish profiles and MSIX artifacts are removed (the portable release uses publish-portable.ps1).
    /// </summary>
    [Fact]
    public void WinUiProject_DefinesScopedReleasePublishProfiles()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var projectPath = Path.Combine(winUiDirectory, "FormID Database Manager.WinUI.csproj");
        var packageManifestPath = Path.Combine(winUiDirectory, "Package.appxmanifest");
        var publishProfilesDirectory = Path.Combine(winUiDirectory, "Properties", "PublishProfiles");
        var packagedProfilePath = Path.Combine(publishProfilesDirectory, "win-x64-msix.pubxml");
        var genericX64ProfilePath = Path.Combine(publishProfilesDirectory, "win-x64.pubxml");
        var genericX86ProfilePath = Path.Combine(publishProfilesDirectory, "win-x86.pubxml");
        var genericArm64ProfilePath = Path.Combine(publishProfilesDirectory, "win-arm64.pubxml");
        var frameworkDependentProfilePath = Path.Combine(
            publishProfilesDirectory,
            "win-x64-unpackaged-framework-dependent.pubxml");
        var selfContainedProfilePath = Path.Combine(
            publishProfilesDirectory,
            "win-x64-unpackaged-self-contained.pubxml");
        var singleFileProfilePath = Path.Combine(
            publishProfilesDirectory,
            "win-x64-unpackaged-single-file.pubxml");

        var projectSource = File.ReadAllText(projectPath);
        Assert.Contains("<WindowsPackageType>None</WindowsPackageType>", projectSource, StringComparison.Ordinal);
        Assert.Contains("<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>", projectSource,
            StringComparison.Ordinal);
        Assert.Contains("<WindowsAppSdkDeploymentManagerInitialize>false</WindowsAppSdkDeploymentManagerInitialize>",
            projectSource, StringComparison.Ordinal);
        Assert.Contains(
            "<WindowsAppSdkUndockedRegFreeWinRTInitialize>true</WindowsAppSdkUndockedRegFreeWinRTInitialize>",
            projectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<EnableMsixTooling>true</EnableMsixTooling>", projectSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<HasPackageAndPublishMenu>true</HasPackageAndPublishMenu>", projectSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<ProjectCapability Include=\"Msix\" />", projectSource, StringComparison.Ordinal);
        Assert.False(File.Exists(packageManifestPath),
            "MSIX package manifest should be removed for portable unpackaged releases.");
        Assert.False(File.Exists(packagedProfilePath), "MSIX publish profile should be removed.");
        Assert.False(File.Exists(genericX64ProfilePath),
            "Generic x64 publish profile should be removed in favor of the explicit portable profile.");
        Assert.False(File.Exists(genericX86ProfilePath),
            "x86 publish profile should not exist because releases are x64-only.");
        Assert.False(File.Exists(genericArm64ProfilePath),
            "ARM64 publish profile should not exist because releases are x64-only.");
        Assert.False(File.Exists(frameworkDependentProfilePath),
            "Framework-dependent profile should be removed because portable releases carry runtimes.");
        Assert.False(File.Exists(singleFileProfilePath),
            "Single-file profile should not exist because the WinUI single-file lane requires MSIX tooling.");
        Assert.False(File.Exists(selfContainedProfilePath),
            "Self-contained publish profile should be removed because the portable release is produced by the build-based publish-portable.ps1 script (dotnet publish omits the XAML resources and crashes on launch in this SDK).");
    }

    /// <summary>
    /// Verifies that command-line debug and publish entry points use the unpackaged self-contained defaults.
    /// </summary>
    [Fact]
    public void WinUiDebugCommands_UseUnpackagedSelfContainedProperties()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expectedRunCommand =
            "dotnet run --project \"FormID Database Manager.WinUI\" -p:Platform=x64";
        var documentationPaths = new[]
        {
            Path.Combine(repositoryRoot, "README.md"), Path.Combine(repositoryRoot, "AGENTS.md"),
            Path.Combine(repositoryRoot, "CLAUDE.md"), Path.Combine(repositoryRoot, "GEMINI.md"),
        };

        foreach (var documentationPath in documentationPaths)
        {
            var documentation = File.ReadAllText(documentationPath);
            Assert.Contains(expectedRunCommand, documentation, StringComparison.Ordinal);
        }

        var tasksJson = File.ReadAllText(Path.Combine(repositoryRoot, ".vscode", "tasks.json"));
        Assert.Contains("${workspaceFolder}/scripts/publish-portable.ps1", tasksJson, StringComparison.Ordinal);
        Assert.Contains("--property:Platform=x64", tasksJson, StringComparison.Ordinal);
        Assert.DoesNotContain("--property:WindowsPackageType=None", tasksJson, StringComparison.Ordinal);
        Assert.DoesNotContain("--property:WindowsAppSDKSelfContained=true", tasksJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that default solution builds remain intentionally Windows-only.
    /// </summary>
    [Fact]
    public void WinUiProject_KeepsDefaultSolutionBuildWindowsOnly()
    {
        var winUiDirectory = GetWinUiProjectDirectory();
        var projectPath = Path.Combine(winUiDirectory, "FormID Database Manager.WinUI.csproj");

        var projectSource = File.ReadAllText(projectPath);
        var projectXml = XDocument.Parse(projectSource);
        var targetFrameworks = projectXml.Descendants("TargetFramework")
            .Select(element => element.Value)
            .Concat(projectXml.Descendants("TargetFrameworks")
                .SelectMany(element => element.Value.Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .ToArray();

        Assert.Contains(targetFrameworks,
            targetFramework => targetFramework.Contains("-windows", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("<EnableWindowsTargeting>true</EnableWindowsTargeting>", projectSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "EnableWindowsTargeting is intentionally omitted so default solution builds fail on non-Windows hosts.",
            projectSource,
            StringComparison.Ordinal);
    }

    private static string GetWinUiProjectDirectory()
    {
        return Path.Combine(FindRepositoryRoot(), "FormID Database Manager.WinUI");
    }

    /// <summary>
    /// Counts exact source-text matches for architecture tests that pin XAML declarations.
    /// </summary>
    /// <param name="source">The source text to scan.</param>
    /// <param name="value">The exact text to count.</param>
    /// <returns>The number of non-overlapping occurrences of <paramref name="value"/>.</returns>
    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var startIndex = 0;

        while (true)
        {
            var foundIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal);
            if (foundIndex < 0)
            {
                return count;
            }

            count++;
            startIndex = foundIndex + value.Length;
        }
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
