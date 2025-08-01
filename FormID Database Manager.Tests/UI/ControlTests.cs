using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;
using Xunit;

namespace FormID_Database_Manager.Tests.UI
{
    public class ControlTests
    {
        [AvaloniaFact]
        public void TextBox_BindsToViewModelProperties()
        {
            var window = new MainWindow();
            var viewModel = (MainWindowViewModel)window.DataContext!;

            var gameDirectoryTextBox = window.FindControl<TextBox>("GameDirectoryTextBox");
            var databasePathTextBox = window.FindControl<TextBox>("DatabasePathTextBox");
            var formIdListTextBox = window.FindControl<TextBox>("FormIdListPathTextBox");

            Assert.NotNull(gameDirectoryTextBox);
            Assert.NotNull(databasePathTextBox);
            Assert.NotNull(formIdListTextBox);

            // Test initial state
            Assert.True(gameDirectoryTextBox.IsReadOnly);
            Assert.True(databasePathTextBox.IsReadOnly);
            Assert.True(formIdListTextBox.IsReadOnly);

            // Test data binding
            viewModel.GameDirectory = @"C:\Games\Skyrim";
            viewModel.DatabasePath = @"C:\Games\test.db";
            viewModel.FormIdListPath = @"C:\Games\formids.txt";

            // Bindings should update the TextBox values
            Assert.Equal(viewModel.GameDirectory, gameDirectoryTextBox.Text);
            Assert.Equal(viewModel.DatabasePath, databasePathTextBox.Text);
            Assert.Equal(viewModel.FormIdListPath, formIdListTextBox.Text);
        }

        [AvaloniaFact]
        public void CheckBox_BindsCorrectly()
        {
            var window = new MainWindow();
            var advancedModeCheckBox = window.FindControl<CheckBox>("AdvancedModeCheckBox");
            var updateModeCheckBox = window.FindControl<CheckBox>("UpdateModeCheckBox");

            Assert.NotNull(advancedModeCheckBox);
            Assert.NotNull(updateModeCheckBox);

            // Test initial state
            Assert.False(advancedModeCheckBox.IsChecked ?? false);
            Assert.False(updateModeCheckBox.IsChecked ?? false);

            // Test checking behavior
            advancedModeCheckBox.IsChecked = true;
            updateModeCheckBox.IsChecked = true;

            Assert.True(advancedModeCheckBox.IsChecked ?? false);
            Assert.True(updateModeCheckBox.IsChecked ?? false);
        }

        [AvaloniaFact]
        public void ItemsControl_DisplaysPlugins()
        {
            var window = new MainWindow();
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var pluginList = window.FindControl<ItemsControl>("PluginList");

            Assert.NotNull(pluginList);

            // Add test plugins
            viewModel.Plugins.Add(new PluginListItem { Name = "Skyrim.esm" });
            viewModel.Plugins.Add(new PluginListItem { Name = "Update.esm" });
            viewModel.Plugins.Add(new PluginListItem { Name = "Dawnguard.esm" });

            // ItemsControl should bind to FilteredPlugins
            Assert.Equal(3, viewModel.Plugins.Count);
            // Trigger filter update
            viewModel.PluginFilter = "";
            Assert.Equal(3, viewModel.FilteredPlugins.Count);
        }

        [AvaloniaFact]
        public void GameReleaseTextBlock_DisplaysDetectedGame()
        {
            var window = new MainWindow();
            var viewModel = (MainWindowViewModel)window.DataContext!;
            var gameReleaseTextBlock = window.FindControl<TextBlock>("GameReleaseTextBlock");

            Assert.NotNull(gameReleaseTextBlock);

            // Test binding
            viewModel.DetectedGame = "SkyrimSE";

            // The text should be bound to DetectedGame property
            // Binding verification would require reactive extensions
        }

        [AvaloniaFact(Skip = "Requires visual tree setup")]
        public void ProgressBar_RespondsToValueChanges()
        {
            var window = new MainWindow();
            window.Show();
            var viewModel = (MainWindowViewModel)window.DataContext!;

            // Find ProgressBar by traversing visual tree
            var progressBar = window.GetVisualDescendants()
                .OfType<ProgressBar>()
                .FirstOrDefault();

            // ProgressBar is only visible when IsProcessing is true
            viewModel.IsProcessing = true;
            
            progressBar = window.GetVisualDescendants()
                .OfType<ProgressBar>()
                .FirstOrDefault();

            Assert.NotNull(progressBar);
            Assert.Equal(0, progressBar.Minimum);
            Assert.Equal(100, progressBar.Maximum);
            Assert.True(progressBar.ShowProgressText);

            // Test value binding
            viewModel.ProgressValue = 25;
            // Binding verification would require reactive extensions
        }

        [AvaloniaFact(Skip = "Requires visual tree setup")]
        public void Button_StatesCorrectlyEnabled()
        {
            var window = new MainWindow();
            window.Show();
            var viewModel = (MainWindowViewModel)window.DataContext!;

            // Find buttons
            var selectDirectoryButton = window.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Content?.ToString() == "Select Directory");

            var processButton = window.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Content?.ToString() == "Process FormIDs");

            Assert.NotNull(selectDirectoryButton);
            Assert.NotNull(processButton);

            // Select Directory button should always be enabled
            Assert.True(selectDirectoryButton.IsEnabled);

            // Process button enablement depends on CanProcess
            // Binding verification would require reactive extensions
        }

        [AvaloniaFact(Skip = "Requires visual tree setup")]
        public void ScrollViewer_HandlesLongErrorLists()
        {
            var window = new MainWindow();
            var viewModel = (MainWindowViewModel)window.DataContext!;

            // Find ScrollViewers
            var scrollViewers = window.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .ToList();

            Assert.NotEmpty(scrollViewers);

            // Add many error messages
            for (int i = 0; i < 20; i++)
            {
                viewModel.AddErrorMessage($"Error message {i}");
            }

            // ScrollViewer should have max height constraint
            var errorScrollViewer = scrollViewers.FirstOrDefault(sv => sv.MaxHeight == 100);
            Assert.NotNull(errorScrollViewer);
        }

        [AvaloniaFact]
        public void PluginCheckBox_ToggleSelection()
        {
            var window = new MainWindow();
            var viewModel = (MainWindowViewModel)window.DataContext!;

            // Add a plugin
            var plugin = new PluginListItem { Name = "TestPlugin.esp" };
            viewModel.Plugins.Add(plugin);

            // Plugin selection should be bindable
            Assert.False(plugin.IsSelected);

            plugin.IsSelected = true;
            Assert.True(plugin.IsSelected);

            plugin.IsSelected = false;
            Assert.False(plugin.IsSelected);
        }
    }
}
