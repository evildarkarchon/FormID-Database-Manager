#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Exceptions;
using Xunit;

namespace FormID_Database_Manager.Tests.Integration;

/// <summary>
///     Covers Plugin Ingestion end to end for one release of each game family, over a Plugin generated at test time
///     and read through the production overlay adapter and the production Entry Extraction module.
/// </summary>
/// <remarks>
///     <para>
///         Every other Plugin Ingestion test delivers Mutagen records through a substituted overlay, so until this
///         file existed Entry Extraction had never seen a record read out of a <em>binary overlay</em>. That matters:
///         Mutagen's overlay record classes are different types from its in-memory record classes, reaching their
///         EditorID and their name through separately generated members, so what an in-memory record does is not
///         evidence of what a Processing Run reads. Two defects (ADR-0004, ADR-0005) lived in that gap.
///     </para>
///     <para>
///         One release per family rather than all ten. Per-release overlay wiring is covered at the adapter layer by
///         <c>PluginOverlayConstructionTests</c>, where it is cheap; what differs here is how a family stores the
///         values Entry Extraction reads, and that is a per-family fact, not a per-release one.
///     </para>
///     <para>
///         Assertions are on what a caller can observe — the outcome Plugin Ingestion reports and the rows that land
///         in the FormID Record Store. Nothing here inspects a private extraction helper or any internal cache, which
///         is what let the pins below outlive the tier they were written against.
///     </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class PluginIngestionFixtureTests : IDisposable
{
    private const string PluginName = "IngestionFixture.esp";

    private readonly string _testDirectory;

    public PluginIngestionFixtureTests()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"plugin_ingestion_fixture_{Guid.NewGuid():N}");
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
    ///     One release per game family. Listed rather than derived, so adding a family is a deliberate edit here.
    /// </summary>
    public static TheoryData<GameRelease> GameFamilyRepresentatives =>
    [
        GameRelease.SkyrimSE,
        GameRelease.Fallout4,
        GameRelease.Oblivion,
        GameRelease.Starfield
    ];

    /// <summary>
    ///     The game families that have a record type whose display name is a required aspect rather than an optional
    ///     one. Oblivion is absent because Mutagen's Oblivion definitions have no such type, not because it is skipped.
    /// </summary>
    /// <remarks>
    ///     Listed rather than derived from <see cref="PluginFixture.CanGenerateRequiredNamedRecord" />, for the same
    ///     reason <see cref="GameFamilyRepresentatives" /> is: a derived list would narrow to nothing, and pass
    ///     vacuously, if the builder ever stopped reporting a recipe. What keeps the list honest is the guard in
    ///     <c>PluginOverlayConstructionTests</c>, which fails by name if the set of families with such a type changes.
    /// </remarks>
    public static TheoryData<GameRelease> FamiliesWithARequiredNamedRecordType =>
    [
        GameRelease.SkyrimSE,
        GameRelease.Fallout4,
        GameRelease.Starfield
    ];

    /// <summary>
    ///     Verifies a Plugin that yields records is reported as an Ingested Plugin carrying every record the fixture
    ///     holds, so the run summary a user reads is trustworthy and no record silently vanishes.
    /// </summary>
    [Theory]
    [MemberData(nameof(GameFamilyRepresentatives))]
    public async Task IngestAsync_GeneratedPluginForEachFamily_ReportsAnIngestedPluginWithEveryRecord(
        GameRelease release)
    {
        var result = await IngestFixtureAsync(release);

        var ingested = Assert.IsType<IngestedPlugin>(Assert.Single(result.Outcomes));
        Assert.Equal(PluginName, ingested.PluginName);
        Assert.Equal(PluginFixture.RecordCount, ingested.FormIdCount);
        Assert.Null(ingested.Warning);
        Assert.Equal(PluginFixture.RecordCount, result.StoredRecords.Count);
    }

    /// <summary>
    ///     Verifies a record carrying an EditorID stores that EditorID as its Entry, so a database stays searchable by
    ///     the identifier users know records by.
    /// </summary>
    [Theory]
    [MemberData(nameof(GameFamilyRepresentatives))]
    public async Task IngestAsync_RecordWithAnEditorId_StoresTheEditorIdAsTheEntry(GameRelease release)
    {
        var result = await IngestFixtureAsync(release);

        Assert.Contains(result.StoredRecords, record => record.Entry == PluginFixture.EditorIdRecordEditorId);
    }

    /// <summary>
    ///     Verifies a record with no EditorID but a display name stores that name, which is the tier every family
    ///     reaches differently — Oblivion holds a plain string where the newer games hold a translated one.
    /// </summary>
    [Theory]
    [MemberData(nameof(GameFamilyRepresentatives))]
    public async Task IngestAsync_RecordWithOnlyADisplayName_StoresTheDisplayNameAsTheEntry(GameRelease release)
    {
        var result = await IngestFixtureAsync(release);

        Assert.Contains(result.StoredRecords, record => record.Entry == PluginFixture.NamedRecordDisplayName);
    }

    /// <summary>
    ///     Verifies a record with neither an EditorID nor a display name still produces a row rather than vanishing,
    ///     so a user's FormID coverage stays complete, and that the label it is given names the Mutagen record type.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the overlay half of the parity issue #50 reported and ADR-0004 resolved. The label used to leak
    ///         the name of Mutagen's <em>overlay</em> class — <c>NpcBinaryOverlay</c>, not <c>Npc</c> — so the Entry a
    ///         user saw in their database differed from the one the same record produces in memory, which is how every
    ///         other test in this suite reads records. The in-memory half is
    ///         <c>PluginIngestionTests.TryExtract_RecordWithNeitherAnEditorIdNorAName_NamesTheMutagenRecordType</c>,
    ///         which asserts this same literal; the two together are the parity claim.
    ///     </para>
    ///     <para>
    ///         Reaching this label at all was the second finding, filed as issue #51 and resolved by ADR-0005: a tier
    ///         between the display-name cast and this fallback looked names up reflectively and could never succeed,
    ///         so every anonymous record landed here for all four families. Deleting it left this assertion standing,
    ///         because the fixture's third record is an <c>Npc</c> — a type whose name aspect is the optional one, and
    ///         which therefore still has no name to find. What did move out of this label is covered by
    ///         <see cref="IngestAsync_RecordWhoseNameAspectIsRequired_StoresThatName" />.
    ///     </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(GameFamilyRepresentatives))]
    public async Task IngestAsync_RecordWithNeitherAnEditorIdNorAName_NamesTheMutagenRecordType(
        GameRelease release)
    {
        var result = await IngestFixtureAsync(release);

        var synthesized = Assert.Single(
            result.StoredRecords,
            record => record.Entry != PluginFixture.EditorIdRecordEditorId &&
                      record.Entry != PluginFixture.NamedRecordDisplayName);

        // Built from the row's own FormID rather than a literal, so this pins the label's shape and the record type it
        // names without also pinning which FormID Mutagen's writer happened to allocate to the third record.
        Assert.Equal($"[Npc_{synthesized.FormId}]", synthesized.Entry);
    }

    /// <summary>
    ///     Verifies a record read out of a binary overlay whose display name is a <em>required</em> aspect stores that
    ///     name, for every family that has such a record type.
    /// </summary>
    /// <remarks>
    ///     The overlay half of issue #51's fix; the in-memory half is
    ///     <c>PluginIngestionTests.TryExtract_RecordWhoseNameAspectIsRequired_UsesThatNameAsEntry</c>, which explains
    ///     the two name aspects and lists the affected record types. Both halves are needed for the same reason
    ///     ADR-0004 needed both: a Mutagen overlay class reaches a name through its own generated members, so what an
    ///     in-memory record does is not evidence of what a Processing Run reads.
    ///     <para>
    ///         Oblivion is absent from the theory because Mutagen's Oblivion definitions have no required-named record
    ///         type. <c>PluginOverlayConstructionTests.EveryGameFamilyExceptOblivion_HasARequiredNamedRecordRecipe</c>
    ///         holds that claim against the Supported GameRelease table, so this theory's narrower coverage cannot
    ///         quietly become wrong.
    ///     </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(FamiliesWithARequiredNamedRecordType))]
    public async Task IngestAsync_RecordWhoseNameAspectIsRequired_StoresThatName(GameRelease release)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var gameDirectory = Path.Combine(_testDirectory, $"{release}-required-named");
        var dataPath = GameInstallations.CanonicalizeDataDirectory(gameDirectory);
        PluginFixture.WriteWithRequiredNamedRecord(release, dataPath, PluginName);
        var databasePath = Path.Combine(_testDirectory, $"{release}-required-named.db");

        var ingestion = new PluginIngestion(
            new StaticGameLoadOrderProvider(
                GameLoadOrderSnapshotFactory.CreateFixtureSnapshot(release, PluginName)));

        await using (var store = await FormIdRecordStore.OpenAsync(databasePath, release, cancellationToken))
        {
            await ingestion.IngestAsync(
                new SelectedPluginIngestionRequest(gameDirectory, release, [PluginName], UpdateMode.Append),
                store,
                progress: null,
                cancellationToken);
        }

        await using var reopened = await FormIdRecordStore.OpenAsync(databasePath, release, cancellationToken);
        var storedRecords = await reopened.ReadRecordsAsync(FormIdRecordQuery.All, cancellationToken);

        var stored = Assert.Single(storedRecords);
        Assert.Equal(PluginFixture.RequiredNamedRecordDisplayName, stored.Entry);
    }

    /// <summary>
    ///     Verifies a required-named record whose name is absent on disk still reaches the Synthesized Entry, rather
    ///     than storing a blank Entry or raising a Processing Warning.
    /// </summary>
    /// <remarks>
    ///     The one genuinely new failure mode ADR-0005's widened cast opens. A required name has no null state, so the
    ///     cast always succeeds for these record types and the emptiness check is the only thing standing between a
    ///     record with no name subrecord and a blank user-visible Entry. Mutagen substitutes an empty value rather than
    ///     reporting the field missing — <c>TranslatedString.Empty</c> for a translated name, <c>string.Empty</c> for a
    ///     plain one — which is what makes the check sufficient, and it is asserted here rather than trusted.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FamiliesWithARequiredNamedRecordType))]
    public async Task IngestAsync_RequiredNamedRecordWithNoNameOnDisk_StoresTheSynthesizedEntry(GameRelease release)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var gameDirectory = Path.Combine(_testDirectory, $"{release}-required-unnamed");
        var dataPath = GameInstallations.CanonicalizeDataDirectory(gameDirectory);
        PluginFixture.WriteWithRequiredNamedRecord(release, dataPath, PluginName, name: null);
        var databasePath = Path.Combine(_testDirectory, $"{release}-required-unnamed.db");

        var ingestion = new PluginIngestion(
            new StaticGameLoadOrderProvider(
                GameLoadOrderSnapshotFactory.CreateFixtureSnapshot(release, PluginName)));

        PluginIngestionReport report;
        await using (var store = await FormIdRecordStore.OpenAsync(databasePath, release, cancellationToken))
        {
            report = await ingestion.IngestAsync(
                new SelectedPluginIngestionRequest(gameDirectory, release, [PluginName], UpdateMode.Append),
                store,
                progress: null,
                cancellationToken);
        }

        await using var reopened = await FormIdRecordStore.OpenAsync(databasePath, release, cancellationToken);
        var storedRecords = await reopened.ReadRecordsAsync(FormIdRecordQuery.All, cancellationToken);

        // Asserted on the label's shape rather than its record type, so this stays one test across three families
        // whose required-named types differ. The type name itself is ADR-0004's business, pinned above.
        var stored = Assert.Single(storedRecords);
        Assert.StartsWith("[", stored.Entry, StringComparison.Ordinal);
        Assert.EndsWith($"_{stored.FormId}]", stored.Entry, StringComparison.Ordinal);
        Assert.Null(Assert.IsType<IngestedPlugin>(Assert.Single(report.Outcomes)).Warning);
    }

    /// <summary>
    ///     Pins what a Starfield Processing Run does when the resolved Data directory holds the selected Plugin but
    ///     not <c>Starfield.esm</c>: the run aborts with an unhandled Mutagen failure instead of reporting one Failed
    ///     Plugin.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Filed as issue #52 and deliberately not fixed here — #49 puts changing the adapter's classification
    ///         list out of scope. This is the stop-and-report, pinned at the layer a user actually meets it.
    ///     </para>
    ///     <para>
    ///         The snapshot is built the way production's <c>GameLoadOrderProvider</c> builds one, not the way the
    ///         other tests in this file do. That difference is the whole point: production collects master styles only
    ///         for listings whose file exists on disk, and falls back to default read parameters when none do — so a
    ///         Data directory with mods but no game master produces exactly the empty-lookup snapshot below.
    ///         <see cref="GameLoadOrderSnapshotFactory.CreateFixtureSnapshot" /> supplies the lookup unconditionally,
    ///         which is right for covering the happy path and wrong for covering this, so this case bypasses it.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task IngestAsync_StarfieldPluginWhoseMasterIsNotInTheDataDirectory_AbortsRatherThanFailingOnePlugin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const GameRelease release = GameRelease.Starfield;
        var gameDirectory = Path.Combine(_testDirectory, "StarfieldWithoutItsMaster");
        var dataPath = GameInstallations.CanonicalizeDataDirectory(gameDirectory);
        PluginFixture.Write(release, dataPath, PluginName);
        var databasePath = Path.Combine(_testDirectory, "starfield-missing-master.db");

        // No master styles: Starfield.esm is not on disk, so production's provider would collect none either.
        var ingestion = new PluginIngestion(
            new StaticGameLoadOrderProvider(GameLoadOrderSnapshotFactory.CreateSnapshot(PluginName)));

        await using var store = await FormIdRecordStore.OpenAsync(databasePath, release, cancellationToken);
        var exception = await Record.ExceptionAsync(() => ingestion.IngestAsync(
            new SelectedPluginIngestionRequest(gameDirectory, release, [PluginName], UpdateMode.Append),
            store,
            progress: null,
            cancellationToken));

        // Asserted as "did not become an outcome" first, because that is the user-visible defect: the run dies rather
        // than the Plugin failing. The type assertion identifies which Mutagen failure got there.
        Assert.NotNull(exception);
        Assert.IsType<MissingModMappingException>(exception);
    }

    /// <summary>
    ///     Runs production Plugin Ingestion over one generated fixture and returns the outcome alongside the rows that
    ///     reached the real FormID Record Store.
    /// </summary>
    private async Task<FixtureIngestionResult> IngestFixtureAsync(GameRelease release)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var gameDirectory = Path.Combine(_testDirectory, release.ToString());
        var dataPath = GameInstallations.CanonicalizeDataDirectory(gameDirectory);
        PluginFixture.Write(release, dataPath, PluginName);
        var databasePath = Path.Combine(_testDirectory, $"{release}.db");

        // The load order is supplied rather than discovered because no game is installed here; its read parameters
        // resolve the master every fixture declares, which a game with separated master load orders requires.
        var ingestion = new PluginIngestion(
            new StaticGameLoadOrderProvider(
                GameLoadOrderSnapshotFactory.CreateFixtureSnapshot(release, PluginName)));

        PluginIngestionReport report;
        await using (var store = await FormIdRecordStore.OpenAsync(databasePath, release, cancellationToken))
        {
            report = await ingestion.IngestAsync(
                new SelectedPluginIngestionRequest(gameDirectory, release, [PluginName], UpdateMode.Append),
                store,
                progress: null,
                cancellationToken);
        }

        await using var reopened = await FormIdRecordStore.OpenAsync(databasePath, release, cancellationToken);
        var storedRecords = await reopened.ReadRecordsAsync(FormIdRecordQuery.All, cancellationToken);

        return new FixtureIngestionResult(report.Outcomes, storedRecords);
    }

    private sealed record FixtureIngestionResult(
        IReadOnlyList<PluginIngestionOutcome> Outcomes,
        IReadOnlyList<FormIdStoredRecord> StoredRecords);
}
