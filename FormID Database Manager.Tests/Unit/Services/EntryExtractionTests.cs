using System;
using System.Collections.Generic;
using System.IO;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using Moq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Tests Entry Extraction directly: the EditorID tier, the required name aspect (ADR-0005), the Synthesized Entry
///     naming the registered record type (ADR-0004), unusable registrations, nested cancellation, and the Record Issue
///     or run failure each getter's failure is classified as (ADR-0016). None of these
///     needs Plugin Ingestion; the overlay halves of the parity claims live in <c>PluginIngestionFixtureTests</c>.
/// </summary>
public sealed class EntryExtractionTests
{
    /// <summary>
    ///     The selected Plugin every extraction reads on behalf of, which a run failure must name.
    /// </summary>
    private const string PluginName = "Entry.esp";

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

        var record = sut.TryExtract(npc, PluginName, warnings.Add);

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

        var record = sut.TryExtract(npc, PluginName, warnings.Add);

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

        var extracted = sut.TryExtract(record, PluginName, warnings.Add);

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

        var extracted = sut.TryExtract(CreateRecordWithUnusableRegistration(), PluginName, warnings.Add);

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
            PluginName,
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

        var thrown = Assert.Throws<OperationCanceledException>(() =>
            sut.TryExtract(record.Object, PluginName, _ => { }));

        Assert.Same(cancellation, thrown);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Record Issues (ADR-0016 rules 4 to 6). Each getter a record's FormID or Entry is read through is driven to
    // fail: a cause on the Plugin-data family list is one counted Record Issue, carrying that cause's message, and
    // any other cause fails the Processing Run naming the Plugin. Nothing is dropped without being counted.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     The getters Entry Extraction reads an Entry through: the EditorID, then the display name.
    /// </summary>
    public static TheoryData<string> EntryGetters => [EditorIdGetter, NameGetter];

    /// <summary>
    ///     Every getter Entry Extraction reads, the FormID's first.
    /// </summary>
    public static TheoryData<string> EveryGetter => [FormKeyGetter, EditorIdGetter, NameGetter];

