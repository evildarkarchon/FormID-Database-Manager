#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class PluginListTests
{
    [Fact]
    public void TypeShape_UsesOneInternalConcreteSealedModuleWithoutExternalMockInterface()
    {
        var pluginListType = typeof(PluginList);

        Assert.True(pluginListType.IsNotPublic);
        Assert.True(pluginListType.IsSealed);
        Assert.False(pluginListType.IsAbstract);
        Assert.Contains(typeof(IDisposable), pluginListType.GetInterfaces());
        Assert.True(typeof(IPluginListDiscovery).IsNotPublic);
        Assert.DoesNotContain(
            pluginListType.Assembly.GetTypes(),
            type => type.Name == "IPluginList");
    }

    [Fact]
    public async Task RefreshAsync_InitialDiscovery_PublishesImmutableConfirmedPluginListInPluginListOrder()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns(["Skyrim.esm"]);
        var discovery = new DeterministicPluginListDiscovery(
            "skyrim.ESM",
            "UserA.esp",
            "usera.ESP",
            "UserB.esp");
        using var sut = new PluginList(gameDetectionService.Object, discovery);
        var changedCount = 0;
        EventArgs? lastEventArgs = null;
        var publishedActivities = new List<PluginListActivity>();
        sut.Changed += (_, eventArgs) =>
        {
            changedCount++;
            lastEventArgs = eventArgs;
            publishedActivities.Add(sut.Current.Activity);
        };

        Assert.Equal(0, sut.Current.StateRevision);
        Assert.Null(sut.Current.Confirmed);
        Assert.IsType<PluginListNoSourceActivity>(sut.Current.Activity);

        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        var current = sut.Current;
        var confirmed = Assert.IsType<ConfirmedPluginList>(current.Confirmed);
        Assert.Equal(1, confirmed.MembershipVersion);
        Assert.Equal(GameRelease.SkyrimSE, confirmed.Source.GameRelease);
        Assert.Equal(discovery.DataDirectory, confirmed.Source.DataDirectory, ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(AdvancedMode.Off, confirmed.AdvancedMode);
        Assert.Equal(["UserA.esp", "UserB.esp"], confirmed.Entries.Select(entry => entry.Name).ToArray());
        Assert.Empty(confirmed.SelectedPluginNames);
        Assert.IsType<PluginListReadyActivity>(current.Activity);
        Assert.True(current.StateRevision >= 2);
        Assert.True(changedCount >= 2);
        Assert.Same(EventArgs.Empty, lastEventArgs);
        Assert.Contains(publishedActivities, activity => activity is PluginListRefreshingActivity);
        Assert.Contains(publishedActivities, activity => activity is PluginListReadyActivity);
    }

    [Fact]
    public async Task RefreshAsync_AdvancedMode_IncludesBasePlugins()
    {
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns(["Skyrim.esm"]);
        var discovery = new DeterministicPluginListDiscovery("skyrim.ESM", "User.esp");
        using var sut = new PluginList(gameDetectionService.Object, discovery);

        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.On,
            TestContext.Current.CancellationToken);

        var confirmed = Assert.IsType<ConfirmedPluginList>(sut.Current.Confirmed);
        Assert.Equal(["skyrim.ESM", "User.esp"], confirmed.Entries.Select(entry => entry.Name).ToArray());
    }

    [Fact]
    public async Task RefreshAsync_ExpectedDiscoveryFailure_PublishesUiNeutralFailureWithoutConfirmedList()
    {
        var failure = PluginListDiscoveryResult.Failed("The local Plugin List could not be read.");
        var discovery = new FixedPluginListDiscovery(failure);
        using var sut = new PluginList(new Mock<GameDetectionService>().Object, discovery);

        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);

        Assert.Null(sut.Current.Confirmed);
        var failed = Assert.IsType<PluginListFailedActivity>(sut.Current.Activity);
        Assert.Equal("The local Plugin List could not be read.", failed.ErrorMessage);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, discovery.GameDirectory), failed.Source);
    }

    [Fact]
    public async Task Invalidate_ConfirmedPluginList_PublishesNoSourceState()
    {
        var discovery = new DeterministicPluginListDiscovery("User.esp");
        var gameDetectionService = new Mock<GameDetectionService>();
        gameDetectionService.Setup(service => service.GetBaseGamePlugins(GameRelease.SkyrimSE))
            .Returns([]);
        using var sut = new PluginList(gameDetectionService.Object, discovery);
        await sut.RefreshAsync(
            GameRelease.SkyrimSE,
            discovery.GameDirectory,
            AdvancedMode.Off,
            TestContext.Current.CancellationToken);
        var confirmedRevision = sut.Current.StateRevision;

        sut.Invalidate();

        Assert.Null(sut.Current.Confirmed);
        Assert.IsType<PluginListNoSourceActivity>(sut.Current.Activity);
        Assert.True(sut.Current.StateRevision > confirmedRevision);
    }

    private sealed class DeterministicPluginListDiscovery(params string[] pluginNames) : IPluginListDiscovery
    {
        public string GameDirectory { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"deterministic-plugin-list-{Guid.NewGuid():N}");

        public string DataDirectory => System.IO.Path.Combine(GameDirectory, "Data");

        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PluginListDiscoveryResult.Completed(pluginNames));
        }
    }

    private sealed class FixedPluginListDiscovery(PluginListDiscoveryResult result) : IPluginListDiscovery
    {
        public string GameDirectory { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fixed-plugin-list-{Guid.NewGuid():N}");

        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }
}
