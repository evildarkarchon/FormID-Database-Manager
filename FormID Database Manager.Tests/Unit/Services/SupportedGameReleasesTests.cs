#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the Supported GameRelease constant table: the table name, Base Game Plugin set, and overlay
///     construction each row carries, the always-throws contract for a GameRelease that is not in the table, and the
///     drift check against every GameRelease Mutagen defines.
/// </summary>
/// <remarks>
///     This file absorbs the tests of the two modules the table replaced, so per-release assertions live in one place
///     rather than being split across modules that no longer exist.
/// </remarks>
public sealed class SupportedGameReleasesTests
{
    /// <summary>
    ///     The dropdown order as it stands today, pinned so that reordering the table is a deliberate test edit rather
    ///     than a silent change to what users see.
    /// </summary>
    private static readonly GameRelease[] ExpectedDisplayOrder =
    [
        GameRelease.Fallout4,
        GameRelease.SkyrimSE,
        GameRelease.SkyrimLE,
        GameRelease.SkyrimVR,
        GameRelease.SkyrimSEGog,
        GameRelease.EnderalSE,
        GameRelease.EnderalLE,
        GameRelease.Fallout4VR,
        GameRelease.Oblivion,
        GameRelease.Starfield
    ];

    /// <summary>
    ///     The GameReleases Mutagen defines that this application deliberately does not support. Pinned so that a
    ///     Mutagen upgrade adding a release forces a decision here instead of shipping as a silent gap.
    /// </summary>
    private static readonly GameRelease[] DeliberatelyUnsupportedReleases =
    [
        // Oblivion Remastered. No table name, Base Game Plugin set, or overlay type has been chosen for it.
        GameRelease.OblivionRE
    ];

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

    [Theory]
    [InlineData(GameRelease.Oblivion, "Oblivion")]
    [InlineData(GameRelease.SkyrimLE, "SkyrimLE")]
    [InlineData(GameRelease.SkyrimSE, "SkyrimSE")]
    [InlineData(GameRelease.SkyrimSEGog, "SkyrimSEGog")]
    [InlineData(GameRelease.SkyrimVR, "SkyrimVR")]
    [InlineData(GameRelease.EnderalLE, "EnderalLE")]
    [InlineData(GameRelease.EnderalSE, "EnderalSE")]
    [InlineData(GameRelease.Fallout4, "Fallout4")]
    [InlineData(GameRelease.Fallout4VR, "Fallout4VR")]
    [InlineData(GameRelease.Starfield, "Starfield")]
    public void ForRelease_SupportedRelease_ReturnsExpectedTableName(GameRelease release, string expected)
    {
        Assert.Equal(expected, SupportedGameReleases.ForRelease(release).TableName);
    }

