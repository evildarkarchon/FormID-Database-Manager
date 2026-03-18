#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
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
            Assert.NotNull(window.FindControl<ComboBox>("GameComboBox"));
            Assert.NotNull(window.FindControl<Button>("BrowseDirectoryButton"));
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

    [AvaloniaFact]
    public async Task MainWindow_DirectorySelectionChanged_ReloadsPluginsForSelectedDirectory()
    {
        var testRoot = CreateTestRoot();
        var firstInstall = CreateGameInstall(testRoot, "First", "First.esp");
        var secondInstall = CreateGameInstall(testRoot, "Second", "Second.esp");

        try
        {
            var viewModel = new MainWindowViewModel();
            var gameDetectionService = new GameDetectionService();
            var pluginListManager = CreatePluginListManager(
                viewModel,
                gameDetectionService,
                new Dictionary<string, string[]>
                {
                    [Path.Combine(firstInstall, "Data")] = ["First.esp"],
                    [Path.Combine(secondInstall, "Data")] = ["Second.esp"]
                });

            viewModel.SelectedGame = GameRelease.SkyrimSE;
            viewModel.DetectedDirectories.Add(firstInstall);
            viewModel.DetectedDirectories.Add(secondInstall);
            viewModel.GameDirectory = firstInstall;

            await UiTestHost.WithWindowAsync(
                () => new MainWindow(
                    viewModel,
                    gameDetectionService,
                    null,
                    pluginListManager,
                    CreatePluginProcessingService(viewModel)),
                async window =>
                {
                    await pluginListManager.RefreshPluginList(firstInstall, GameRelease.SkyrimSE, viewModel.Plugins, showAdvanced: false);
                    await WaitUntilAsync(() => viewModel.Plugins.Count == 1 && viewModel.Plugins[0].Name == "First.esp");

                    var directoryComboBox = window.FindControl<ComboBox>("DirectoryComboBox");
                    var selectionChangedHandler = typeof(MainWindow).GetMethod(
                        "DirectoryComboBox_SelectionChanged",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    Assert.NotNull(directoryComboBox);
                    Assert.NotNull(selectionChangedHandler);

                    viewModel.GameDirectory = secondInstall;
                    selectionChangedHandler.Invoke(
                        window,
                        [
                            directoryComboBox,
                            new SelectionChangedEventArgs(
                                ComboBox.SelectionChangedEvent,
                                new[] { firstInstall },
                                new[] { secondInstall })
                        ]);

                    await WaitUntilAsync(() => viewModel.GameDirectory == secondInstall);
                    await WaitUntilAsync(() => viewModel.Plugins.Count == 1 && viewModel.Plugins[0].Name == "Second.esp");

                    Assert.Equal(secondInstall, viewModel.GameDirectory);
                });
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_GameSelection_IgnoresStaleFolderLookupResults()
    {
        var testRoot = CreateTestRoot();
        var olderInstall = CreateGameInstall(testRoot, "Older", "Older.esp");
        var newerInstall = CreateGameInstall(testRoot, "Newer", "Newer.esp");

        try
        {
            var viewModel = new MainWindowViewModel();
            var gameDetectionService = new GameDetectionService();
            var pluginListManager = CreatePluginListManager(
                viewModel,
                gameDetectionService,
                new Dictionary<string, string[]>
                {
                    [Path.Combine(olderInstall, "Data")] = ["Older.esp"],
                    [Path.Combine(newerInstall, "Data")] = ["Newer.esp"]
                });
            var gameLocationService = new BlockingGameLocationService(olderInstall, newerInstall);

            await UiTestHost.WithWindowAsync(
                () => new MainWindow(
                    viewModel,
                    gameDetectionService,
                    gameLocationService,
                    pluginListManager,
                    CreatePluginProcessingService(viewModel)),
                async window =>
                {
                    var gameComboBox = window.FindControl<ComboBox>("GameComboBox");

                    Assert.NotNull(gameComboBox);

                    gameComboBox.SelectedItem = GameRelease.SkyrimSE;
                    await gameLocationService.WaitForFirstLookupAsync();

                    gameComboBox.SelectedItem = GameRelease.Fallout4;

                    await WaitUntilAsync(() =>
                        viewModel.GameDirectory == newerInstall &&
                        viewModel.Plugins.Count == 1 &&
                        viewModel.Plugins[0].Name == "Newer.esp");

                    gameLocationService.ReleaseFirstLookup();
                    await gameLocationService.WaitForFirstLookupCompletionAsync();
                    await Task.Delay(100);
                    await UiTestHost.FlushUiAsync();

                    Assert.Equal(newerInstall, viewModel.GameDirectory);
                    Assert.Single(viewModel.Plugins);
                    Assert.Equal("Newer.esp", viewModel.Plugins[0].Name);
                });
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    private static PluginListManager CreatePluginListManager(
        MainWindowViewModel viewModel,
        GameDetectionService gameDetectionService,
        IReadOnlyDictionary<string, string[]> pluginsByDataPath)
    {
        return new PluginListManager(
            gameDetectionService,
            viewModel,
            new AvaloniaThreadDispatcher(),
            new TestGameLoadOrderProvider(pluginsByDataPath));
    }

    private static PluginProcessingService CreatePluginProcessingService(MainWindowViewModel viewModel)
    {
        return new PluginProcessingService(new DatabaseService(), viewModel, new AvaloniaThreadDispatcher());
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mainwindow-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateGameInstall(string rootPath, string name, params string[] pluginNames)
    {
        var installPath = Path.Combine(rootPath, name);
        var dataPath = Path.Combine(installPath, "Data");
        Directory.CreateDirectory(dataPath);

        foreach (var pluginName in pluginNames)
        {
            File.WriteAllBytes(Path.Combine(dataPath, pluginName), [1, 2, 3, 4]);
        }

        return installPath;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            await UiTestHost.FlushUiAsync();
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), "Timed out waiting for the expected UI state.");
    }

    private sealed class TestGameLoadOrderProvider(IReadOnlyDictionary<string, string[]> pluginsByDataPath) : IGameLoadOrderProvider
    {
        public GameLoadOrderSnapshot BuildSnapshot(
            GameRelease gameRelease,
            string dataPath,
            bool includeMasterFlagsLookup = false)
        {
            return new GameLoadOrderSnapshot(pluginsByDataPath.TryGetValue(dataPath, out var pluginNames)
                ? pluginNames
                : []);
        }

        public IReadOnlyList<string> GetListedPluginNames(GameRelease gameRelease, string dataPath)
        {
            return BuildSnapshot(gameRelease, dataPath).ListedPluginNames;
        }
    }

    private sealed class BlockingGameLocationService(string olderInstall, string newerInstall) : IGameLocationService
    {
        private readonly TaskCompletionSource<bool> _firstLookupCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _firstLookupStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseFirstLookup =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> GetGameFolders(GameRelease release)
        {
            if (release == GameRelease.SkyrimSE)
            {
                _firstLookupStarted.TrySetResult(true);
                _releaseFirstLookup.Task.GetAwaiter().GetResult();
                _firstLookupCompleted.TrySetResult(true);
                return [olderInstall];
            }

            return release == GameRelease.Fallout4 ? [newerInstall] : [];
        }

        public Task WaitForFirstLookupAsync()
        {
            return _firstLookupStarted.Task;
        }

        public void ReleaseFirstLookup()
        {
            _releaseFirstLookup.TrySetResult(true);
        }

        public Task WaitForFirstLookupCompletionAsync()
        {
            return _firstLookupCompleted.Task;
        }
    }
}
