using System.Collections.Frozen;
using System.Collections.Immutable;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Everything this application knows about one Supported GameRelease: its FormID Record Store table name, the
///     Base Game Plugins it ships, and how to open one of its Plugins as a Mutagen binary overlay.
/// </summary>
/// <remarks>
///     A sealed class rather than a record: <see cref="CreateOverlay" /> is a delegate, and delegates compare by
///     reference, so record value-equality would be surprising rather than useful here (ADR-0003).
/// </remarks>
internal sealed class SupportedGameRelease
{
    /// <summary>
    ///     Builds one row of the Supported GameRelease table.
    /// </summary>
    /// <param name="release">The GameRelease this row describes.</param>
    /// <param name="tableName">The literal SQLite table name for the release's FormID Record Store.</param>
    /// <param name="basePlugins">The release's Base Game Plugins, immutable and compared case-insensitively.</param>
    /// <param name="createOverlay">Opens a Plugin path as the Mutagen overlay type this release uses.</param>
    internal SupportedGameRelease(
        GameRelease release,
        string tableName,
        ImmutableHashSet<string> basePlugins,
        Func<string, BinaryReadParameters, IModDisposeGetter> createOverlay)
    {
        Release = release;
        TableName = tableName;
        BasePlugins = basePlugins;
        CreateOverlay = createOverlay;
    }

    /// <summary>
    ///     Gets the GameRelease this row describes.
    /// </summary>
    internal GameRelease Release { get; }

    /// <summary>
    ///     Gets the SQLite table name for this release's FormID Record Store. Always a literal from the table, never
    ///     derived from a caller-supplied value, which is what keeps GameRelease out of SQL text (ADR-0003).
    /// </summary>
    internal string TableName { get; }

    /// <summary>
    ///     Gets the Base Game Plugins that ship with this release — its master files and official add-ons. Immutable
    ///     and case-insensitive, so filesystem casing cannot change whether a Plugin counts as a Base Game Plugin and
    ///     no caller has to defensively copy. Releases of the same game share one set instance.
    /// </summary>
    internal ImmutableHashSet<string> BasePlugins { get; }

    /// <summary>
    ///     Gets the factory that opens a Plugin path as this release's Mutagen overlay type, given the shared
    ///     load-order-aware binary read parameters. This is data in a constant table, not a seam: callers reach it
    ///     through <see cref="IPluginOverlayReader" />, which owns failure normalization.
    /// </summary>
    internal Func<string, BinaryReadParameters, IModDisposeGetter> CreateOverlay { get; }
}

