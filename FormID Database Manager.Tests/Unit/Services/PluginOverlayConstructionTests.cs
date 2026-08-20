#nullable enable

using System;
using System.IO;
using System.Linq;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using FormID_Database_Manager.Tests.Fakes;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the happy path of the production Mutagen overlay adapter for every row of the Supported GameRelease
///     table, using a Plugin generated at test time for that row's release.
/// </summary>
/// <remarks>
///     <para>
///         Overlay construction is a column of the constant table — ten rows, ten factories — and until these tests
///         existed the only thing asserted about those factories was that they were not null. A row wired to the wrong
///         Mutagen type, or to the right type with the wrong release value, would have shipped undetected and read one
///         game's Plugins under another's rules.
///     </para>
///     <para>
///         The assertion is what a caller can observe about the returned overlay: the GameRelease it reports and its
///         concrete family. The overlay derives its GameRelease from the release value the factory passed, and the
///         Mutagen-release-to-GameRelease conversion is a bijection across all six Skyrim releases, so a within-family
///         swap fails the release assertion and a cross-family swap fails both. Nothing here inspects the table's
///         delegate.
///     </para>
///     <para>
///         Fixtures come from <see cref="PluginFixture" />, whose release mapping is deliberately its own rather than
///         a read of the table. Deriving it from the table would make these assertions circular: a row wired to the
///         wrong release would generate a fixture for that same wrong release and agree with itself.
///     </para>
/// </remarks>
public sealed class PluginOverlayConstructionTests : IDisposable
{
    private readonly IPluginOverlayReader _reader = new MutagenPluginOverlayReader();
    private readonly string _testDirectory;

    public PluginOverlayConstructionTests()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"PluginOverlayConstructionTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    /// <summary>
    ///     Every Supported GameRelease, so a row added to the table inherits this file's coverage rather than shipping
    ///     untested.
    /// </summary>
    public static TheoryData<GameRelease> SupportedReleases =>
        [.. SupportedGameReleases.All.Select(row => row.Release)];

    /// <summary>
    ///     The Mutagen overlay family each release must produce, listed here rather than derived from the table for
    ///     the same anti-circularity reason the fixture generator's mapping is its own.
    /// </summary>
    public static TheoryData<GameRelease, Type> ExpectedOverlayFamilies => new()
    {
        { GameRelease.Fallout4, typeof(IFallout4ModGetter) },
        { GameRelease.Fallout4VR, typeof(IFallout4ModGetter) },
        { GameRelease.SkyrimSE, typeof(ISkyrimModGetter) },
        { GameRelease.SkyrimLE, typeof(ISkyrimModGetter) },
        { GameRelease.SkyrimVR, typeof(ISkyrimModGetter) },
        { GameRelease.SkyrimSEGog, typeof(ISkyrimModGetter) },
        { GameRelease.EnderalSE, typeof(ISkyrimModGetter) },
        { GameRelease.EnderalLE, typeof(ISkyrimModGetter) },
        { GameRelease.Oblivion, typeof(IOblivionModGetter) },
        { GameRelease.Starfield, typeof(IStarfieldModGetter) }
    };

    // ---------------------------------------------------------------------------------------------------------
    // Per-release wiring.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Verifies a row opens a Plugin as the release it claims, which is the complete answer to a mis-wired
    ///     factory: it catches both a swap within a game family and a swap across families.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedReleases))]
    public void ReadOverlay_GeneratedPluginForEachRow_ReportsThatRowsGameRelease(GameRelease release)
    {
        var readyPlugin = CreateProductionReadyPlugin(release, "Wiring.esp");

        using var overlay = _reader.ReadOverlay(readyPlugin);

        Assert.Equal(release, overlay.GameRelease);
    }

