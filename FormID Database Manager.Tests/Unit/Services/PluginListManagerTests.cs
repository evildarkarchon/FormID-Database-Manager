using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class PluginListManagerTests : IDisposable
{
    private readonly Mock<IThreadDispatcher> _dispatcher;
    private readonly Mock<GameDetectionService> _gameDetectionService;
    private readonly Mock<IGameLoadOrderProvider> _loadOrderProvider;
    private readonly ObservableCollection<PluginListItem> _plugins;
    private readonly string _testDirectory;
    private readonly MainWindowViewModel _viewModel;

    public PluginListManagerTests()
    {
        _dispatcher = new Mock<IThreadDispatcher>();
        _dispatcher.Setup(d => d.InvokeAsync(It.IsAny<Action>()))
            .Callback<Action>(action => action())
            .Returns(Task.CompletedTask);
        _dispatcher.Setup(d => d.Post(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        _dispatcher.Setup(d => d.CheckAccess()).Returns(true);

        _gameDetectionService = FormID_Database_Manager.TestUtilities.Mocks.MockFactory.CreateGameDetectionServiceMock();
        _loadOrderProvider = new Mock<IGameLoadOrderProvider>();
        _viewModel = new MainWindowViewModel(_dispatcher.Object);
        _plugins = [];

        _testDirectory = Path.Combine(Path.GetTempPath(), $"pluginlist-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(Path.Combine(_testDirectory, "Data"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private PluginListManager CreateSut()
    {
        return new PluginListManager(
            _gameDetectionService.Object,
            _viewModel,
            _dispatcher.Object,
            _loadOrderProvider.Object);
    }

    private static void CreatePluginFiles(string dataPath, params string[] pluginNames)
    {
        foreach (var pluginName in pluginNames)
        {
            File.WriteAllBytes(Path.Combine(dataPath, pluginName), [1, 2, 3, 4]);
        }
    }

    [Fact]
    public async Task RefreshPluginList_NormalMode_FiltersBaseAndDeduplicates()
    {
        var dataPath = Path.Combine(_testDirectory, "Data");
        CreatePluginFiles(dataPath, "Skyrim.esm", "Update.esm", "UserA.esp", "UserB.esp");

        _loadOrderProvider.Setup(x => x.GetListedPluginNames(GameRelease.SkyrimSE, dataPath))
            .Returns(["Skyrim.esm", "Update.esm", "UserA.esp", "UserA.esp", "UserB.esp"]);
        _gameDetectionService.Setup(x => x.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns(["Skyrim.esm", "Update.esm"]);

        var sut = CreateSut();

        await sut.RefreshPluginList(_testDirectory, GameRelease.SkyrimSE, _plugins, showAdvanced: false);

        Assert.Equal(2, _plugins.Count);
        Assert.Equal("UserA.esp", _plugins[0].Name);
        Assert.Equal("UserB.esp", _plugins[1].Name);
        Assert.Contains(_viewModel.InformationMessages, x => x.Contains("Loaded 2 non-base game plugins"));
        Assert.Empty(_viewModel.ErrorMessages);
    }

    [Fact]
    public async Task RefreshPluginList_AdvancedMode_IncludesBasePlugins()
    {
        var dataPath = Path.Combine(_testDirectory, "Data");
        CreatePluginFiles(dataPath, "Skyrim.esm", "Update.esm", "UserA.esp");

        _loadOrderProvider.Setup(x => x.GetListedPluginNames(GameRelease.SkyrimSE, dataPath))
            .Returns(["Skyrim.esm", "Update.esm", "UserA.esp"]);

        var sut = CreateSut();

        await sut.RefreshPluginList(_testDirectory, GameRelease.SkyrimSE, _plugins, showAdvanced: true);

        Assert.Equal(3, _plugins.Count);
        Assert.Equal(["Skyrim.esm", "Update.esm", "UserA.esp"], _plugins.Select(x => x.Name).ToArray());
        Assert.Contains(_viewModel.InformationMessages, x => x.Contains("Loaded 3 plugins"));
    }

    [Fact]
    public async Task RefreshPluginList_SkipsMissingFiles_FromLoadOrder()
    {
        var dataPath = Path.Combine(_testDirectory, "Data");
        CreatePluginFiles(dataPath, "UserA.esp");

        _loadOrderProvider.Setup(x => x.GetListedPluginNames(GameRelease.SkyrimSE, dataPath))
            .Returns(["UserA.esp", "Missing.esp"]);

        var sut = CreateSut();

        await sut.RefreshPluginList(_testDirectory, GameRelease.SkyrimSE, _plugins, showAdvanced: true);

        Assert.Single(_plugins);
        Assert.Equal("UserA.esp", _plugins[0].Name);
    }

    [Fact]
    public async Task RefreshPluginList_NormalMode_FiltersBasePlugins_CaseInsensitive()
    {
        var dataPath = Path.Combine(_testDirectory, "Data");
        CreatePluginFiles(dataPath, "skyrim.esm", "update.esm", "UserA.esp");

        _loadOrderProvider.Setup(x => x.GetListedPluginNames(GameRelease.SkyrimSE, dataPath))
            .Returns(["skyrim.esm", "update.esm", "UserA.esp"]);
        _gameDetectionService.Setup(x => x.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns(["Skyrim.esm", "Update.esm"]);

        var sut = CreateSut();

        await sut.RefreshPluginList(_testDirectory, GameRelease.SkyrimSE, _plugins, showAdvanced: false);

        Assert.Single(_plugins);
        Assert.Equal("UserA.esp", _plugins[0].Name);
        Assert.Contains(_viewModel.InformationMessages, x => x.Contains("Loaded 1 non-base game plugins"));
    }

    [Fact]
    public async Task RefreshPluginList_WhenProviderThrows_ClearsCollectionsAndAddsError()
    {
        _plugins.Add(new PluginListItem { Name = "OldPlugin.esp" });
        _viewModel.FilteredPlugins.Add(new PluginListItem { Name = "OldPlugin.esp" });

        var dataPath = Path.Combine(_testDirectory, "Data");
        _loadOrderProvider.Setup(x => x.GetListedPluginNames(GameRelease.SkyrimSE, dataPath))
            .Throws(new InvalidOperationException("load order failure"));

        var sut = CreateSut();

        await sut.RefreshPluginList(_testDirectory, GameRelease.SkyrimSE, _plugins, showAdvanced: false);

        Assert.Empty(_plugins);
        Assert.Empty(_viewModel.FilteredPlugins);
        Assert.Contains(_viewModel.ErrorMessages, x => x.Contains("Failed to load plugins"));
    }

    [Fact]
    public async Task RefreshPluginList_PostsScanningUpdates_DuringProcessing()
    {
        var postCount = 0;
        _dispatcher.Setup(d => d.Post(It.IsAny<Action>()))
            .Callback<Action>(action =>
            {
                postCount++;
                action();
            });

        var dataPath = Path.Combine(_testDirectory, "Data");
        var pluginNames = Enumerable.Range(1, 12).Select(i => $"Plugin{i}.esp").ToArray();
        CreatePluginFiles(dataPath, pluginNames);

        _loadOrderProvider.Setup(x => x.GetListedPluginNames(GameRelease.SkyrimSE, dataPath))
            .Returns(pluginNames);

        var sut = CreateSut();

        await sut.RefreshPluginList(_testDirectory, GameRelease.SkyrimSE, _plugins, showAdvanced: true);

        Assert.False(_viewModel.IsScanning);
        Assert.Equal(12, _plugins.Count);
        Assert.True(postCount >= 3);
    }

    [Fact]
    public async Task RefreshPluginList_IgnoresStaleRefreshResult_WhenNewerRefreshStarts()
    {
        var dataPath = Path.Combine(_testDirectory, "Data");
        CreatePluginFiles(dataPath, "OlderA.esp", "OlderB.esp", "Newer.esp");

        var olderStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOlderToContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _loadOrderProvider.SetupSequence(x => x.GetListedPluginNames(GameRelease.SkyrimSE, dataPath))
            .Returns(() =>
            {
                olderStarted.TrySetResult(true);
                allowOlderToContinue.Task.GetAwaiter().GetResult();
                return ["OlderA.esp", "OlderB.esp"];
            })
            .Returns(["Newer.esp"]);

        var sut = CreateSut();

        var olderTask = sut.RefreshPluginList(_testDirectory, GameRelease.SkyrimSE, _plugins, showAdvanced: true);
        await olderStarted.Task;

        var newerTask = sut.RefreshPluginList(_testDirectory, GameRelease.SkyrimSE, _plugins, showAdvanced: true);

        allowOlderToContinue.TrySetResult(true);

        await Task.WhenAll(olderTask, newerTask);

        Assert.Single(_plugins);
        Assert.Equal("Newer.esp", _plugins[0].Name);
    }

    [Fact]
    public void SelectAll_MarksAllPlugins_AsSelected()
    {
        var plugins = new ObservableCollection<PluginListItem>
        {
            new() { Name = "A.esp", IsSelected = false },
            new() { Name = "B.esp", IsSelected = false }
        };

        CreateSut().SelectAll(plugins);

        Assert.All(plugins, x => Assert.True(x.IsSelected));
    }

    [Fact]
    public void SelectNone_ClearsAllSelections()
    {
        var plugins = new ObservableCollection<PluginListItem>
        {
            new() { Name = "A.esp", IsSelected = true },
            new() { Name = "B.esp", IsSelected = true }
        };

        CreateSut().SelectNone(plugins);

        Assert.All(plugins, x => Assert.False(x.IsSelected));
    }
}