    /// <summary>
    ///     Verifies each Skyrim-family release maps to exactly the Base Game Plugin set it maps to today.
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
        AssertSetEquals(ExpectedSkyrimPlugins, SupportedGameReleases.ForRelease(gameRelease).BasePlugins);
    }

    [Theory]
    [InlineData(GameRelease.Fallout4)]
    [InlineData(GameRelease.Fallout4VR)]
    public void ForRelease_FalloutRelease_ReturnsCompleteFalloutSet(GameRelease gameRelease)
    {
        AssertSetEquals(ExpectedFalloutPlugins, SupportedGameReleases.ForRelease(gameRelease).BasePlugins);
    }

    [Fact]
    public void ForRelease_Oblivion_ReturnsCompleteOblivionSet()
    {
        AssertSetEquals(ExpectedOblivionPlugins, SupportedGameReleases.ForRelease(GameRelease.Oblivion).BasePlugins);
    }

    [Fact]
    public void ForRelease_Starfield_ReturnsCompleteStarfieldSet()
    {
        AssertSetEquals(ExpectedStarfieldPlugins, SupportedGameReleases.ForRelease(GameRelease.Starfield).BasePlugins);
    }

    /// <summary>
    ///     Verifies the Skyrim and Enderal releases keep sharing one set rather than each owning a copy.
    /// </summary>
    [Fact]
    public void ForRelease_SkyrimAndEnderalReleases_ShareOneBasePluginSet()
    {
        var skyrimSe = SupportedGameReleases.ForRelease(GameRelease.SkyrimSE).BasePlugins;

        Assert.Same(skyrimSe, SupportedGameReleases.ForRelease(GameRelease.SkyrimVR).BasePlugins);
        Assert.Same(skyrimSe, SupportedGameReleases.ForRelease(GameRelease.SkyrimSEGog).BasePlugins);
        Assert.Same(skyrimSe, SupportedGameReleases.ForRelease(GameRelease.SkyrimLE).BasePlugins);
        Assert.Same(skyrimSe, SupportedGameReleases.ForRelease(GameRelease.EnderalLE).BasePlugins);
        Assert.Same(skyrimSe, SupportedGameReleases.ForRelease(GameRelease.EnderalSE).BasePlugins);
    }

    [Fact]
    public void ForRelease_FalloutReleases_ShareOneBasePluginSet()
    {
        Assert.Same(
            SupportedGameReleases.ForRelease(GameRelease.Fallout4).BasePlugins,
            SupportedGameReleases.ForRelease(GameRelease.Fallout4VR).BasePlugins);
    }

    /// <summary>
    ///     Verifies Base Game Plugin membership ignores filesystem casing, so a Plugin on disk spelled differently is
    ///     still a Base Game Plugin.
    /// </summary>
    [Theory]
    [InlineData("skyrim.esm")]
    [InlineData("SKYRIM.ESM")]
    [InlineData("Skyrim.ESM")]
    public void ForRelease_BasePluginInAnyCasing_IsMatched(string pluginName)
    {
        // Asserted through the set's own Contains rather than a sequence search, so the match is the one the
        // Plugin List performs — decided by the set's comparer, not by an assertion-supplied one.
        Assert.True(SupportedGameReleases.ForRelease(GameRelease.SkyrimSE).BasePlugins.Contains(pluginName));
    }

    /// <summary>
    ///     Verifies a caller cannot mutate the table: the returned set is immutable, so an attempted addition yields a
    ///     separate set and the constant table keeps returning the original contents.
    /// </summary>
    [Fact]
    public void ForRelease_CallerAttemptsMutation_LeavesConstantTableUnchanged()
    {
        var plugins = SupportedGameReleases.ForRelease(GameRelease.SkyrimSE).BasePlugins;

        var mutated = plugins.Add("Intruder.esp");

        Assert.NotSame(plugins, mutated);
        var reread = SupportedGameReleases.ForRelease(GameRelease.SkyrimSE).BasePlugins;
        Assert.DoesNotContain("Intruder.esp", reread);
        AssertSetEquals(ExpectedSkyrimPlugins, reread);
    }

    /// <summary>
    ///     Verifies the always-throws contract for an enum value Mutagen does not define, so no caller can proceed on
    ///     an empty or default answer.
    /// </summary>
    [Fact]
    public void ForRelease_UndefinedEnumValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SupportedGameReleases.ForRelease((GameRelease)999));
        Assert.False(SupportedGameReleases.IsSupported((GameRelease)999));
    }

    /// <summary>
    ///     Verifies a Mutagen-defined but deliberately unsupported release follows the same one rule as an undefined
    ///     value — rejected by both members — and that the failure names the release.
    /// </summary>
    [Fact]
    public void ForRelease_DeliberatelyUnsupportedRelease_ThrowsAndIsNotSupported()
    {
        foreach (var release in DeliberatelyUnsupportedReleases)
        {
            Assert.False(SupportedGameReleases.IsSupported(release));
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => SupportedGameReleases.ForRelease(release));
            Assert.Contains(release.ToString(), ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void IsSupported_ReleaseInTheTable_ReturnsTrue()
    {
        Assert.All(SupportedGameReleases.All, row => Assert.True(SupportedGameReleases.IsSupported(row.Release)));
    }

    /// <summary>
    ///     The drift check. Every GameRelease Mutagen defines must be either a row in the table or a recorded
    ///     deliberate exclusion, so a Mutagen upgrade that adds a release fails the suite by name rather than
    ///     shipping as a silent gap.
    /// </summary>
    [Fact]
    public void All_ComparedWithEveryGameReleaseMutagenDefines_AccountsForEachOne()
    {
        var unaccounted = Enum.GetValues<GameRelease>()
            .Where(release => !SupportedGameReleases.IsSupported(release))
            .Where(release => !DeliberatelyUnsupportedReleases.Contains(release))
            .ToArray();

        Assert.True(
            unaccounted.Length == 0,
            $"Mutagen defines GameRelease values this application neither supports nor deliberately excludes: " +
            $"{string.Join(", ", unaccounted)}. Add a row to {nameof(SupportedGameReleases)}, or add the release to " +
            $"{nameof(DeliberatelyUnsupportedReleases)} with the reason it is excluded.");
    }

    /// <summary>
    ///     Verifies no row is half-populated, which the compiler cannot catch for a blank string or a null column.
    /// </summary>
    [Fact]
    public void All_EveryRow_IsFullyPopulated()
    {
        Assert.NotEmpty(SupportedGameReleases.All);
        Assert.All(SupportedGameReleases.All, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.TableName));
            Assert.NotNull(row.BasePlugins);
            Assert.NotNull(row.CreateOverlay);
        });
    }

    /// <summary>
    ///     Verifies a GameRelease appears at most once, so a lookup cannot depend on which duplicate wins.
    /// </summary>
    [Fact]
    public void All_EveryRow_HasADistinctRelease()
    {
        Assert.Equal(
            SupportedGameReleases.All.Count,
            SupportedGameReleases.All.Select(row => row.Release).Distinct().Count());
    }

    [Fact]
    public void All_DisplayOrder_MatchesTheDocumentedOrder()
    {
        Assert.Equal(ExpectedDisplayOrder, SupportedGameReleases.All.Select(row => row.Release));
    }

    private static void AssertSetEquals(string[] expected, ImmutableHashSet<string> actual)
    {
        Assert.Equal(
            expected.OrderBy(static name => name, StringComparer.Ordinal),
            actual.OrderBy(static name => name, StringComparer.Ordinal));
    }
}
