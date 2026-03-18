using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.UI;

[Collection("UI Tests")]
public class ControlTests
{
    [AvaloniaFact]
    public async Task TextBox_BindsToViewModelProperties()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;

            var gameDirectoryTextBox = window.FindControl<TextBox>("GameDirectoryTextBox");
            var databasePathTextBox = window.FindControl<TextBox>("DatabasePathTextBox");
            var formIdListTextBox = window.FindControl<TextBox>("FormIdListPathTextBox");

            Assert.NotNull(gameDirectoryTextBox);
            Assert.NotNull(databasePathTextBox);
            Assert.NotNull(formIdListTextBox);

            Assert.True(gameDirectoryTextBox.IsReadOnly);
            Assert.True(databasePathTextBox.IsReadOnly);
            Assert.True(formIdListTextBox.IsReadOnly);

            viewModel.GameDirectory = @"C:\Games\Skyrim";
            viewModel.DatabasePath = @"C:\Games\test.db";
            viewModel.FormIdListPath = @"C:\Games\formids.txt";
            await UiTestHost.FlushUiAsync();

            Assert.Equal(viewModel.GameDirectory, gameDirectoryTextBox.Text);
            Assert.Equal(viewModel.DatabasePath, databasePathTextBox.Text);
            Assert.Equal(viewModel.FormIdListPath, formIdListTextBox.Text);
        });
    }

    [AvaloniaFact]
    public async Task CheckBox_BindsCorrectly()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), async window =>
        {
            var advancedModeCheckBox = window.FindControl<CheckBox>("AdvancedModeCheckBox");
            var updateModeCheckBox = window.FindControl<CheckBox>("UpdateModeCheckBox");

            Assert.NotNull(advancedModeCheckBox);
            Assert.NotNull(updateModeCheckBox);

            Assert.False(advancedModeCheckBox.IsChecked ?? false);
            Assert.False(updateModeCheckBox.IsChecked ?? false);

            advancedModeCheckBox.IsChecked = true;
            updateModeCheckBox.IsChecked = true;
            await UiTestHost.FlushUiAsync();

            Assert.True(advancedModeCheckBox.IsChecked ?? false);
            Assert.True(updateModeCheckBox.IsChecked ?? false);
        });
    }

    [AvaloniaFact]
    public async Task ItemsControl_DisplaysPlugins()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var pluginList = window.FindControl<ItemsControl>("PluginList");

            Assert.NotNull(pluginList);

            viewModel.Plugins.Add(new PluginListItem { Name = "Skyrim.esm" });
            viewModel.Plugins.Add(new PluginListItem { Name = "Update.esm" });
            viewModel.Plugins.Add(new PluginListItem { Name = "Dawnguard.esm" });
            await UiTestHost.FlushUiAsync();

            Assert.Equal(3, viewModel.Plugins.Count);
            viewModel.PluginFilter = "";
            await UiTestHost.FlushUiAsync();
            Assert.Equal(3, viewModel.FilteredPlugins.Count);
            Assert.Same(viewModel.FilteredPlugins, pluginList.ItemsSource);
        });
    }

    [AvaloniaFact]
    public async Task GameComboBox_BindsToSelectedGame()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var gameComboBox = window.FindControl<ComboBox>("GameComboBox");

            Assert.NotNull(gameComboBox);
            Assert.Equal(10, gameComboBox.ItemCount);

            viewModel.SelectedGame = GameRelease.SkyrimSE;
            await UiTestHost.FlushUiAsync();

            Assert.Equal(GameRelease.SkyrimSE, gameComboBox.SelectedItem);
        });
    }

    [AvaloniaFact]
    public async Task Button_StatesCorrectlyEnabled()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), async window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var browseDirectoryButton = window.FindControl<Button>("BrowseDirectoryButton");
            var processButton = window.FindControl<Button>("ProcessFormIdsButton");

            Assert.NotNull(browseDirectoryButton);
            Assert.NotNull(processButton);

            Assert.True(browseDirectoryButton.IsEnabled);
            Assert.True(processButton.IsEnabled);

            viewModel.IsProcessing = true;
            await UiTestHost.FlushUiAsync();

            Assert.True(processButton.IsEnabled);
            Assert.Equal("Process FormIDs", processButton.Content);
        });
    }

    [AvaloniaFact]
    public async Task PluginCheckBox_ToggleSelection()
    {
        await UiTestHost.WithWindowAsync(() => new MainWindow(), window =>
        {
            var viewModel = (MainWindowViewModel)window.DataContext!;

            var plugin = new PluginListItem { Name = "TestPlugin.esp" };
            viewModel.Plugins.Add(plugin);

            Assert.False(plugin.IsSelected);

            plugin.IsSelected = true;
            Assert.True(plugin.IsSelected);

            plugin.IsSelected = false;
            Assert.False(plugin.IsSelected);
        });
    }
}