    /// <summary>
    ///     Every getter, each with cancellation raised directly and nested in a reflection-style wrapper.
    /// </summary>
    public static TheoryData<string, bool> EveryGetterWithEachCancellationShape
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var getter in new[] { FormKeyGetter, EditorIdGetter, NameGetter })
            {
                data.Add(getter, false);
                data.Add(getter, true);
            }

            return data;
        }
    }

    /// <summary>
    ///     A sample of Plugin-data family causes a getter can raise, including one behind a Mutagen enrichment wrapper.
    ///     The full family list is pinned once, against opening, in <c>MutagenPluginReaderTests</c>; this sample proves
    ///     the per-record path reaches that same list rather than a list of its own.
    /// </summary>
    public static TheoryData<string> ListedGetterCauses =>
    [
        nameof(InvalidDataException),
        nameof(IndexOutOfRangeException),
        nameof(IOException),
        nameof(RecordException)
    ];

    private const string FormKeyGetter = "FormKey";
    private const string EditorIdGetter = "EditorID";
    private const string NameGetter = "Name";

    /// <summary>
    ///     The message every test-built cause carries, so an assertion proves which exception's message was reported.
    /// </summary>
    private const string CauseWording = "Cause wording.";

    /// <summary>
    ///     Verifies a listed cause from the FormID getter drops the record and reports exactly one Record Issue
    ///     carrying the cause's message, looking through a Mutagen enrichment wrapper to that cause.
    /// </summary>
    [Theory]
    [MemberData(nameof(ListedGetterCauses))]
    public void TryExtract_ListedCauseFromTheFormIdGetter_DropsTheRecordAndReportsOneRecordIssue(string cause)
    {
        var record = CreateRecordWhoseGetterFails(FormKeyGetter, CreateListedCause(cause));
        var issues = new List<string>();
        var sut = new EntryExtraction();

        var extracted = sut.TryExtract(record, PluginName, issues.Add);

        Assert.Null(extracted);
        Assert.Equal([CauseWording], issues);
    }

    /// <summary>
    ///     Verifies a listed cause from either Entry getter keeps the record, stored under its FormID with a
    ///     Synthesized Entry, and reports exactly one Record Issue carrying the cause's message.
    /// </summary>
    [Theory]
    [MemberData(nameof(EntryGetters))]
    public void TryExtract_ListedCauseFromAnEntryGetter_StoresASynthesizedEntryAndReportsOneRecordIssue(
        string getter)
    {
        var record = CreateRecordWhoseGetterFails(getter, CreateListedCause(nameof(InvalidDataException)));
        var issues = new List<string>();
        var sut = new EntryExtraction();

        var extracted = sut.TryExtract(record, PluginName, issues.Add);

        Assert.NotNull(extracted);
        Assert.Equal("000001", extracted.Value.FormId);
        Assert.StartsWith("[", extracted.Value.Entry, StringComparison.Ordinal);
        Assert.EndsWith("_000001]", extracted.Value.Entry, StringComparison.Ordinal);
        Assert.Equal([CauseWording], issues);
    }

    /// <summary>
    ///     Verifies an <see cref="IOException" /> from <c>Name</c>, which is how a localized Plugin's failed lazy
    ///     <c>.STRINGS</c> load arrives, costs only the record's name: it is a Record Issue, the record keeps its
    ///     FormID, and neither a Failed Plugin nor a run failure results.
    /// </summary>
    /// <remarks>
    ///     Mutagen caches that load's failure, so it repeats for every record that uses the strings file. Two records
    ///     here prove each one is counted and each one is stored, rather than the first failure ending the Plugin.
    /// </remarks>
    [Fact]
    public void TryExtract_StringsLoadIOExceptionFromName_IsARecordIssueAndKeepsEachRecordsFormId()
    {
        var stringsFailure = new IOException("Could not locate strings file Tested_English.STRINGS.");
        var issues = new List<string>();
        var sut = new EntryExtraction();

        var first = sut.TryExtract(CreateRecordWhoseGetterFails(NameGetter, stringsFailure, 0x000001), PluginName,
            issues.Add);
        var second = sut.TryExtract(CreateRecordWhoseGetterFails(NameGetter, stringsFailure, 0x000002), PluginName,
            issues.Add);

        Assert.Equal("000001", Assert.NotNull(first).FormId);
        Assert.Equal("000002", Assert.NotNull(second).FormId);
        Assert.Equal([stringsFailure.Message, stringsFailure.Message], issues);
    }

    /// <summary>
    ///     Verifies a failure whose message the retired ignore list matched, such as one naming <c>KSIZ</c>, is now
    ///     counted as a Record Issue when its type is listed, rather than dropped without a trace.
    /// </summary>
    [Fact]
    public void TryExtract_ListedCauseWhoseMessageTheIgnoreListMatched_IsReportedAsARecordIssue()
    {
        var failure = new InvalidDataException("KSIZ count did not match the KWDA entries.");
        var record = CreateRecordWhoseGetterFails(FormKeyGetter, failure);
        var issues = new List<string>();
        var sut = new EntryExtraction();

        var extracted = sut.TryExtract(record, PluginName, issues.Add);

        Assert.Null(extracted);
        Assert.Equal([failure.Message], issues);
    }

    /// <summary>
    ///     Verifies a cause the application does not recognise, from any getter, fails the Processing Run naming the
    ///     Plugin and the reading-records phase, with the getter's exception kept, and reports no Record Issue.
    /// </summary>
    /// <remarks>
    ///     A <see cref="NullReferenceException" /> is the case the retired ignore list used to drop silently by matching
    ///     its message (ADR-0016 rules 4 and 5).
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryGetter))]
    public void TryExtract_UnlistedCauseFromAnyGetter_ThrowsUnrecognizedPluginReadFailureNamingThePlugin(
        string getter)
    {
        var defect = new NullReferenceException("Object reference not set to an instance of an object.");
        var record = CreateRecordWhoseGetterFails(getter, defect);
        var issues = new List<string>();
        var sut = new EntryExtraction();

        var thrown = Record.Exception(() => sut.TryExtract(record, PluginName, issues.Add));

        var failure = Assert.IsType<UnrecognizedPluginReadFailureException>(thrown);
        Assert.Equal(PluginName, failure.PluginName);
        Assert.Equal(PluginReadPhase.ReadingRecords, failure.Phase);
        Assert.Same(defect, failure.InnerException);
        Assert.Empty(issues);
    }

    /// <summary>
    ///     Verifies cancellation from any getter, raised directly or nested in a reflection-style wrapper, is rethrown
    ///     as the original cancellation before any classification, and reports no Record Issue.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryGetterWithEachCancellationShape))]
    public void TryExtract_CancellationFromAnyGetter_ThrowsTheOriginalCancellation(string getter, bool nested)
    {
        var cancellation = new OperationCanceledException("Entry extraction cancelled.");
        Exception failure = nested ? new System.Reflection.TargetInvocationException(cancellation) : cancellation;
        var record = CreateRecordWhoseGetterFails(getter, failure);
        var issues = new List<string>();
        var sut = new EntryExtraction();

        var thrown = Assert.Throws<OperationCanceledException>(() => sut.TryExtract(record, PluginName, issues.Add));

        Assert.Same(cancellation, thrown);
        Assert.Empty(issues);
    }

    /// <summary>
    ///     Builds a record with no EditorID and no usable registration whose named getter raises the given failure.
    /// </summary>
    /// <param name="getter">
    ///     <see cref="FormKeyGetter" />, <see cref="EditorIdGetter" />, or <see cref="NameGetter" />. The getters
    ///     before it succeed; for <see cref="NameGetter" /> the record carries the required name aspect, the one
    ///     Entry Extraction reads, and its EditorID is left unset so the name is reached.
    /// </param>
    /// <param name="failure">The exception the getter raises.</param>
    /// <param name="formId">The FormID the record's FormKey carries when that getter succeeds.</param>
    /// <returns>The record.</returns>
    private static IMajorRecordGetter CreateRecordWhoseGetterFails(string getter, Exception failure,
        uint formId = 0x000001)
    {
        var record = new Mock<IMajorRecordGetter>();
        if (getter == FormKeyGetter)
        {
            record.SetupGet(candidate => candidate.FormKey).Throws(failure);
            return record.Object;
        }

        record
            .SetupGet(candidate => candidate.FormKey)
            .Returns(new FormKey(ModKey.FromNameAndExtension(PluginName), formId));
        switch (getter)
        {
            case EditorIdGetter:
                record.SetupGet(candidate => candidate.EditorID).Throws(failure);
                break;
            case NameGetter:
                // As<> must run before Object is first read, so the proxy is generated implementing the aspect.
                record.As<INamedRequiredGetter>().SetupGet(named => named.Name).Throws(failure);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(getter), getter, "Unknown getter.");
        }

        return record.Object;
    }

    /// <summary>
    ///     Creates one listed cause carrying <see cref="CauseWording" />.
    /// </summary>
    /// <param name="cause">
    ///     The cause's type name. <see cref="RecordException" /> stands for an <see cref="IOException" /> behind
    ///     Mutagen's enrichment wrapper, which carries wording of its own so the reported message proves the cause's
    ///     was the one chosen.
    /// </param>
    private static Exception CreateListedCause(string cause)
    {
        return cause switch
        {
            nameof(InvalidDataException) => new InvalidDataException(CauseWording),
            nameof(IndexOutOfRangeException) => new IndexOutOfRangeException(CauseWording),
            nameof(IOException) => new IOException(CauseWording),
            nameof(RecordException) => new RecordException(
                formKey: null,
                recordType: null,
                modKey: ModKey.FromNameAndExtension(PluginName),
                edid: null,
                message: "Wrapper wording.",
                innerException: new IOException(CauseWording)),
            _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "Unknown listed cause.")
        };
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
