#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the base game Plugin constant table: which names each GameRelease maps to, that releases sharing a
///     game share one set, and that the sets a caller receives cannot be mutated.
/// </summary>
public sealed class BaseGamePluginsTests
{
    /// <summary>
    ///     The Skyrim set as it stands today. Pinned in full so a change to the table is a deliberate edit here too.
    /// </summary>
    private static readonly string[] ExpectedSkyrimPlugins =
    [
        "Skyrim.esm",
        "Update.esm",
        "Dawnguard.esm",
        "HearthFires.esm",
        "Dragonborn.esm",
        "ccBGSSSE001-Fish.esm",
        "ccQDRSSE001-SurvivalMode.esm"
    ];

    private static readonly string[] ExpectedFalloutPlugins =
    [
        "Fallout4.esm",
        "DLCRobot.esm",
        "DLCworkshop01.esm",
        "DLCCoast.esm",
        "DLCworkshop02.esm",
        "DLCworkshop03.esm",
        "DLCNukaWorld.esm"
    ];

    private static readonly string[] ExpectedOblivionPlugins =
    [
        "Oblivion.esm",
        "Knights.esp",
        "DLCVileLair.esp",
        "DLCThievesDen.esp",
        "DLCSpellTomes.esp",
        "DLCShiveringIsles.esp",
        "DLCOrrery.esp",
        "DLCMehrunesRazor.esp",
        "DLCHorseArmor.esp",
        "DLCFrostcrag.esp",
        "DLCBattlehornCastle.esp"
    ];

    private static readonly string[] ExpectedStarfieldPlugins =
    [
        "Starfield.esm",
        "BlueprintShips-Starfield.esm",
        "OldMars.esm",
        "Constellation.esm"
    ];

    /// <summary>
    ///     Verifies each supported GameRelease maps to exactly the set it maps to today.
    /// </summary>
    [Theory]
    [InlineData(GameRelease.SkyrimSE)]
    [InlineData(GameRelease.SkyrimVR)]
    [InlineData(GameRelease.SkyrimSEGog)]
    [InlineData(GameRelease.SkyrimLE)]
    [InlineData(GameRelease.EnderalLE)]
    [InlineData(GameRelease.EnderalSE)]
    public void ForRelease_SkyrimFamilyRelease_ReturnsCompleteSkyrimSet(GameRelease gameRelease)
    {
        AssertSetEquals(ExpectedSkyrimPlugins, BaseGamePlugins.ForRelease(gameRelease));
    }

    [Theory]
    [InlineData(GameRelease.Fallout4)]
    [InlineData(GameRelease.Fallout4VR)]
    public void ForRelease_FalloutRelease_ReturnsCompleteFalloutSet(GameRelease gameRelease)
    {
        AssertSetEquals(ExpectedFalloutPlugins, BaseGamePlugins.ForRelease(gameRelease));
    }

    [Fact]
    public void ForRelease_Oblivion_ReturnsCompleteOblivionSet()
    {
        AssertSetEquals(ExpectedOblivionPlugins, BaseGamePlugins.ForRelease(GameRelease.Oblivion));
    }

    [Fact]
    public void ForRelease_Starfield_ReturnsCompleteStarfieldSet()
    {
        AssertSetEquals(ExpectedStarfieldPlugins, BaseGamePlugins.ForRelease(GameRelease.Starfield));
    }

    /// <summary>
    ///     Verifies the Skyrim and Enderal releases keep sharing one set rather than each owning a copy.
    /// </summary>
    [Fact]
    public void ForRelease_SkyrimAndEnderalReleases_ShareOneSet()
    {
        var skyrimSe = BaseGamePlugins.ForRelease(GameRelease.SkyrimSE);

        Assert.Same(skyrimSe, BaseGamePlugins.ForRelease(GameRelease.SkyrimVR));
        Assert.Same(skyrimSe, BaseGamePlugins.ForRelease(GameRelease.SkyrimSEGog));
        Assert.Same(skyrimSe, BaseGamePlugins.ForRelease(GameRelease.SkyrimLE));
        Assert.Same(skyrimSe, BaseGamePlugins.ForRelease(GameRelease.EnderalLE));
        Assert.Same(skyrimSe, BaseGamePlugins.ForRelease(GameRelease.EnderalSE));
    }

    [Fact]
    public void ForRelease_FalloutReleases_ShareOneSet()
    {
        Assert.Same(
            BaseGamePlugins.ForRelease(GameRelease.Fallout4),
            BaseGamePlugins.ForRelease(GameRelease.Fallout4VR));
    }

    [Fact]
    public void ForRelease_UnsupportedRelease_ReturnsEmptySet()
    {
        Assert.Empty(BaseGamePlugins.ForRelease((GameRelease)999));
    }

    /// <summary>
    ///     Verifies membership ignores filesystem casing, so a Plugin on disk spelled differently is still a base Plugin.
    /// </summary>
    [Theory]
    [InlineData("skyrim.esm")]
    [InlineData("SKYRIM.ESM")]
    [InlineData("Skyrim.ESM")]
    public void ForRelease_BasePluginInAnyCasing_IsMatched(string pluginName)
    {
        // Asserted through the set's own Contains rather than a sequence search, so the match is the one the
        // Plugin List performs — decided by the set's comparer, not by an assertion-supplied one.
        Assert.True(BaseGamePlugins.ForRelease(GameRelease.SkyrimSE).Contains(pluginName));
    }

    /// <summary>
    ///     Verifies a caller cannot mutate the table: the returned set is immutable, so an attempted addition yields a
    ///     separate set and the constant table keeps returning the original contents.
    /// </summary>
    [Fact]
    public void ForRelease_CallerAttemptsMutation_LeavesConstantTableUnchanged()
    {
        var plugins = BaseGamePlugins.ForRelease(GameRelease.SkyrimSE);

        var mutated = plugins.Add("Intruder.esp");

        Assert.NotSame(plugins, mutated);
        Assert.DoesNotContain("Intruder.esp", BaseGamePlugins.ForRelease(GameRelease.SkyrimSE));
        AssertSetEquals(ExpectedSkyrimPlugins, BaseGamePlugins.ForRelease(GameRelease.SkyrimSE));
    }

    private static void AssertSetEquals(string[] expected, ImmutableHashSet<string> actual)
    {
        Assert.Equal(
            expected.OrderBy(static name => name, StringComparer.Ordinal),
            actual.OrderBy(static name => name, StringComparer.Ordinal));
    }
}