/// <summary>
///     The constant table of Supported GameReleases — one row per GameRelease this application can process, carrying
///     every fact it knows about that release.
/// </summary>
/// <remarks>
///     <para>
///         This is data, not a service: nothing about it varies at runtime, so it is a static table rather than an
///         injected dependency, and consumers read it directly instead of taking a whole module to call one method
///         (ADR-0002, ADR-0003).
///     </para>
///     <para>
///         Adding a release is one row here. Because each row must supply every column, an omitted table name, Base
///         Game Plugin set, or overlay factory is a compile error rather than a silent gap — which is the whole point
///         of the table, since three of the four sites it replaced failed silently when missed.
///     </para>
///     <para>
///         Mutagen defines GameReleases this application does not support. <see cref="ForRelease" /> throws for every
///         one of them, so no caller can proceed on an empty or default answer; <see cref="IsSupported" /> is the
///         non-throwing form for the one caller that needs to reject rather than fail.
///     </para>
/// </remarks>
internal static class SupportedGameReleases
{
    /// <summary>
    ///     Shared by every Skyrim release. The Enderal releases are total conversions built on Skyrim's masters, so
    ///     they hide the same base Plugins rather than owning a separate set.
    /// </summary>
    private static readonly ImmutableHashSet<string> SkyrimPlugins = CreatePluginSet(
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
    private static readonly ImmutableHashSet<string> FalloutPlugins = CreatePluginSet(
        "Fallout4.esm",
        "DLCRobot.esm",
        "DLCworkshop01.esm",
        "DLCCoast.esm",
        "DLCworkshop02.esm",
        "DLCworkshop03.esm",
        "DLCNukaWorld.esm");

    private static readonly ImmutableHashSet<string> OblivionPlugins = CreatePluginSet(
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

    private static readonly ImmutableHashSet<string> StarfieldPlugins = CreatePluginSet(
        "Starfield.esm",
        "BlueprintShips-Starfield.esm",
        "OldMars.esm",
        "Constellation.esm");

    /// <summary>
    ///     Gets the Supported GameReleases, in the order the game dropdown shows them.
    /// </summary>
    /// <remarks>
    ///     The order below is the dropdown order and is deliberate, not incidental: it is what users see, and a test
    ///     pins it. It reads accidental — the two Fallout entries are split across the list — and is expected to
    ///     change eventually, but as its own change with its own reasoning rather than as a side effect of some other
    ///     edit. Reordering these lines reorders the dropdown.
    /// </remarks>
    internal static IReadOnlyList<SupportedGameRelease> All { get; } =
    [
        new(GameRelease.Fallout4, "Fallout4", FalloutPlugins,
            (path, parameters) => Fallout4Mod.CreateFromBinaryOverlay(path, Fallout4Release.Fallout4, parameters)),
        new(GameRelease.SkyrimSE, "SkyrimSE", SkyrimPlugins,
            (path, parameters) => SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE, parameters)),
        new(GameRelease.SkyrimLE, "SkyrimLE", SkyrimPlugins,
            (path, parameters) => SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimLE, parameters)),
        new(GameRelease.SkyrimVR, "SkyrimVR", SkyrimPlugins,
            (path, parameters) => SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimVR, parameters)),
        new(GameRelease.SkyrimSEGog, "SkyrimSEGog", SkyrimPlugins,
            (path, parameters) => SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSEGog, parameters)),
        new(GameRelease.EnderalSE, "EnderalSE", SkyrimPlugins,
            (path, parameters) => SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.EnderalSE, parameters)),
        new(GameRelease.EnderalLE, "EnderalLE", SkyrimPlugins,
            (path, parameters) => SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.EnderalLE, parameters)),
        new(GameRelease.Fallout4VR, "Fallout4VR", FalloutPlugins,
            (path, parameters) => Fallout4Mod.CreateFromBinaryOverlay(path, Fallout4Release.Fallout4VR, parameters)),
        new(GameRelease.Oblivion, "Oblivion", OblivionPlugins,
            (path, parameters) => OblivionMod.CreateFromBinaryOverlay(path, OblivionRelease.Oblivion, parameters)),
        new(GameRelease.Starfield, "Starfield", StarfieldPlugins,
            (path, parameters) => StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, parameters))
    ];

    /// <summary>
    ///     Indexes <see cref="All" /> so lookups do not walk the list. Frozen because the table never changes after
    ///     static initialization.
    /// </summary>
    private static readonly FrozenDictionary<GameRelease, SupportedGameRelease> ByRelease =
        All.ToFrozenDictionary(static row => row.Release);

    /// <summary>
    ///     Gets the row for a Supported GameRelease.
    /// </summary>
    /// <param name="release">The GameRelease whose facts are wanted.</param>
    /// <returns>The row carrying that release's table name, Base Game Plugins, and overlay factory.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="release" /> is not a Supported GameRelease. One rule covers both a GameRelease Mutagen
    ///     defines but this application does not support and an entirely undefined enum value, so there is no code
    ///     path where an unsupported release yields an empty or default answer.
    /// </exception>
    internal static SupportedGameRelease ForRelease(GameRelease release)
    {
        if (ByRelease.TryGetValue(release, out var supported))
        {
            return supported;
        }

        throw new ArgumentOutOfRangeException(nameof(release), release, "Unsupported GameRelease value.");
    }

    /// <summary>
    ///     Reports whether a GameRelease is in the table.
    /// </summary>
    /// <param name="release">The GameRelease to test.</param>
    /// <returns><see langword="true" /> when <see cref="ForRelease" /> would return a row.</returns>
    /// <remarks>
    ///     Exists for the Plugin List Source guard, which must reject an unsupported release with context rather than
    ///     fail inside a lookup. It is a narrower answer to that need than returning a nullable row, and no other
    ///     caller needs it.
    /// </remarks>
    internal static bool IsSupported(GameRelease release)
    {
        return ByRelease.ContainsKey(release);
    }

    /// <summary>
    ///     Builds one Base Game Plugin set with the case-insensitive comparer every set shares, so filesystem casing
    ///     cannot change whether a Plugin counts as a Base Game Plugin.
    /// </summary>
    /// <param name="pluginNames">The Plugin filenames in the set.</param>
    /// <returns>An immutable set comparing names with <see cref="StringComparer.OrdinalIgnoreCase" />.</returns>
    private static ImmutableHashSet<string> CreatePluginSet(params string[] pluginNames)
    {
        return ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, pluginNames);
    }
}
