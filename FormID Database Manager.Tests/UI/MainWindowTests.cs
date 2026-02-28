#nullable enable

using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Xunit;

namespace FormID_Database_Manager.Tests.UI;

[Collection("UI Tests")]
public class MainWindowTests
{
    [AvaloniaFact]
    public async Task MainWindow_InitializesCorrectly()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), window =>
        {
            Assert.NotNull(window.DataContext);
            Assert.IsType<MainWindowViewModel>(window.DataContext);
            Assert.Equal("FormID Database Manager", window.Title);
            Assert.Equal(750, window.Height);
            Assert.Equal(1200, window.Width);
        });
    }

    [AvaloniaFact]
    public async Task MainWindow_HasRequiredNamedControls()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), window =>
        {
            Assert.NotNull(window.FindControl<TextBox>("GameDirectoryTextBox"));
            Assert.NotNull(window.FindControl<TextBox>("DatabasePathTextBox"));
            Assert.NotNull(window.FindControl<TextBox>("FormIdListPathTextBox"));
            Assert.NotNull(window.FindControl<ItemsControl>("PluginList"));
            Assert.NotNull(window.FindControl<CheckBox>("AdvancedModeCheckBox"));
            Assert.NotNull(window.FindControl<CheckBox>("UpdateModeCheckBox"));
            Assert.NotNull(window.FindControl<Button>("SelectAllButton"));
            Assert.NotNull(window.FindControl<Button>("SelectNoneButton"));
            Assert.NotNull(window.FindControl<Button>("ProcessFormIdsButton"));
            Assert.NotNull(window.FindControl<ProgressBar>("ProcessingProgressBar"));
        });
    }

    [AvaloniaFact]
    public async Task MainWindow_SelectAll_UpdatesAllPlugins()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var selectAllButton = window.FindControl<Button>("SelectAllButton");

            Assert.NotNull(selectAllButton);

            viewModel.Plugins.Add(new PluginListItem { Name = "Plugin1.esp" });
            viewModel.Plugins.Add(new PluginListItem { Name = "Plugin2.esp" });
            viewModel.Plugins.Add(new PluginListItem { Name = "Plugin3.esp" });

            selectAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UiTestHost.FlushUiAsync();

            Assert.All(viewModel.Plugins, p => Assert.True(p.IsSelected));
        });
    }

    [AvaloniaFact]
    public async Task MainWindow_SelectNone_UpdatesAllPlugins()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var selectNoneButton = window.FindControl<Button>("SelectNoneButton");

            Assert.NotNull(selectNoneButton);

            viewModel.Plugins.Add(new PluginListItem { Name = "Plugin1.esp", IsSelected = true });
            viewModel.Plugins.Add(new PluginListItem { Name = "Plugin2.esp", IsSelected = true });
            viewModel.Plugins.Add(new PluginListItem { Name = "Plugin3.esp", IsSelected = true });

            selectNoneButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UiTestHost.FlushUiAsync();

            Assert.All(viewModel.Plugins, p => Assert.False(p.IsSelected));
        });
    }

    [AvaloniaFact]
    public async Task MainWindow_ProgressBar_UpdatesWithProgress()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var progressBar = window.FindControl<ProgressBar>("ProcessingProgressBar");

            Assert.NotNull(progressBar);

            viewModel.IsProcessing = true;
            viewModel.ProgressValue = 25;
            await UiTestHost.FlushUiAsync();

            Assert.Equal(25, progressBar.Value);

            viewModel.ProgressValue = 100;
            await UiTestHost.FlushUiAsync();

            Assert.Equal(100, progressBar.Value);
        });
    }

    [AvaloniaFact]
    public async Task MainWindow_UpdateModeCheckBox_UpdatesState()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), window =>
        {
            var updateModeCheckBox = window.FindControl<CheckBox>("UpdateModeCheckBox");

            Assert.NotNull(updateModeCheckBox);
            Assert.False(updateModeCheckBox.IsChecked);

            updateModeCheckBox.IsChecked = true;
            Assert.True(updateModeCheckBox.IsChecked);
        });
    }
}
