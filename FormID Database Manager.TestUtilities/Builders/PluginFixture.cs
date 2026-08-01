using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;

namespace FormID_Database_Manager.TestUtilities.Builders;

/// <summary>
///     Writes minimal but spec-correct Plugins for every GameRelease this application supports, using Mutagen's own
///     writer, into a caller-owned directory.
/// </summary>
/// <remarks>
///     <para>
///         The release-to-Mutagen mapping below is this builder's own, written independently of the production
///         Supported GameRelease table. Deriving it from that table would make the per-release wiring assertion
///         circular: a row wired to the wrong Mutagen release would generate a fixture for that same wrong release
///         and pass. <see cref="MainMasterFor" /> is duplicated for the same reason — the master names here are this
///         builder's constants, not a read of the table's Base Game Plugin sets. Coverage is enforced the other way
///         round and is not circular: a test walks the production table and fails by name for any row this builder
///         cannot generate.
///     </para>
///     <para>
///         Every fixture declares its game's main master first, because that is what the Plugin format requires of
///         every Plugin. The declared master does not have to exist on disk; a game with separated master load orders
///         needs only a master-flags lookup that can resolve the ModKey, which
///         <c>GameLoadOrderSnapshotFactory.CreateFixtureSnapshot</c> supplies from <see cref="MasterStylesFor" />.
///     </para>
///     <para>
///         Fixtures are generated at test time and never committed, so a format change in a Mutagen upgrade surfaces
///         as a real failure rather than as a stale binary artifact. Round-trip blindness is accepted: nothing
///         asserted against these files depends on the bytes being byte-for-byte what a game would produce, only on
///         the file being readable and the wiring being right. Mutagen's own passthrough tests cover format fidelity.
///     </para>
/// </remarks>
public static class PluginFixture
{
    /// <summary>
    ///     The EditorID carried by the one fixture record that has one.
    /// </summary>
    public const string EditorIdRecordEditorId = "FixtureEditorIdRecord";

    /// <summary>
    ///     The display name carried by the one fixture record that has a name but no EditorID.
    /// </summary>
    public const string NamedRecordDisplayName = "Fixture Named Record";

    /// <summary>
    ///     The shared fixture recipe: one record with an EditorID, one with only a display name, and one with
    ///     neither. Three records reach all four Entry Extraction tiers.
    /// </summary>
    private static readonly (string? EditorId, string? Name)[] SharedRecipe =
    [
        (EditorIdRecordEditorId, null),
        (null, NamedRecordDisplayName),
        (null, null)
    ];

    /// <summary>
    ///     Gets the number of records <see cref="Write" /> puts in every fixture. Derived from the recipe rather than
    ///     stated separately, so the two cannot drift.
    /// </summary>
    public static int RecordCount => SharedRecipe.Length;

    private const string SkyrimMainMaster = "Skyrim.esm";
    private const string FalloutMainMaster = "Fallout4.esm";
    private const string OblivionMainMaster = "Oblivion.esm";
    private const string StarfieldMainMaster = "Starfield.esm";

