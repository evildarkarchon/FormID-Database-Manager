using System;
using System.Collections.Generic;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using Moq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Tests Entry Extraction directly: the EditorID tier, the required name aspect (ADR-0005), the Synthesized Entry
///     naming the registered record type (ADR-0004), unusable registrations, and nested cancellation. None of these
///     needs Plugin Ingestion; the overlay halves of the parity claims live in <c>PluginIngestionFixtureTests</c>.
/// </summary>
public sealed class EntryExtractionTests
{
    /// <summary>
    ///     One release per game family. Listed rather than derived, so adding a family is a deliberate edit here — the
    ///     same convention <c>PluginIngestionFixtureTests</c> follows, and re-listed rather than shared with it so
    ///     neither file's coverage can be silently narrowed by an edit to the other.
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

    [Fact]
    public void TryExtract_RecordWithEditorId_UsesEditorIdAsEntry()
    {
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Entry.esp"), SkyrimRelease.SkyrimSE);
        var npc = mod.Npcs.AddNew("NPC_TEST");
        var warnings = new List<string>();
        var sut = new EntryExtraction();

        var record = sut.TryExtract(npc, warnings.Add);

        Assert.NotNull(record);
        Assert.Equal("NPC_TEST", record.Value.Entry);
        Assert.Empty(warnings);
    }

    /// <summary>
    ///     Verifies the synthesized Entry names the Mutagen record type — <c>Npc</c> — for a record carrying neither an
    ///     EditorID nor a display name, for one release of each game family.
    /// </summary>
    /// <remarks>
    ///     This is the in-memory half of the parity issue #50 reported. Its overlay half is
    ///     <c>PluginIngestionFixtureTests.IngestAsync_RecordWithNeitherAnEditorIdNorAName_NamesTheMutagenRecordType</c>,
    ///     which asserts this same literal for the same record read out of a binary overlay, over the same four
    ///     families. The two together are the claim: which label a user gets no longer depends on how Mutagen was
    ///     asked to open the file. Covering only one family here would leave the other three resting on ADR-0004's
    ///     reasoning rather than on an assertion.
    /// </remarks>
    [Theory]
    [MemberData(nameof(GameFamilyRepresentatives))]
    public void TryExtract_RecordWithNeitherAnEditorIdNorAName_NamesTheMutagenRecordType(GameRelease release)
    {
        var npc = PluginFixture.CreateRecordWithoutEditorIdOrName(release);
        var warnings = new List<string>();
        var sut = new EntryExtraction();

        var record = sut.TryExtract(npc, warnings.Add);

        Assert.NotNull(record);
        Assert.Equal($"[Npc_{npc.FormKey.ID:X6}]", record.Value.Entry);
        Assert.Empty(warnings);
    }

    /// <summary>
    ///     Verifies a record whose display name is a <em>required</em> aspect stores that name, rather than being
    ///     treated as anonymous and given a synthesized label, for every family that has such a record type.
    /// </summary>
    /// <remarks>
    ///     Mutagen splits the name aspect in two: an optional <c>INamedGetter</c> and a required
    ///     <c>INamedRequiredGetter</c> the optional one derives from. A handful of record types — Skyrim's <c>Class</c>,
    ///     <c>Key</c>, <c>Flora</c>, <c>Eyes</c> and <c>CollisionLayer</c>, Fallout 4's <c>Key</c>, <c>Flora</c> and
    ///     <c>CollisionLayer</c>, Starfield's <c>Planet</c> and <c>CollisionLayer</c> — implement only the required
    ///     aspect, so a cast to the optional one misses them. Every such record with no EditorID used to land in the
    ///     synthesized-Entry tier with its real name sitting unread on the record (issue #51, ADR-0005). The overlay
    ///     half is <c>PluginIngestionFixtureTests.IngestAsync_RecordWhoseNameAspectIsRequired_StoresThatName</c>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FamiliesWithARequiredNamedRecordType))]
    public void TryExtract_RecordWhoseNameAspectIsRequired_UsesThatNameAsEntry(GameRelease release)
    {
        var record = PluginFixture.CreateRequiredNamedRecord(release);
        var warnings = new List<string>();
        var sut = new EntryExtraction();

        var extracted = sut.TryExtract(record, warnings.Add);

        Assert.NotNull(extracted);
        Assert.Equal(PluginFixture.RequiredNamedRecordDisplayName, extracted.Value.Entry);
        Assert.Empty(warnings);
    }

