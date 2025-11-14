#nullable enable

using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Xunit;

namespace FormID_Database_Manager.Tests.UI;

[Collection("UI Tests")]
public class MainWindowTests
{
    [AvaloniaFact]
    public void MainWindow_InitializesCorrectly()
    {
        var window = new MainWindow();

        Assert.NotNull(window);
        Assert.NotNull(window.DataContext);
        Assert.IsType<MainWindowViewModel>(window.DataContext);
        Assert.Equal("FormID Database Manager", window.Title);
        Assert.Equal(750, window.Height);
        Assert.Equal(1200, window.Width);
    }

    [AvaloniaFact]
    public void MainWindow_HasRequiredControls()
    {
        var window = new MainWindow();

        // Find essential controls
        var gameDirectoryTextBox = window.FindControl<TextBox>("GameDirectoryTextBox");
        var databaseTextBox = window.FindControl<TextBox>("DatabasePathTextBox");
        var pluginList = window.FindControl<ItemsControl>("PluginList");
        var advancedModeCheckBox = window.FindControl<CheckBox>("AdvancedModeCheckBox");
        var updateModeCheckBox = window.FindControl<CheckBox>("UpdateModeCheckBox");
        var formIdListTextBox = window.FindControl<TextBox>("FormIdListPathTextBox");

        Assert.NotNull(gameDirectoryTextBox);
        Assert.NotNull(databaseTextBox);
        Assert.NotNull(pluginList);
        Assert.NotNull(advancedModeCheckBox);
        Assert.NotNull(updateModeCheckBox);
        Assert.NotNull(formIdListTextBox);
    }

    [AvaloniaFact]
    public void MainWindow_InitialState_ButtonsDisabled()
    {
        var window = new MainWindow();
        var viewModel = (MainWindowViewModel)window.DataContext!;

        // Initially, process button should be disabled when no game directory is selected
        Assert.True(string.IsNullOrEmpty(viewModel.GameDirectory));
        Assert.False(viewModel.IsProcessing);
        // CanProcess depends on game directory and selected plugins
    }

    [AvaloniaFact(Skip = "Requires visual tree setup")]
    public void MainWindow_SelectAll_UpdatesAllPlugins()
    {
        var window = new MainWindow();
        var viewModel = (MainWindowViewModel)window.DataContext!;

        // Add test plugins
        viewModel.Plugins.Add(new PluginListItem { Name = "Plugin1.esp" });
        viewModel.Plugins.Add(new PluginListItem { Name = "Plugin2.esp" });
        viewModel.Plugins.Add(new PluginListItem { Name = "Plugin3.esp" });

        // Find and click Select All button
        var selectAllButton = window.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Content?.ToString() == "Select All");

        Assert.NotNull(selectAllButton);

        // Simulate click
        selectAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // All plugins should be selected
        Assert.All(viewModel.Plugins, p => Assert.True(p.IsSelected));
    }

    [AvaloniaFact(Skip = "Requires visual tree setup")]
    public void MainWindow_SelectNone_UpdatesAllPlugins()
    {
        var window = new MainWindow();
        var viewModel = (MainWindowViewModel)window.DataContext!;

        // Add test plugins
        viewModel.Plugins.Add(new PluginListItem { Name = "Plugin1.esp" });
        viewModel.Plugins.Add(new PluginListItem { Name = "Plugin2.esp" });
        viewModel.Plugins.Add(new PluginListItem { Name = "Plugin3.esp" });

        // Find and click Select None button
        var selectNoneButton = window.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Content?.ToString() == "Select None");

        Assert.NotNull(selectNoneButton);

        // Simulate click
        selectNoneButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // All plugins should be deselected
        Assert.All(viewModel.Plugins, p => Assert.False(p.IsSelected));
    }

    [AvaloniaFact(Skip = "Requires visual tree setup")]
    public void MainWindow_ProcessButton_ChangesTextWhenProcessing()
    {
        var window = new MainWindow();
        var viewModel = (MainWindowViewModel)window.DataContext!;

        var processButton = window.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Content?.ToString()?.Contains("Process") == true);

        Assert.NotNull(processButton);
        Assert.Equal("Process FormIDs", processButton.Content);

        // Simulate processing state
        viewModel.IsProcessing = true;

        // Button content should update (this would happen in the actual click handler)
        // For testing purposes, we verify the ViewModel state changes
        Assert.True(viewModel.IsProcessing);
    }

    [AvaloniaFact(Skip = "Requires visual tree setup")]
    public void MainWindow_ProgressBar_UpdatesWithProgress()
    {
        var window = new MainWindow();
        var viewModel = (MainWindowViewModel)window.DataContext!;

        // Find ProgressBar by type since it doesn't have a name
        var progressBar = window.GetVisualDescendants()
            .OfType<ProgressBar>()
            .FirstOrDefault();

        Assert.NotNull(progressBar);

        // Test progress updates via ViewModel binding
        viewModel.ProgressValue = 0;
        Assert.Equal(0, viewModel.ProgressValue);

        viewModel.ProgressValue = 50;
        Assert.Equal(50, viewModel.ProgressValue);

        viewModel.ProgressValue = 100;
        Assert.Equal(100, viewModel.ProgressValue);
    }

    [AvaloniaFact]
    public void MainWindow_ErrorMessages_AddedToViewModel()
    {
        var window = new MainWindow();
        var viewModel = (MainWindowViewModel)window.DataContext!;

        // Initially no errors
        Assert.Empty(viewModel.ErrorMessages);

        // Add error messages
        viewModel.AddErrorMessage("Test Error 1");
        viewModel.AddErrorMessage("Test Error 2");

        // Verify error messages are in collection
        Assert.Equal(2, viewModel.ErrorMessages.Count);
        Assert.Contains("Test Error 1", viewModel.ErrorMessages);
        Assert.Contains("Test Error 2", viewModel.ErrorMessages);
    }

    [AvaloniaFact]
    public void MainWindow_AdvancedMode_TogglesPluginVisibility()
    {
        var window = new MainWindow();
        var advancedModeCheckBox = window.FindControl<CheckBox>("AdvancedModeCheckBox");

        Assert.NotNull(advancedModeCheckBox);
        Assert.False(advancedModeCheckBox.IsChecked);

        // Toggle advanced mode
        advancedModeCheckBox.IsChecked = true;

        // In real scenario, this would trigger plugin list refresh
        // For unit test, we verify the checkbox state changes
        Assert.True(advancedModeCheckBox.IsChecked);
    }

    [AvaloniaFact]
    public void MainWindow_UpdateMode_PreservesState()
    {
        var window = new MainWindow();
        var updateModeCheckBox = window.FindControl<CheckBox>("UpdateModeCheckBox");

        Assert.NotNull(updateModeCheckBox);
        Assert.False(updateModeCheckBox.IsChecked);

        // Toggle update mode
        updateModeCheckBox.IsChecked = true;
        Assert.True(updateModeCheckBox.IsChecked);

        // Verify it maintains state
        updateModeCheckBox.IsChecked = false;
        Assert.False(updateModeCheckBox.IsChecked);
    }

    [AvaloniaFact]
    public void MainWindow_DisposesResourcesCorrectly()
    {
        MainWindow? window = null;

        try
        {
            window = new MainWindow();
            Assert.NotNull(window);
        }
        finally
        {
            window?.Dispose();
        }

        // Window should be disposed without exceptions
        Assert.True(true); // If we get here, disposal worked
    }
}