    /// <summary>
    ///     One recipe per GameRelease this builder can generate a Plugin for.
    /// </summary>
    private static readonly IReadOnlyDictionary<GameRelease, FixtureRecipe> Recipes =
        new Dictionary<GameRelease, FixtureRecipe>
        {
            [GameRelease.Fallout4] = new(
                modKey => new Fallout4Mod(modKey, Fallout4Release.Fallout4), FalloutMainMaster, AddFallout4Npc),
            [GameRelease.Fallout4VR] = new(
                modKey => new Fallout4Mod(modKey, Fallout4Release.Fallout4VR), FalloutMainMaster, AddFallout4Npc),
            [GameRelease.SkyrimSE] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.SkyrimSE), SkyrimMainMaster, AddSkyrimNpc),
            [GameRelease.SkyrimLE] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.SkyrimLE), SkyrimMainMaster, AddSkyrimNpc),
            [GameRelease.SkyrimVR] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.SkyrimVR), SkyrimMainMaster, AddSkyrimNpc),
            [GameRelease.SkyrimSEGog] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.SkyrimSEGog), SkyrimMainMaster, AddSkyrimNpc),
            [GameRelease.EnderalSE] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.EnderalSE), SkyrimMainMaster, AddSkyrimNpc),
            [GameRelease.EnderalLE] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.EnderalLE), SkyrimMainMaster, AddSkyrimNpc),
            [GameRelease.Oblivion] = new(
                modKey => new OblivionMod(modKey, OblivionRelease.Oblivion), OblivionMainMaster, AddOblivionNpc),
            [GameRelease.Starfield] = new(
                modKey => new StarfieldMod(modKey, StarfieldRelease.Starfield), StarfieldMainMaster, AddStarfieldNpc)
        };

    /// <summary>
    ///     Gets the GameReleases this builder can write a fixture for, so a coverage guard can compare them against
    ///     the production Supported GameRelease table.
    /// </summary>
    public static IReadOnlyCollection<GameRelease> GeneratableReleases { get; } = [.. Recipes.Keys];

    /// <summary>
    ///     Reports whether this builder has a recipe for a GameRelease.
    /// </summary>
    /// <param name="release">The GameRelease to test.</param>
    /// <returns><see langword="true" /> when <see cref="Write" /> would produce a Plugin for it.</returns>
    public static bool CanGenerate(GameRelease release)
    {
        return Recipes.ContainsKey(release);
    }

    /// <summary>
    ///     Gets the main master file name every fixture for a GameRelease declares.
    /// </summary>
    /// <param name="release">The GameRelease whose main master is wanted.</param>
    /// <returns>The main master file name, for example <c>Skyrim.esm</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">This builder has no recipe for <paramref name="release" />.</exception>
    public static string MainMasterFor(GameRelease release)
    {
        return RecipeFor(release).MainMaster;
    }

    /// <summary>
    ///     Builds the master styles a game with separated master load orders needs in order to resolve the master a
    ///     fixture declares.
    /// </summary>
    /// <param name="release">The GameRelease whose fixtures are being read.</param>
    /// <returns>One entry, for the declared main master.</returns>
    /// <remarks>
    ///     The master file itself is never written to disk: Mutagen only needs the ModKey's master style, so a
    ///     <see cref="KeyedMasterStyle" /> is enough. Full is the correct style for a game's main master, which is
    ///     neither a small nor a medium master.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">This builder has no recipe for <paramref name="release" />.</exception>
    public static IReadOnlyList<IModMasterStyledGetter> MasterStylesFor(GameRelease release)
    {
        var mainMaster = ModKey.FromNameAndExtension(MainMasterFor(release));
        return [new KeyedMasterStyle(mainMaster, MasterStyle.Full)];
    }

    /// <summary>
    ///     Writes the shared fixture recipe: one record with an EditorID, one with a display name but no EditorID, and
    ///     one with neither.
    /// </summary>
    /// <param name="release">The GameRelease whose Mutagen type and main master the fixture uses.</param>
    /// <param name="directoryPath">An existing caller-owned directory to write into.</param>
    /// <param name="pluginName">The Plugin file name, which must parse as a ModKey.</param>
    /// <returns>The full path to the written Plugin.</returns>
    /// <exception cref="ArgumentOutOfRangeException">This builder has no recipe for <paramref name="release" />.</exception>
    public static string Write(GameRelease release, string directoryPath, string pluginName)
    {
        return Write(release, directoryPath, pluginName, static (recipe, mod) =>
        {
            foreach (var (editorId, name) in SharedRecipe)
            {
                recipe.AddRecord(mod, editorId, name);
            }
        });
    }

    /// <summary>
    ///     Writes a fixture carrying a caller-chosen number of fully named records, for scenarios that need volume
    ///     rather than Entry Extraction coverage.
    /// </summary>
    /// <param name="release">The GameRelease whose Mutagen type and main master the fixture uses.</param>
    /// <param name="directoryPath">An existing caller-owned directory to write into.</param>
    /// <param name="pluginName">The Plugin file name, which must parse as a ModKey.</param>
    /// <param name="recordCount">The number of records to write.</param>
    /// <returns>The full path to the written Plugin.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     This builder has no recipe for <paramref name="release" />, or <paramref name="recordCount" /> is negative.
    /// </exception>
    public static string WriteWithNamedRecords(
        GameRelease release,
        string directoryPath,
        string pluginName,
        int recordCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);

        return Write(release, directoryPath, pluginName, (recipe, mod) =>
        {
            for (var index = 0; index < recordCount; index++)
            {
                recipe.AddRecord(mod, $"FIXTURE_{index:D6}", $"Fixture Record {index}");
            }
        });
    }

    private static string Write(
        GameRelease release,
        string directoryPath,
        string pluginName,
        Action<FixtureRecipe, IMod> addRecords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        var recipe = RecipeFor(release);
        var modKey = ModKey.FromNameAndExtension(pluginName);
        var mod = recipe.CreateMod(modKey);

        // Declared before the records are added so the fixture is shaped like a real Plugin, whose masters list comes
        // first in the mod header. NoCheck below is what keeps it: the default Iterate rebuilds the list from record
        // content, and these records reference nothing, so an unconditional write would drop the master again.
        mod.MasterReferences.Add(new MasterReference { Master = ModKey.FromNameAndExtension(recipe.MainMaster) });
        addRecords(recipe, mod);

        Directory.CreateDirectory(directoryPath);
        var pluginPath = Path.Combine(directoryPath, pluginName);
        mod.WriteToBinary(
            pluginPath,
            new BinaryWriteParameters
            {
                MastersListContent = MastersListContentOption.NoCheck,
                MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(MasterStylesFor(release))
            });

        return pluginPath;
    }

    private static FixtureRecipe RecipeFor(GameRelease release)
    {
        if (Recipes.TryGetValue(release, out var recipe))
        {
            return recipe;
        }

        throw new ArgumentOutOfRangeException(
            nameof(release),
            release,
            $"{nameof(PluginFixture)} has no recipe for this GameRelease. Add one so the release's Plugins can be " +
            "generated at test time.");
    }

    // Each family reaches its own NPC type, but all four share one population rule below. Only the group access is
    // family-specific, because Mutagen's per-game record types have no common typed accessor.

    private static void AddSkyrimNpc(IMod mod, string? editorId, string? name)
    {
        Populate(((SkyrimMod)mod).Npcs.AddNew(), editorId, name);
    }

    private static void AddFallout4Npc(IMod mod, string? editorId, string? name)
    {
        Populate(((Fallout4Mod)mod).Npcs.AddNew(), editorId, name);
    }

    private static void AddOblivionNpc(IMod mod, string? editorId, string? name)
    {
        Populate(((OblivionMod)mod).Npcs.AddNew(), editorId, name);
    }

    private static void AddStarfieldNpc(IMod mod, string? editorId, string? name)
    {
        Populate(((StarfieldMod)mod).Npcs.AddNew(), editorId, name);
    }

    /// <summary>
    ///     Applies the shared population rule to a freshly added record of any game family.
    /// </summary>
    /// <remarks>
    ///     Written against the <see cref="INamed" /> aspect rather than each family's own record type, which is what
    ///     lets one rule cover all four: Oblivion stores a plain string name where the newer games store a translated
    ///     one, but <c>ITranslatedNamed</c> derives from <see cref="INamed" />, so both are settable as a string here.
    ///     A null name is left unset rather than assigned, so the "neither an EditorID nor a name" record reaches
    ///     Entry Extraction's fallback the same way a real record missing both would.
    /// </remarks>
    private static void Populate<TRecord>(TRecord record, string? editorId, string? name)
        where TRecord : IMajorRecord, INamed
    {
        record.EditorID = editorId;
        if (name is not null)
        {
            record.Name = name;
        }
    }

    /// <summary>
    ///     One row of this builder's own release mapping.
    /// </summary>
    /// <param name="CreateMod">Creates an empty writable mod of the release's Mutagen type, at the release's value.</param>
    /// <param name="MainMaster">The main master file name every fixture for the release declares.</param>
    /// <param name="AddRecord">Adds one record with the supplied EditorID and display name, either of which may be null.</param>
    private sealed record FixtureRecipe(
        Func<ModKey, IMod> CreateMod,
        string MainMaster,
        Action<IMod, string?, string?> AddRecord);
}
