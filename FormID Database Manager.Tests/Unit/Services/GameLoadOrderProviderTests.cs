using System;
using System.Collections.Generic;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
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

        var snapshot = sut.BuildSnapshot(GameRelease.SkyrimSE, @"C:\Games\Skyrim\Data");

        Assert.Equal(["Skyrim.esm", "Update.esm", "MyPatch.esp"], snapshot.ListedPluginNames);
    }

    /// <summary>
    ///     Verifies a game that does not separate master load orders is read without a master-flags lookup, so the
    ///     empty-lookup rule below stays scoped to the games that actually need one.
    /// </summary>
    [Fact]
    public void BuildSnapshot_MasterFlagsLookupNotNeeded_LeavesReadParametersWithoutOne()
    {
        var listings = new List<ILoadOrderListingGetter>
        {
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("Skyrim.esm"))
        };

        var sut = new GameLoadOrderProvider(
            (_, _) => listings,
            _ => false,
            _ => true,
            (_, _) => throw new InvalidOperationException("Should not read master style"));

        var snapshot = sut.BuildSnapshot(GameRelease.SkyrimSE, @"C:\Games\Skyrim\Data", true);

        Assert.Null(snapshot.MasterStyles);
        Assert.Null(snapshot.ReadParameters.MasterFlagsLookup);
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

    /// <summary>
    ///     Verifies a separated-master game whose Data directory holds none of its listed files still gets a
    ///     master-flags lookup — an empty one — rather than default read parameters.
    /// </summary>
    /// <remarks>
    ///     An empty collection of master styles is not the same fact as "this game needs no lookup", and the difference
    ///     is what a user sees: with no lookup at all Mutagen can only report that none was supplied, while an empty one
    ///     makes it name the master it could not resolve. Issue #52 turns that name into the run's failure message.
    /// </remarks>
    [Fact]
    public void BuildSnapshot_SeparatedMasterWithNoListedFileOnDisk_StillSuppliesAMasterFlagsLookup()
    {
        var listings = new List<ILoadOrderListingGetter>
        {
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("Starfield.esm"))
        };

        var sut = new GameLoadOrderProvider(
            (_, _) => listings,
            _ => true,
            _ => false,
            (_, _) => throw new InvalidOperationException("Should not read the master style of an absent file"));

        var snapshot = sut.BuildSnapshot(GameRelease.Starfield, @"C:\Games\Starfield\Data", true);

        Assert.Empty(snapshot.MasterStyles!);
        Assert.NotNull(snapshot.ReadParameters.MasterFlagsLookup);
    }
}
