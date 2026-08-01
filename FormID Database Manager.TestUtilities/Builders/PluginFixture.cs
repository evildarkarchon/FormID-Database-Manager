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
    ///     The display name carried by the record whose name aspect is required rather than optional.
    /// </summary>
    /// <remarks>
    ///     Deliberately different from <see cref="NamedRecordDisplayName" /> so an assertion cannot pass by reading the
    ///     wrong record when a fixture holds both.
    /// </remarks>
    public const string RequiredNamedRecordDisplayName = "Fixture Required Named Record";

    /// <summary>
    ///     The shared fixture recipe: one record with an EditorID, one with only a display name, and one with
    ///     neither. Three records reach all three Entry Extraction tiers. The name here is the optional aspect's;
    ///     <see cref="WriteWithRequiredNamedRecord" /> covers the required one, which no NPC type carries.
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
                modKey => new Fallout4Mod(modKey, Fallout4Release.Fallout4), FalloutMainMaster, AddFallout4Npc,
                AddFallout4Key),
            [GameRelease.Fallout4VR] = new(
                modKey => new Fallout4Mod(modKey, Fallout4Release.Fallout4VR), FalloutMainMaster, AddFallout4Npc,
                AddFallout4Key),
            [GameRelease.SkyrimSE] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.SkyrimSE), SkyrimMainMaster, AddSkyrimNpc,
                AddSkyrimClass),
            [GameRelease.SkyrimLE] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.SkyrimLE), SkyrimMainMaster, AddSkyrimNpc,
                AddSkyrimClass),
            [GameRelease.SkyrimVR] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.SkyrimVR), SkyrimMainMaster, AddSkyrimNpc,
                AddSkyrimClass),
            [GameRelease.SkyrimSEGog] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.SkyrimSEGog), SkyrimMainMaster, AddSkyrimNpc,
                AddSkyrimClass),
            [GameRelease.EnderalSE] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.EnderalSE), SkyrimMainMaster, AddSkyrimNpc,
                AddSkyrimClass),
            [GameRelease.EnderalLE] = new(
                modKey => new SkyrimMod(modKey, SkyrimRelease.EnderalLE), SkyrimMainMaster, AddSkyrimNpc,
                AddSkyrimClass),

            // Oblivion has no required-named recipe: every Oblivion record type that carries a name carries an
            // optional one, so there is nothing in that family for the required tier to reach.
            [GameRelease.Oblivion] = new(
                modKey => new OblivionMod(modKey, OblivionRelease.Oblivion), OblivionMainMaster, AddOblivionNpc),
            [GameRelease.Starfield] = new(
                modKey => new StarfieldMod(modKey, StarfieldRelease.Starfield), StarfieldMainMaster, AddStarfieldNpc,
                AddStarfieldPlanet)
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
    ///     Reports whether a GameRelease has a record type whose display name is a required aspect.
    /// </summary>
    /// <param name="release">The GameRelease to test.</param>
    /// <returns>
    ///     <see langword="true" /> when <see cref="CreateRequiredNamedRecord" /> and
    ///     <see cref="WriteWithRequiredNamedRecord" /> can produce a record for it.
    /// </returns>
    /// <remarks>
    ///     False for Oblivion, whose named record types all carry an optional name. That is a fact about Mutagen's
    ///     Oblivion definitions, not a gap in this builder, so it is reported rather than worked around.
    /// </remarks>
    public static bool CanGenerateRequiredNamedRecord(GameRelease release)
    {
        return Recipes.TryGetValue(release, out var recipe) && recipe.AddRequiredNamedRecord is not null;
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

    /// <summary>
    ///     Creates, in memory and without writing a Plugin, one record of a GameRelease's NPC type carrying neither an
    ///     EditorID nor a display name.
    /// </summary>
    /// <param name="release">The GameRelease whose Mutagen NPC type the record is created from.</param>
    /// <returns>The record, still owned by the mod it was added to.</returns>
    /// <remarks>
    ///     The in-memory counterpart of <see cref="Write" />'s third recipe record, for asserting what Entry Extraction
    ///     does with a record read in memory rather than out of a binary overlay. The two reads produce different CLR
    ///     types for the same record, which is the divergence ADR-0004 resolved, so pinning both halves per family
    ///     takes a per-family source of each.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">This builder has no recipe for <paramref name="release" />.</exception>
    public static IMajorRecordGetter CreateRecordWithoutEditorIdOrName(GameRelease release)
    {
        var recipe = RecipeFor(release);
        var mod = recipe.CreateMod(ModKey.FromNameAndExtension("InMemoryFixture.esp"));
        recipe.AddRecord(mod, null, null);

        // Single rather than First: the recipe added exactly one record, and a second would mean this drifted from
        // Write's shared population rule.
        return mod.EnumerateMajorRecords().Single();
    }

    /// <summary>
    ///     Creates, in memory and without writing a Plugin, one record of a GameRelease's required-named record type,
    ///     carrying a display name but no EditorID.
    /// </summary>
    /// <param name="release">The GameRelease whose required-named Mutagen type the record is created from.</param>
    /// <returns>The record, still owned by the mod it was added to.</returns>
    /// <remarks>
    ///     Mutagen models a record's display name as one of two aspects: an optional one (<c>INamedGetter</c>, whose
    ///     <c>Name</c> is nullable) and a required one (<c>INamedRequiredGetter</c>, whose <c>Name</c> always has a
    ///     value). The optional aspect derives from the required one, so most named record types satisfy both — but a
    ///     handful satisfy only the required one, and those are what this creates. They are the record types Entry
    ///     Extraction used to miss entirely, and every assertion about the required tier needs a source of one.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     This builder has no recipe for <paramref name="release" />, or the release has no required-named record type.
    /// </exception>
    public static IMajorRecordGetter CreateRequiredNamedRecord(GameRelease release)
    {
        var recipe = RecipeFor(release);
        var mod = recipe.CreateMod(ModKey.FromNameAndExtension("InMemoryFixture.esp"));
        RequiredNamedRecordAdderFor(recipe, release)(mod, RequiredNamedRecordDisplayName);

        return mod.EnumerateMajorRecords().Single();
    }

    /// <summary>
    ///     Writes a fixture holding exactly one record of a GameRelease's required-named record type, so the same
    ///     record can be read back out of a binary overlay rather than in memory.
    /// </summary>
    /// <param name="release">The GameRelease whose required-named Mutagen type the fixture uses.</param>
    /// <param name="directoryPath">An existing caller-owned directory to write into.</param>
    /// <param name="pluginName">The Plugin file name, which must parse as a ModKey.</param>
    /// <param name="name">
    ///     The display name to give the record, or <see langword="null" /> to leave its required name unset. A required
    ///     name has no null state, so unset means Mutagen's empty value and, on the way back in, an absent name
    ///     subrecord — which is the only way to reach Entry Extraction's fallback from a required-named record.
    /// </param>
    /// <returns>The full path to the written Plugin.</returns>
    /// <remarks>
    ///     The overlay counterpart of <see cref="CreateRequiredNamedRecord" />. Mutagen's overlay classes reach a
    ///     required name through their own generated members, so an in-memory assertion alone would not cover what a
    ///     Processing Run actually reads — the same in-memory-versus-overlay divergence ADR-0004 resolved.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     This builder has no recipe for <paramref name="release" />, or the release has no required-named record type.
    /// </exception>
    public static string WriteWithRequiredNamedRecord(
        GameRelease release,
        string directoryPath,
        string pluginName,
        string? name = RequiredNamedRecordDisplayName)
    {
        // Resolved before Write rather than inside its callback, so a release with no required-named type fails here
        // rather than after the file has been created.
        var addRequiredNamedRecord = RequiredNamedRecordAdderFor(RecipeFor(release), release);

        return Write(release, directoryPath, pluginName, (_, mod) => addRequiredNamedRecord(mod, name));
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

    private static Action<IMod, string?> RequiredNamedRecordAdderFor(FixtureRecipe recipe, GameRelease release)
    {
        return recipe.AddRequiredNamedRecord ?? throw new ArgumentOutOfRangeException(
            nameof(release),
            release,
            $"{nameof(PluginFixture)} has no required-named record type for this GameRelease. Guard the call with " +
            $"{nameof(CanGenerateRequiredNamedRecord)} rather than assuming every family has one.");
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

    // The required-named record type each family contributes. One per family rather than all of them, chosen to cover
    // both shapes the required aspect takes: Class and Key hold a translated name, Planet a plain string one.

    private static void AddSkyrimClass(IMod mod, string? name)
    {
        PopulateRequiredName(((SkyrimMod)mod).Classes.AddNew(), name);
    }

    private static void AddFallout4Key(IMod mod, string? name)
    {
        PopulateRequiredName(((Fallout4Mod)mod).Keys.AddNew(), name);
    }

    private static void AddStarfieldPlanet(IMod mod, string? name)
    {
        PopulateRequiredName(((StarfieldMod)mod).Planets.AddNew(), name);
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
    ///     Applies a display name to a freshly added record whose name aspect is required rather than optional.
    /// </summary>
    /// <remarks>
    ///     Written against <see cref="INamedRequired" /> for the same reason <see cref="Populate{TRecord}" /> is written
    ///     against <see cref="INamed" />: it is the one aspect all three families' required-named types share, so a
    ///     translated name and a plain string one are both settable as a string through it.
    ///     <para>
    ///         A null name is left unassigned rather than assigned, exactly as <see cref="Populate{TRecord}" /> does.
    ///         The aspect has no null state to assign — a required name is always present — so unset leaves Mutagen's
    ///         own empty default, which is what a real record missing its name subrecord reads back as.
    ///     </para>
    /// </remarks>
    private static void PopulateRequiredName<TRecord>(TRecord record, string? name)
        where TRecord : IMajorRecord, INamedRequired
    {
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
    /// <param name="AddRequiredNamedRecord">
    ///     Adds one record of the release's required-named type with the supplied display name, leaving that name unset
    ///     when it is null. The delegate itself is null for a release whose record types all carry an optional name.
    /// </param>
    private sealed record FixtureRecipe(
        Func<ModKey, IMod> CreateMod,
        string MainMaster,
        Action<IMod, string?, string?> AddRecord,
        Action<IMod, string?>? AddRequiredNamedRecord = null);
}