    /// <summary>
    ///     Verifies a row opens a Plugin as its game family's Mutagen type, so a cross-family wiring error is
    ///     impossible even if two families ever shared a GameRelease projection.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExpectedOverlayFamilies))]
    public void ReadOverlay_GeneratedPluginForEachRow_ReturnsThatRowsOverlayFamily(
        GameRelease release,
        Type expectedFamily)
    {
        var readyPlugin = CreateProductionReadyPlugin(release, "Family.esp");

        using var overlay = _reader.ReadOverlay(readyPlugin);

        Assert.True(
            expectedFamily.IsInstanceOfType(overlay),
            $"{release} produced a {overlay.GetType().Name}, which is not a {expectedFamily.Name}. Its row in " +
            $"{nameof(SupportedGameReleases)} is wired to the wrong Mutagen overlay type.");
    }

    /// <summary>
    ///     Verifies a ready case carrying production-prepared state reaches the Supported GameRelease overlay row
    ///     identified by that opaque capability.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedReleases))]
    public void ReadOverlay_ProductionReadyPluginForEachRow_ReportsCapabilitiesGameRelease(GameRelease release)
    {
        var readyPlugin = CreateProductionReadyPlugin(release, "Capability.esp");

        using var overlay = _reader.ReadOverlay(readyPlugin);

        Assert.Equal(release, overlay.GameRelease);
    }

    /// <summary>
    ///     Verifies an in-memory capability is rejected as a composition failure before the missing Plugin path can
    ///     reach Mutagen or the filesystem.
    /// </summary>
    [Fact]
    public void ReadOverlay_InMemoryReadyPlugin_RejectsCapabilityBeforeOverlayConstruction()
    {
        const string pluginName = "InMemoryOnly.esp";
        var environment = new InMemoryGameLoadOrderEnvironment()
            .WithLoadOrder(GameRelease.SkyrimSE, _testDirectory, pluginName)
            .WithAvailablePlugin(_testDirectory, pluginName);
        var preparedPlugin = Assert.Single(
            new GameLoadOrders(environment).PrepareSelectedPlugins(
                GameRelease.SkyrimSE,
                _testDirectory,
                [pluginName],
                TestContext.Current.CancellationToken));
        var readyPlugin = Assert.IsType<SelectedPluginReady>(preparedPlugin);

        var exception = Assert.Throws<InvalidOperationException>(() => _reader.ReadOverlay(readyPlugin));

        Assert.Equal("The Plugin-read capability belongs to a different adapter.", exception.Message);
        Assert.False(File.Exists(readyPlugin.ResolvedPluginPath));
    }

    /// <summary>
    ///     Verifies the production capability path retains the existing narrow normalization for malformed Plugin
    ///     bytes rather than treating the opaque state as a new failure boundary.
    /// </summary>
    [Fact]
    public void ReadOverlay_ProductionReadyPluginWithMalformedBytes_ThrowsPluginReadFailure()
    {
        const string pluginName = "CapabilityMalformed.esp";
        var pluginPath = Path.Combine(_testDirectory, pluginName);
        File.WriteAllBytes(pluginPath, [0x01, 0x02, 0x03, 0x04]);
        var capability = new GameLoadOrderEnvironment().PreparePluginReads(GameRelease.SkyrimSE, []);
        var readyPlugin = new SelectedPluginReady(pluginName, pluginPath, capability);

        var exception = Assert.Throws<PluginOverlayReadException>(() => _reader.ReadOverlay(readyPlugin));

        Assert.IsType<MalformedDataException>(exception.InnerException);
    }

