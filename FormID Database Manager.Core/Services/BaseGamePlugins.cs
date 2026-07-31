using System.Collections.Immutable;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     The constant table of base game Plugins — the master files and official add-ons that ship with a game, which
///     the Plugin List hides unless Advanced Mode is on.
/// </summary>
/// <remarks>
///     This is data, not a service: nothing about it varies at runtime, so it is a static table rather than an
///     injected dependency, and Plugin List reads it directly instead of taking a whole module to call one method
///     (ADR-0002). The sets are exposed as <see cref="ImmutableHashSet{T}" /> so no caller can mutate another
///     module's state and none has to defensively copy.
/// </remarks>
internal static class BaseGamePlugins
{
    /// <summary>
    ///     Shared by every Skyrim release. The Enderal releases are total conversions built on Skyrim's masters, so
    ///     they hide the same base Plugins rather than owning a separate set.
    /// </summary>
    private static readonly ImmutableHashSet<string> SkyrimPlugins = Create(
        "Skyrim.esm",
        "Update.esm",
        "Dawnguard.esm",
        "HearthFires.esm",
        "Dragonborn.esm",
        "ccBGSSSE001-Fish.esm",
        "ccQDRSSE001-SurvivalMode.esm");

    /// <summary>
    ///     Shared by the flat and VR Fallout 4 releases, which ship the same masters and add-ons.
    /// </summary>
    private static readonly ImmutableHashSet<string> FalloutPlugins = Create(
        "Fallout4.esm",
        "DLCRobot.esm",
        "DLCworkshop01.esm",
        "DLCCoast.esm",
        "DLCworkshop02.esm",
        "DLCworkshop03.esm",
        "DLCNukaWorld.esm");

    private static readonly ImmutableHashSet<string> OblivionPlugins = Create(
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
        "DLCBattlehornCastle.esp");

    private static readonly ImmutableHashSet<string> StarfieldPlugins = Create(
        "Starfield.esm",
        "BlueprintShips-Starfield.esm",
        "OldMars.esm",
        "Constellation.esm");

    /// <summary>
    ///     Returned for any GameRelease with no base Plugin set, so callers never have to null-check.
    /// </summary>
    private static readonly ImmutableHashSet<string> NoPlugins = Create();

    /// <summary>
    ///     Gets the base game Plugins that ship with the specified GameRelease.
    /// </summary>
    /// <param name="gameRelease">The GameRelease whose base Plugins are wanted.</param>
    /// <returns>
    ///     An immutable, case-insensitive set of Plugin filenames; empty for a GameRelease with no known base Plugins.
    ///     Releases of the same game return the same set instance.
    /// </returns>
    internal static ImmutableHashSet<string> ForRelease(GameRelease gameRelease)
    {
        return gameRelease switch
        {
            GameRelease.SkyrimSE => SkyrimPlugins,
            GameRelease.SkyrimVR => SkyrimPlugins,
            GameRelease.SkyrimSEGog => SkyrimPlugins,
            GameRelease.SkyrimLE => SkyrimPlugins,
            GameRelease.EnderalLE => SkyrimPlugins,
            GameRelease.EnderalSE => SkyrimPlugins,
            GameRelease.Oblivion => OblivionPlugins,
            GameRelease.Fallout4 => FalloutPlugins,
            GameRelease.Fallout4VR => FalloutPlugins,
            GameRelease.Starfield => StarfieldPlugins,
            _ => NoPlugins
        };
    }

    /// <summary>
    ///     Builds one table entry with the case-insensitive comparer every entry shares, so filesystem casing cannot
    ///     change whether a Plugin counts as a base Plugin.
    /// </summary>
    /// <param name="pluginNames">The Plugin filenames in the set.</param>
    /// <returns>An immutable set comparing names with <see cref="StringComparer.OrdinalIgnoreCase" />.</returns>
    private static ImmutableHashSet<string> Create(params string[] pluginNames)
    {
        return ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, pluginNames);
    }
}
