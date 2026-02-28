using System;
using System.Collections.Generic;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class GameLoadOrderProviderTests
{
    [Fact]
    public void BuildSnapshot_PreservesLoadOrderListingOrder()
    {
        var listings = new List<ILoadOrderListingGetter>
        {
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("Skyrim.esm")),
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("Update.esm")),
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("MyPatch.esp"))
        };

        var sut = new GameLoadOrderProvider(
            (_, _) => listings,
            _ => false,
            _ => true,
            (_, _) => throw new InvalidOperationException("Should not read master style"));

        var snapshot = sut.BuildSnapshot(GameRelease.SkyrimSE, @"C:\Games\Skyrim\Data", false);

        Assert.Equal(["Skyrim.esm", "Update.esm", "MyPatch.esp"], snapshot.ListedPluginNames);
    }

    [Fact]
    public void BuildSnapshot_SeparatedMasterRequested_BuildsMasterFlagsLookup()
    {
        var listings = new List<ILoadOrderListingGetter>
        {
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("Starfield.esm")),
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("TestPlugin.esm"))
        };

        var sut = new GameLoadOrderProvider(
            (_, _) => listings,
            _ => true,
            _ => true,
            (modPath, _) => new KeyedMasterStyle(modPath.ModKey, MasterStyle.Full));

        var snapshot = sut.BuildSnapshot(GameRelease.Starfield, @"C:\Games\Starfield\Data", true);

        Assert.NotNull(snapshot.ReadParameters.MasterFlagsLookup);
        Assert.Equal(2, snapshot.MasterStyles?.Count);
    }

    [Fact]
    public void BuildSnapshot_SeparatedMasterSkipsMissingFiles()
    {
        var listings = new List<ILoadOrderListingGetter>
        {
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("Starfield.esm")),
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("Missing.esm"))
        };

        var sut = new GameLoadOrderProvider(
            (_, _) => listings,
            _ => true,
            path => !path.EndsWith("Missing.esm", StringComparison.OrdinalIgnoreCase),
            (modPath, _) => new KeyedMasterStyle(modPath.ModKey, MasterStyle.Full));

        var snapshot = sut.BuildSnapshot(GameRelease.Starfield, @"C:\Games\Starfield\Data", true);

        Assert.Single(snapshot.MasterStyles!);
        Assert.Equal("Starfield.esm", snapshot.MasterStyles![0].ModKey.FileName.ToString());
    }

    [Fact]
    public void GetListedPluginNames_ReturnsSnapshotNames()
    {
        var listings = new List<ILoadOrderListingGetter>
        {
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("A.esm")),
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("B.esp"))
        };

        var sut = new GameLoadOrderProvider(
            (_, _) => listings,
            _ => false,
            _ => true,
            (_, _) => throw new InvalidOperationException("Should not read master style"));

        var names = sut.GetListedPluginNames(GameRelease.SkyrimSE, @"C:\Games\Skyrim\Data");

        Assert.Equal(["A.esm", "B.esp"], names);
    }
}