    /// <summary>
    ///     Verifies a production capability with a present but empty separated-master lookup preserves Mutagen's
    ///     master-resolution failure for Plugin Ingestion to classify.
    /// </summary>
    [Fact]
    public void ReadOverlay_ProductionReadyPluginWithUnresolvableMaster_EscapesWithoutPluginReadFailure()
    {
        const string pluginName = "CapabilityMissingMaster.esp";
        var pluginPath = PluginFixture.Write(GameRelease.Starfield, _testDirectory, pluginName);
        var capability = new GameLoadOrderEnvironment().PreparePluginReads(GameRelease.Starfield, []);
        var readyPlugin = new SelectedPluginReady(pluginName, pluginPath, capability);

        var exception = Record.Exception(() => _reader.ReadOverlay(readyPlugin));

        Assert.IsNotType<PluginOverlayReadException>(exception);
        Assert.IsType<MissingModException>(exception);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Fixture coverage. Enforced from the constant table outward, which is the direction that is not circular:
    // the table is the authority on which releases exist, and this fails by name for one the generator cannot make.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Verifies every row of the Supported GameRelease table has a Plugin fixture, so a release cannot arrive with
    ///     nine of ten rows covered.
    /// </summary>
    [Fact]
    public void EverySupportedRelease_HasAPluginFixture()
    {
        var ungeneratable = SupportedGameReleases.All
            .Select(row => row.Release)
            .Where(release => !PluginFixture.CanGenerate(release))
            .ToArray();

        Assert.True(
            ungeneratable.Length == 0,
            $"{nameof(SupportedGameReleases)} has rows this suite cannot generate a Plugin for: " +
            $"{string.Join(", ", ungeneratable)}. Add a recipe to {nameof(PluginFixture)} so the release's overlay " +
            "wiring and Entry Extraction are covered like every other release's.");
    }

    /// <summary>
    ///     Verifies the fixture generator carries no recipe for a release the table does not support, so its mapping
    ///     cannot quietly outlive a release the application dropped.
    /// </summary>
    [Fact]
    public void EveryPluginFixtureRecipe_MatchesASupportedRelease()
    {
        var unsupported = PluginFixture.GeneratableReleases
            .Where(release => !SupportedGameReleases.IsSupported(release))
            .ToArray();

        Assert.True(
            unsupported.Length == 0,
            $"{nameof(PluginFixture)} can generate Plugins for releases {nameof(SupportedGameReleases)} does not " +
            $"list: {string.Join(", ", unsupported)}. Remove the recipe, or add the row it was written for.");
    }

    /// <summary>
    ///     Verifies Oblivion is the only game family with no required-named record recipe, so the Entry Extraction
    ///     theories that cover three families rather than four are documenting a fact about Mutagen rather than
    ///     quietly skipping a family.
    /// </summary>
    /// <remarks>
    ///     Mutagen models a display name as either an optional aspect or a required one, and every Oblivion record type
    ///     that has a name has the optional kind — so there is nothing in that family for the required tier to reach
    ///     (ADR-0005). This guard is the fixture half of that claim, enforced from the table outward like its
    ///     neighbours above: it catches a recipe that was never written. The Mutagen half — that Oblivion genuinely has
    ///     no such record type, and the other three families do — is
    ///     <c>MutagenNameAspectTests.Oblivion_HasNoRecordTypeReachableOnlyThroughTheRequiredAspect</c>, which reflects
    ///     over the game assemblies because no read of this suite's own recipes could notice Mutagen gaining one.
    /// </remarks>
    [Fact]
    public void EveryGameFamilyExceptOblivion_HasARequiredNamedRecordRecipe()
    {
        var withoutARecipe = SupportedGameReleases.All
            .Select(row => row.Release)
            .Where(release => !PluginFixture.CanGenerateRequiredNamedRecord(release))
            .ToArray();

        Assert.Equal([GameRelease.Oblivion], withoutARecipe);
    }

    // ---------------------------------------------------------------------------------------------------------
    // The negative boundary of the adapter's classification list. The companion coverage pins which inputs *become*
    // an expected Plugin-read failure; these pin which real Mutagen failures deliberately stay off that list and
    // escape unwrapped. Both need a spec-correct Plugin, which is why they live with the fixture-based tests.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Verifies that opening a master-declaring Starfield Plugin without a master-flags lookup escapes the adapter
    ///     unchanged, so an unexpected internal failure aborts the Processing Run loudly instead of being reported to
    ///     the user as a problem with their Plugin.
    /// </summary>
    /// <remarks>
    ///     The fixture is an ordinary Starfield Plugin, not an exotic one: the format requires every Plugin to list
    ///     its game's main master first, so declaring <c>Starfield.esm</c> is what makes it spec-correct rather than
    ///     what makes it unusual. Starfield uses separated master load orders, so reading one without a lookup raises
    ///     <see cref="MissingModMappingException" />, which matches none of the adapter's classified branches. The
    ///     capability below deliberately carries default read parameters instead of prepared separated-master state.
    ///     What is asserted is the boundary of the list, not this specific Mutagen type — widening the list to swallow
    ///     this would have to be a deliberate edit here.
    /// </remarks>
    [Fact]
    public void ReadOverlay_MasterFlagsLookupMissingEntirely_EscapesWithoutBecomingAPluginReadFailure()
    {
        const string pluginName = "NoLookup.esp";
        var pluginPath = PluginFixture.Write(GameRelease.Starfield, _testDirectory, pluginName);
        var capability = new PluginReadCapability(
            new MutagenPluginReadCapabilityPayload(GameRelease.Starfield, BinaryReadParameters.Default));
        var readyPlugin = new SelectedPluginReady(pluginName, pluginPath, capability);

        var exception = Record.Exception(() => _reader.ReadOverlay(readyPlugin));

        // The "not a Failed Plugin" assertion comes first and names the actual regression; the type assertion below
        // is what identifies which Mutagen failure this case reached.
        Assert.IsNotType<PluginOverlayReadException>(exception);
        Assert.IsType<MissingModMappingException>(exception);
    }

    /// <summary>
    ///     Verifies the same for a lookup that exists but does not contain the master the Plugin declares, which is a
    ///     different Mutagen failure reaching the same boundary.
    /// </summary>
    /// <remarks>
    ///     This case is reachable in production, not merely a constructed one: production Game Load Orders collects
    ///     master styles only for listings whose file exists on disk, so a Starfield Data directory holding
    ///     mods but not <c>Starfield.esm</c> builds exactly this lookup. Issue #52 fixed what a Processing Run does
    ///     with it, and fixed it one layer up — Plugin Ingestion turns this exception into an
    ///     <c>UnresolvableMasterException</c> that stops the run and names the master (ADR-0006). The adapter's list
    ///     deliberately did not widen, which is what this pins: widening it would report a Data-directory problem as a
    ///     Failed Plugin, and would have to be a deliberate edit here.
    /// </remarks>
    [Fact]
    public void ReadOverlay_MasterFlagsLookupWithoutTheDeclaredMaster_EscapesWithoutBecomingAPluginReadFailure()
    {
        const string pluginName = "WrongLookup.esp";
        var pluginPath = PluginFixture.Write(GameRelease.Starfield, _testDirectory, pluginName);
        var lookupMissingTheMaster = new BinaryReadParameters
        {
            MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(
                [new KeyedMasterStyle(ModKey.FromNameAndExtension("NotTheMaster.esm"), MasterStyle.Full)])
        };
        var capability = new PluginReadCapability(
            new MutagenPluginReadCapabilityPayload(GameRelease.Starfield, lookupMissingTheMaster));
        var readyPlugin = new SelectedPluginReady(pluginName, pluginPath, capability);

        var exception = Record.Exception(() => _reader.ReadOverlay(readyPlugin));

        Assert.IsNotType<PluginOverlayReadException>(exception);
        Assert.IsType<MissingModException>(exception);
    }

    /// <summary>
    ///     Creates a generated Plugin and the production adapter capability required to open it.
    /// </summary>
    /// <param name="release">The Supported GameRelease that owns both the Plugin and capability.</param>
    /// <param name="pluginName">The generated selected Plugin filename.</param>
    /// <returns>A ready selected-Plugin case carrying production-prepared read state.</returns>
    private SelectedPluginReady CreateProductionReadyPlugin(GameRelease release, string pluginName)
    {
        var pluginPath = PluginFixture.Write(release, _testDirectory, pluginName);
        var mainMasterName = PluginFixture.MainMasterFor(release);
        var mainMasterPath = PluginFixture.Write(release, _testDirectory, mainMasterName);
        var environment = new GameLoadOrderEnvironment();
        var capability = environment.PreparePluginReads(
            release,
            [new PluginFileObservation(mainMasterName, mainMasterPath, IsAvailable: true)]);

        return new SelectedPluginReady(pluginName, pluginPath, capability);
    }
}
