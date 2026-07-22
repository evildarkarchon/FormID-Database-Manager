#nullable enable

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

public sealed class PluginListDiscoveryTests : IDisposable
{
    private readonly string _gameDirectory = Path.Combine(
        Path.GetTempPath(),
        $"plugin-list-discovery-tests-{Guid.NewGuid():N}");

    public PluginListDiscoveryTests()
    {
        Directory.CreateDirectory(DataDirectory);
    }

    private string DataDirectory => Path.Combine(_gameDirectory, "Data");

    public void Dispose()
    {
        if (Directory.Exists(_gameDirectory))
        {
            Directory.Delete(_gameDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverAsync_LoadOrderContainsUnavailablePlugin_ReturnsAvailableNamesAndRawProgressInOrder()
    {
        File.WriteAllBytes(Path.Combine(DataDirectory, "AvailableA.esp"), [1]);
        File.WriteAllBytes(Path.Combine(DataDirectory, "AvailableB.esp"), [2]);
        var loadOrderProvider = new Mock<IGameLoadOrderProvider>();
        loadOrderProvider.Setup(provider => provider.BuildSnapshot(
                GameRelease.SkyrimSE,
                Path.GetFullPath(DataDirectory),
                false))
            .Returns(GameLoadOrderSnapshotFactory.CreateSnapshot(
                "AvailableA.esp",
                "Missing.esp",
                "AvailableB.esp"));
        var progressReports = new List<PluginListDiscoveryProgress>();
        var source = PluginListSource.Create(GameRelease.SkyrimSE, _gameDirectory);
        var sut = new PluginListDiscovery(loadOrderProvider.Object);

        var result = await sut.DiscoverAsync(
            source,
            new SynchronousProgress<PluginListDiscoveryProgress>(progressReports.Add),
            TestContext.Current.CancellationToken);

        var completed = Assert.IsType<PluginListDiscoveryCompleted>(result);
        Assert.Equal(["AvailableA.esp", "AvailableB.esp"], completed.PluginNames.ToArray());
        Assert.Equal(
            [new PluginListDiscoveryProgress(0, 3), new PluginListDiscoveryProgress(3, 3)],
            progressReports);
        loadOrderProvider.VerifyAll();
    }

    [Fact]
    public async Task DiscoverAsync_LoadOrderReadHasExpectedLocalFailure_ReturnsFailedDiscoveryFact()
    {
        var failure = new IOException("The local load order could not be read.");
        var loadOrderProvider = new Mock<IGameLoadOrderProvider>();
        loadOrderProvider.Setup(provider => provider.BuildSnapshot(
                GameRelease.SkyrimSE,
                Path.GetFullPath(DataDirectory),
                false))
            .Throws(failure);
        var sut = new PluginListDiscovery(loadOrderProvider.Object);

        var result = await sut.DiscoverAsync(
            PluginListSource.Create(GameRelease.SkyrimSE, _gameDirectory),
            cancellationToken: TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PluginListDiscoveryFailed>(result);
        Assert.Equal(failure.Message, failed.ErrorMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoverAsync_LoadOrderReadHasProgrammingOrFatalFailure_PropagatesFailure(bool fatal)
    {
        Exception failure = fatal
            ? new OutOfMemoryException("Fatal discovery failure.")
            : new InvalidOperationException("Invalid discovery adapter state.");
        var loadOrderProvider = new Mock<IGameLoadOrderProvider>();
        loadOrderProvider.Setup(provider => provider.BuildSnapshot(
                GameRelease.SkyrimSE,
                Path.GetFullPath(DataDirectory),
                false))
            .Throws(failure);
        var sut = new PluginListDiscovery(loadOrderProvider.Object);

        var thrown = await Assert.ThrowsAsync(
            failure.GetType(),
            () => sut.DiscoverAsync(
                PluginListSource.Create(GameRelease.SkyrimSE, _gameDirectory),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task DiscoverAsync_ProgressObserverThrowsIOException_PropagatesObserverFailure()
    {
        var observerFailure = new IOException("The progress observer failed.");
        var loadOrderProvider = new Mock<IGameLoadOrderProvider>();
        loadOrderProvider.Setup(provider => provider.BuildSnapshot(
                GameRelease.SkyrimSE,
                Path.GetFullPath(DataDirectory),
                false))
            .Returns(GameLoadOrderSnapshotFactory.CreateSnapshot());
        var sut = new PluginListDiscovery(loadOrderProvider.Object);

        var thrown = await Assert.ThrowsAsync<IOException>(() => sut.DiscoverAsync(
            PluginListSource.Create(GameRelease.SkyrimSE, _gameDirectory),
            new ThrowingProgress<PluginListDiscoveryProgress>(observerFailure),
            TestContext.Current.CancellationToken));

        Assert.Same(observerFailure, thrown);
    }

    private sealed class ThrowingProgress<T>(Exception failure) : IProgress<T>
    {
        public void Report(T value)
        {
            throw failure;
        }
    }
}