    /// <summary>
    ///     Verifies a record whose registration is null still gets a synthesized Entry carrying its FormID rather than
    ///     losing its row, and raises no Processing Warning.
    /// </summary>
    /// <remarks>
    ///     ADR-0004's last-resort branch. <see cref="IMajorRecordGetter" /> inherits <c>ILoquiObject</c>, so this is a
    ///     record breaking that contract; no record Mutagen generates does, which leaves only a double like this here.
    ///     The assertion is on the label's shape rather than on the runtime type name it is built from, because the
    ///     only way to write that name down for a generated mock proxy is the very expression under test.
    /// </remarks>
    [Fact]
    public void TryExtract_RecordWithNullRegistration_StillSynthesizesAnEntryCarryingTheFormId()
    {
        var sut = new EntryExtraction();
        var warnings = new List<string>();

        var extracted = sut.TryExtract(CreateRecordWithUnusableRegistration(), warnings.Add);

        Assert.NotNull(extracted);
        Assert.StartsWith("[", extracted.Value.Entry, StringComparison.Ordinal);
        Assert.EndsWith("_000001]", extracted.Value.Entry, StringComparison.Ordinal);
        Assert.Empty(warnings);
    }

    /// <summary>
    ///     Verifies a record whose registration lookup throws is still labelled rather than losing its row, and that
    ///     the throw does not become a Processing Warning.
    /// </summary>
    /// <remarks>
    ///     A record that reached this tier has already lost its EditorID and its name, so a diagnostic about the third
    ///     missing value would report the same fact a third time; what must not happen is the throw escaping.
    /// </remarks>
    [Fact]
    public void TryExtract_RecordWhoseRegistrationThrows_StillSynthesizesAnEntryWithoutWarning()
    {
        var sut = new EntryExtraction();
        var warnings = new List<string>();

        var extracted = sut.TryExtract(
            CreateRecordWithUnusableRegistration(new InvalidOperationException("registration unavailable")),
            warnings.Add);

        Assert.NotNull(extracted);
        Assert.EndsWith("_000001]", extracted.Value.Entry, StringComparison.Ordinal);
        Assert.Empty(warnings);
    }

    /// <summary>
    ///     Verifies that reflection-style exception wrapping cannot turn record-getter cancellation into a recoverable
    ///     Entry Extraction issue.
    /// </summary>
    [Fact]
    public void TryExtract_NestedCancellation_ThrowsOriginalOperationCanceledException()
    {
        var cancellation = new OperationCanceledException("Entry extraction cancelled.");
        var record = new Mock<IMajorRecordGetter>();
        record
            .SetupGet(candidate => candidate.FormKey)
            .Throws(new System.Reflection.TargetInvocationException(cancellation));
        var sut = new EntryExtraction();

        var thrown = Assert.Throws<OperationCanceledException>(() => sut.TryExtract(record.Object, _ => { }));

        Assert.Same(cancellation, thrown);
    }

    /// <summary>
    ///     Builds a record with no EditorID whose registration is unusable, so Entry Extraction must fall back to the
    ///     runtime type name.
    /// </summary>
    /// <param name="registrationFailure">
    ///     The failure the registration lookup should raise, or null to leave the registration null — the two ways a
    ///     record can break the <c>ILoquiObject</c> contract that <see cref="IMajorRecordGetter" /> inherits.
    /// </param>
    /// <returns>The record, carrying FormID <c>000001</c>.</returns>
    private static IMajorRecordGetter CreateRecordWithUnusableRegistration(Exception? registrationFailure = null)
    {
        var record = new Mock<IMajorRecordGetter>();
        record
            .SetupGet(candidate => candidate.FormKey)
            .Returns(new FormKey(ModKey.FromNameAndExtension("Fallback.esp"), 0x000001));

        // EditorID and Name are both left unset, so the record reaches the synthesized-Entry tier the same way a real
        // record missing both would. Registration is null by default, which is the no-registration case itself.
        if (registrationFailure is not null)
        {
            record.SetupGet(candidate => candidate.Registration).Throws(registrationFailure);
        }

        return record.Object;
    }
}
