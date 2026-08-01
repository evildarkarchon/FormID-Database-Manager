#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using Mutagen.Bethesda;
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
///         Mutagen's overlay record classes are different types from its in-memory record classes, and Entry
///         Extraction's third tier discovers names by reflecting over the runtime type.
///     </para>
///     <para>
///         One release per family rather than all ten. Per-release overlay wiring is covered at the adapter layer by
///         <c>PluginOverlayConstructionTests</c>, where it is cheap; what differs here is how a family stores the
///         values Entry Extraction reads, and that is a per-family fact, not a per-release one.
///     </para>
///     <para>
///         Assertions are on what a caller can observe — the outcome Plugin Ingestion reports and the rows that land
///         in the FormID Record Store. Nothing here inspects the reflection cache or any private extraction helper.
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
    ///     so a user's FormID coverage stays complete, and pins the label it is given.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <em>Pinned, not endorsed.</em> The label leaks the name of Mutagen's <em>overlay</em> class rather than
    ///         its record class — <c>NpcBinaryOverlay</c>, not <c>Npc</c> — so the Entry a user sees in their database
    ///         differs from the one the same record produces in memory, which is how every other test in this suite
    ///         reads records. Filed as issue #50. It is recorded here rather than corrected because changing it
    ///         changes what users see in their databases, which deserves its own change with its own reasoning — the
    ///         same discipline ADR-0003 applied to the dropdown order.
    ///     </para>
    ///     <para>
    ///         Reaching this label at all is the second finding. Entry Extraction has a tier between the display-name
    ///         cast and this fallback that looks names up reflectively, and it can never succeed: it selects
    ///         <c>INamedGetter</c>, whose <c>Name</c> is a plain string, then asks that string for a nested
    ///         <c>String</c> property it does not have. Every record without an EditorID or a name therefore lands
    ///         here, for all four families. Filed as issue #51. Asserted through the stored Entry rather than by
    ///         inspecting the reflection cache, so the pin survives that tier being repaired or deleted — it would
    ///         then fail here, by name, as a deliberate change to a user-visible value.
    ///     </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(GameFamilyRepresentatives))]
    public async Task IngestAsync_RecordWithNeitherAnEditorIdNorAName_StoresTheSynthesizedOverlayTypeLabel(
        GameRelease release)
    {
        var result = await IngestFixtureAsync(release);

        var synthesized = Assert.Single(
            result.StoredRecords,
            record => record.Entry != PluginFixture.EditorIdRecordEditorId &&
                      record.Entry != PluginFixture.NamedRecordDisplayName);

        // Built from the row's own FormID rather than a literal, so this pins the label's shape and its overlay-type
        // leak without also pinning which FormID Mutagen's writer happened to allocate to the third record.
        Assert.Equal($"[NpcBinaryOverlay_{synthesized.FormId}]", synthesized.Entry);
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
