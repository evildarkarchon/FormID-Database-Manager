using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using FormID_Database_Manager.TestUtilities.Mocks;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class PluginListRefreshTests : IDisposable
{
    private readonly Mock<GameDetectionService> _gameDetectionService = new();
    private readonly Mock<IGameLoadOrderProvider> _loadOrderProvider = new();
    private readonly string _testDirectory;

    public PluginListRefreshTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"pluginlist-refresh-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DataPath);
        _gameDetectionService.Setup(x => x.GetBaseGamePlugins(It.IsAny<GameRelease>()))
            .Returns([]);
    }

    private string DataPath => Path.Combine(_testDirectory, "Data");

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshAsync_NormalMode_FiltersBasePluginsAndDeduplicates()
    {
        CreatePluginFiles("Skyrim.esm", "Update.esm", "UserA.esp", "UserB.esp");
        _loadOrderProvider.Setup(x => x.BuildSnapshot(GameRelease.SkyrimSE, DataPath, false))
            .Returns(GameLoadOrderSnapshotFactory.CreateSnapshot("Skyrim.esm", "Update.esm", "UserA.esp", "UserA.esp", "UserB.esp"));
        _gameDetectionService.Setup(x => x.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns(["Skyrim.esm", "Update.esm"]);

        var result = await CreateSut().RefreshAsync(
            new PluginListRefreshRequest(_testDirectory, GameRelease.SkyrimSE, AdvancedMode.Off),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PluginListRefreshStatus.Completed, result.Status);
        Assert.Equal(2, result.LoadedCount);
        Assert.Equal(["UserA.esp", "UserB.esp"], result.Plugins.Select(plugin => plugin.Name).ToArray());
    }

    [Fact]
    public async Task RefreshAsync_AdvancedMode_IncludesBasePlugins()
    {
        CreatePluginFiles("Skyrim.esm", "Update.esm", "UserA.esp");
        _loadOrderProvider.Setup(x => x.BuildSnapshot(GameRelease.SkyrimSE, DataPath, false))
            .Returns(GameLoadOrderSnapshotFactory.CreateSnapshot("Skyrim.esm", "Update.esm", "UserA.esp"));

        var result = await CreateSut().RefreshAsync(
            new PluginListRefreshRequest(_testDirectory, GameRelease.SkyrimSE, AdvancedMode.On),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PluginListRefreshStatus.Completed, result.Status);
        Assert.Equal(["Skyrim.esm", "Update.esm", "UserA.esp"], result.Plugins.Select(plugin => plugin.Name).ToArray());
    }

    [Fact]
    public async Task RefreshAsync_MissingPluginFile_OmitsPluginWithoutFailing()
    {
        CreatePluginFiles("UserA.esp");
        _loadOrderProvider.Setup(x => x.BuildSnapshot(GameRelease.SkyrimSE, DataPath, false))
            .Returns(GameLoadOrderSnapshotFactory.CreateSnapshot("UserA.esp", "Missing.esp"));

        var result = await CreateSut().RefreshAsync(
            new PluginListRefreshRequest(_testDirectory, GameRelease.SkyrimSE, AdvancedMode.On),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PluginListRefreshStatus.Completed, result.Status);
        Assert.Equal(["UserA.esp"], result.Plugins.Select(plugin => plugin.Name).ToArray());
    }

    [Fact]
    public async Task RefreshAsync_OverlappingRefreshes_ReturnsStaleForOlderRefresh()
    {
        CreatePluginFiles("Older.esp", "Newer.esp");
        var olderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOlderToContinue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _loadOrderProvider.SetupSequence(x => x.BuildSnapshot(GameRelease.SkyrimSE, DataPath, false))
            .Returns(() =>
            {
                olderStarted.SetResult();
                allowOlderToContinue.Task.GetAwaiter().GetResult();
                return GameLoadOrderSnapshotFactory.CreateSnapshot("Older.esp");
            })
            .Returns(GameLoadOrderSnapshotFactory.CreateSnapshot("Newer.esp"));
        var sut = CreateSut();
        var request = new PluginListRefreshRequest(_testDirectory, GameRelease.SkyrimSE, AdvancedMode.On);

        var olderTask = sut.RefreshAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        await olderStarted.Task;
        var newerTask = sut.RefreshAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        allowOlderToContinue.SetResult();
        var results = await Task.WhenAll(olderTask, newerTask);

        Assert.Equal(PluginListRefreshStatus.Stale, results[0].Status);
        Assert.Equal(PluginListRefreshStatus.Completed, results[1].Status);
        Assert.Equal(["Newer.esp"], results[1].Plugins.Select(plugin => plugin.Name).ToArray());
    }

    [Fact]
    public async Task RefreshAsync_ReportsProgressFactsWithoutUiText()
    {
        var pluginNames = Enumerable.Range(1, 12).Select(i => $"Plugin{i}.esp").ToArray();
        CreatePluginFiles(pluginNames);
        _loadOrderProvider.Setup(x => x.BuildSnapshot(GameRelease.SkyrimSE, DataPath, false))
            .Returns(GameLoadOrderSnapshotFactory.CreateSnapshot(pluginNames));
        var reports = new List<PluginListRefreshProgress>();

        await CreateSut().RefreshAsync(
            new PluginListRefreshRequest(_testDirectory, GameRelease.SkyrimSE, AdvancedMode.On),
            new SynchronousProgress<PluginListRefreshProgress>(reports.Add),
            TestContext.Current.CancellationToken);

        Assert.Contains(new PluginListRefreshProgress(0, 12), reports);
        Assert.Contains(new PluginListRefreshProgress(10, 12), reports);
        Assert.Contains(new PluginListRefreshProgress(12, 12), reports);
    }

    private PluginListRefresh CreateSut()
    {
        return new PluginListRefresh(_gameDetectionService.Object, _loadOrderProvider.Object);
    }

    private void CreatePluginFiles(params string[] pluginNames)
    {
        foreach (var pluginName in pluginNames)
        {
            File.WriteAllBytes(Path.Combine(DataPath, pluginName), [1, 2, 3, 4]);
        }
    }
}
