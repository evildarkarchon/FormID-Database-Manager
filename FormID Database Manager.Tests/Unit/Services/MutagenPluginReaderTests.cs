#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FormID_Database_Manager.Services;
using Moq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Tests the production Plugin read adapter through its one test seam, the overlay-opening delegate, with Moq'd
///     overlays standing in for Plugins that would otherwise need real broken files.
/// </summary>
public sealed class MutagenPluginReaderTests
{
    private const string PluginName = "Tested.esp";
    private const string PluginPath = @"C:\Data\Tested.esp";

    /// <summary>
    ///     Verifies that opening hands the delegate the Supported GameRelease row, the resolved path, and the prepared
    ///     read parameters, and that reading yields each record's FormID and Entry.
    /// </summary>
    [Fact]
    public void Open_ReadyPlugin_ReadsTheOverlaysRecordsAsFormIdRecords()
    {
        var readParameters = new BinaryReadParameters();
        SupportedGameRelease? openedRelease = null;
        string? openedPath = null;
        BinaryReadParameters? openedParameters = null;
        var sut = new MutagenPluginReader((release, path, parameters) =>
        {
            openedRelease = release;
            openedPath = path;
            openedParameters = parameters;
            return CreateOverlay([CreateRecord("FirstNpc"), CreateRecord("SecondNpc")]);
        });

        using var opened = sut.Open(CreateReadyPlugin(GameRelease.SkyrimSE, readParameters));
        var records = opened.ReadRecords(_ => { }, CancellationToken.None).ToList();

        Assert.Same(SupportedGameReleases.ForRelease(GameRelease.SkyrimSE), openedRelease);
        Assert.Equal(PluginPath, openedPath);
        Assert.Same(readParameters, openedParameters);
        Assert.Equal(["FirstNpc", "SecondNpc"], records.Select(record => record.Entry));
        Assert.All(records, record => Assert.Matches("^[0-9A-F]{6}$", record.FormId));
    }

    /// <summary>
    ///     Verifies that opening is eager while reading is lazy: no record is enumerated until the sequence is consumed.
    /// </summary>
    [Fact]
    public void ReadRecords_SequenceNotYetConsumed_EnumeratesNothing()
    {
        var overlay = new Mock<IModDisposeGetter>();
        overlay.Setup(plugin => plugin.EnumerateMajorRecords()).Returns([CreateRecord("LazyNpc")]);
        var sut = new MutagenPluginReader((_, _, _) => overlay.Object);

        using var opened = sut.Open(CreateReadyPlugin());
        var records = opened.ReadRecords(_ => { }, CancellationToken.None);

        overlay.Verify(plugin => plugin.EnumerateMajorRecords(), Times.Never);
        Assert.Single(records);
        overlay.Verify(plugin => plugin.EnumerateMajorRecords(), Times.Once);
    }

    /// <summary>
    ///     Verifies that disposing the opened Plugin releases the overlay it was opened from.
    /// </summary>
    [Fact]
    public void Dispose_OpenedPlugin_DisposesTheOverlay()
    {
        var overlay = new Mock<IModDisposeGetter>();
        var sut = new MutagenPluginReader((_, _, _) => overlay.Object);

        sut.Open(CreateReadyPlugin()).Dispose();

        overlay.Verify(plugin => plugin.Dispose(), Times.Once);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Before opening. The capability and the Supported GameRelease lookup are checked outside the delegate, so
    // neither programming error can be normalized into a Failed Plugin by the opening classification.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Verifies a capability prepared by another adapter is rejected before any overlay is opened.
    /// </summary>
    [Fact]
    public void Open_CapabilityFromAnotherAdapter_ThrowsInvalidOperationBeforeOpening()
    {
        var delegateCalled = false;
        var sut = new MutagenPluginReader((_, _, _) =>
        {
            delegateCalled = true;
            return CreateOverlay([]);
        });
        var foreignPlugin = new SelectedPluginReady(PluginName, PluginPath, new PluginReadCapability(new object()));

        var exception = Record.Exception(() => sut.Open(foreignPlugin));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.False(delegateCalled);
    }

    /// <summary>
    ///     Verifies an unsupported GameRelease is a programming error raised before any overlay is opened, never a
    ///     Failed Plugin, whether Mutagen defines the release or not.
    /// </summary>
    [Theory]
    [InlineData(GameRelease.OblivionRE)]
    [InlineData((GameRelease)999)]
    public void Open_UnsupportedGameRelease_ThrowsArgumentOutOfRangeBeforeOpening(GameRelease release)
    {
        Assert.False(SupportedGameReleases.IsSupported(release), $"{release} is now a Supported GameRelease.");
        var delegateCalled = false;
        var sut = new MutagenPluginReader((_, _, _) =>
        {
            delegateCalled = true;
            return CreateOverlay([]);
        });

        var exception = Record.Exception(() => sut.Open(CreateReadyPlugin(release)));

        Assert.IsNotType<PluginReadException>(exception);
        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.False(delegateCalled);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Opening. One entry per family on the expected-failure list, each reached by a wrapped and an unwrapped
    // failure, so narrowing the list or the deepest-message walk fails a case by name.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     The opening failure families that make a Failed Plugin, by name.
    /// </summary>
    public static TheoryData<string> OpeningFailureFamilies =>
    [
        nameof(MalformedDataException),
        nameof(RecordException),
        nameof(ArgumentException),
        nameof(OverflowException),
        nameof(IOException),
        nameof(UnauthorizedAccessException)
    ];

    /// <summary>
    ///     Verifies each expected opening failure becomes a Failed Plugin in the opening phase carrying its message.
    /// </summary>
    [Theory]
    [MemberData(nameof(OpeningFailureFamilies))]
    public void Open_ExpectedOpeningFailure_ThrowsPluginReadFailureInTheOpeningPhase(string family)
    {
        var failure = CreateOpeningFailure(family, "The Plugin header is unreadable.");
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Assert.Throws<PluginReadException>(() => sut.Open(CreateReadyPlugin()));

        Assert.Equal(PluginReadPhase.OpeningPlugin, thrown.Phase);
        Assert.Equal("The Plugin header is unreadable.", thrown.DiagnosticMessage);
        Assert.Same(failure, thrown.InnerException);
    }

    /// <summary>
    ///     Verifies an expected opening failure wrapped by an unlisted exception still becomes a Failed Plugin, and that
    ///     the message reported is the deepest one rather than the wrapper's.
    /// </summary>
    [Theory]
    [MemberData(nameof(OpeningFailureFamilies))]
    public void Open_WrappedOpeningFailure_ThrowsPluginReadFailureWithTheDeepestMessage(string family)
    {
        var failure = new InvalidOperationException(
            "Wrapper wording.",
            new TargetInvocationException(CreateOpeningFailure(family, "Deepest wording.")));
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Assert.Throws<PluginReadException>(() => sut.Open(CreateReadyPlugin()));

        Assert.Equal(PluginReadPhase.OpeningPlugin, thrown.Phase);
        Assert.Equal("Deepest wording.", thrown.DiagnosticMessage);
        Assert.Same(failure, thrown.InnerException);
    }

    /// <summary>
    ///     Verifies an opening failure outside the expected list propagates unchanged and is not a Failed Plugin.
    /// </summary>
    [Fact]
    public void Open_UnexpectedOpeningFailure_PropagatesUnchanged()
    {
        var failure = new InvalidOperationException("Adapter state is broken.", new NullReferenceException());
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Record.Exception(() => sut.Open(CreateReadyPlugin()));

        Assert.Same(failure, thrown);
    }

    /// <summary>
    ///     Verifies a missing master Mutagen names becomes an Unresolvable Master naming the selected Plugin and that
    ///     master's file name, keeping Mutagen's exception as the inner exception.
    /// </summary>
    [Fact]
    public void Open_MissingMasterNamedByMutagen_ThrowsUnresolvableMasterNamingIt()
    {
        var failure = new MissingModException(ModKey.FromNameAndExtension("Starfield.esm"));
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Record.Exception(() => sut.Open(CreateReadyPlugin(GameRelease.Starfield)));

        var unresolvableMaster = Assert.IsType<UnresolvableMasterException>(thrown);
        Assert.Equal(PluginName, unresolvableMaster.PluginName);
        Assert.Equal("Starfield.esm", unresolvableMaster.MasterName);
        Assert.Same(failure, unresolvableMaster.InnerException);
    }

    /// <summary>
    ///     Verifies a missing master-flags lookup becomes an Unresolvable Master that names no master, because Mutagen
    ///     reports only that the lookup was absent.
    /// </summary>
    [Fact]
    public void Open_MasterFlagsLookupMissing_ThrowsUnresolvableMasterWithoutNamingAMaster()
    {
        var failure = new MissingModMappingException("No master flags lookup was supplied.");
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Record.Exception(() => sut.Open(CreateReadyPlugin(GameRelease.Starfield)));

        var unresolvableMaster = Assert.IsType<UnresolvableMasterException>(thrown);
        Assert.Equal(PluginName, unresolvableMaster.PluginName);
        Assert.Null(unresolvableMaster.MasterName);
        Assert.Same(failure, unresolvableMaster.InnerException);
    }

    /// <summary>
    ///     Verifies the Failed Plugin list is consulted before the master classification, so a master-resolution failure
    ///     wrapping a listed cause stays the Failed Plugin it has always been.
    /// </summary>
    [Fact]
    public void Open_MissingMasterWrappingAListedCause_ThrowsPluginReadFailure()
    {
        var failure = new MissingModException(
            ModKey.FromNameAndExtension("Starfield.esm"),
            "Master could not be read.",
            new IOException("Master file is locked."));
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Assert.Throws<PluginReadException>(() => sut.Open(CreateReadyPlugin(GameRelease.Starfield)));

        Assert.Equal(PluginReadPhase.OpeningPlugin, thrown.Phase);
        Assert.Equal("Master file is locked.", thrown.DiagnosticMessage);
    }

    /// <summary>
    ///     Verifies cancellation nested inside an otherwise expected opening failure is rethrown as the original
    ///     cancellation, never reported as a Failed Plugin.
    /// </summary>
    [Fact]
    public void Open_CancellationNestedInAnOpeningFailure_ThrowsTheOriginalCancellation()
    {
        var cancellation = new OperationCanceledException("Opening cancelled.");
        var sut = new MutagenPluginReader((_, _, _) => throw new IOException("Read interrupted.", cancellation));

        var thrown = Assert.ThrowsAny<OperationCanceledException>(() => sut.Open(CreateReadyPlugin()));

        Assert.Same(cancellation, thrown);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Enumeration. Only a top-level RecordException is a Failed Plugin here, a narrower rule than opening's on
    // purpose (#127); its message is the RecordException's own, not the deepest one.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Verifies a record-read failure raised midway through enumeration becomes a Failed Plugin in the
    ///     reading-records phase, after the records before it were yielded.
    /// </summary>
    [Fact]
    public void ReadRecords_RecordExceptionWhileEnumerating_ThrowsPluginReadFailureInTheReadingPhase()
    {
        var failure = CreateRecordException("Invalid record data.");
        var sut = new MutagenPluginReader((_, _, _) =>
            CreateOverlay(YieldThenThrow(CreateRecord("BeforeFailure"), failure)));
        using var opened = sut.Open(CreateReadyPlugin());
        var yielded = new List<string>();

        var thrown = Assert.Throws<PluginReadException>(() =>
        {
            foreach (var record in opened.ReadRecords(_ => { }, CancellationToken.None))
            {
                yielded.Add(record.Entry);
            }
        });

        Assert.Equal(["BeforeFailure"], yielded);
        Assert.Equal(PluginReadPhase.ReadingRecords, thrown.Phase);
        Assert.Equal("Invalid record data.", thrown.DiagnosticMessage);
        Assert.Same(failure, thrown.InnerException);
    }

    /// <summary>
    ///     Verifies a record-read failure raised as enumeration begins is classified the same way.
    /// </summary>
    [Fact]
    public void ReadRecords_RecordExceptionStartingEnumeration_ThrowsPluginReadFailureInTheReadingPhase()
    {
        var failure = CreateRecordException("Group header is invalid.");
        var overlay = new Mock<IModDisposeGetter>();
        overlay.Setup(plugin => plugin.EnumerateMajorRecords()).Throws(failure);
        var sut = new MutagenPluginReader((_, _, _) => overlay.Object);
        using var opened = sut.Open(CreateReadyPlugin());

        var thrown = Assert.Throws<PluginReadException>(() =>
            opened.ReadRecords(_ => { }, CancellationToken.None).ToList());

        Assert.Equal(PluginReadPhase.ReadingRecords, thrown.Phase);
        Assert.Equal("Group header is invalid.", thrown.DiagnosticMessage);
        Assert.Same(failure, thrown.InnerException);
    }

    /// <summary>
    ///     Verifies an enumeration failure other than a top-level <see cref="RecordException" /> propagates unchanged,
    ///     including one that merely wraps a <see cref="RecordException" />.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadRecords_UnexpectedEnumerationFailure_PropagatesUnchanged(bool wrapsARecordException)
    {
        Exception failure = wrapsARecordException
            ? new InvalidOperationException("Enumerator broke.", CreateRecordException("Inner record failure."))
            : new IOException("Record enumeration infrastructure failed.");
        var sut = new MutagenPluginReader((_, _, _) =>
            CreateOverlay(YieldThenThrow(CreateRecord("BeforeFailure"), failure)));
        using var opened = sut.Open(CreateReadyPlugin());

        var thrown = Record.Exception(() => opened.ReadRecords(_ => { }, CancellationToken.None).ToList());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    ///     Verifies cancellation that Mutagen wraps in a <see cref="RecordException" /> while enumerating is rethrown as
    ///     the original cancellation, never reported as a Failed Plugin.
    /// </summary>
    [Fact]
    public void ReadRecords_CancellationNestedInARecordException_ThrowsTheOriginalCancellation()
    {
        var cancellation = new OperationCanceledException("Record read cancelled.");
        var sut = new MutagenPluginReader((_, _, _) =>
            CreateOverlay(YieldThenThrow(CreateRecord("BeforeFailure"), CreateRecordException(cancellation))));
        using var opened = sut.Open(CreateReadyPlugin());

        var thrown = Assert.ThrowsAny<OperationCanceledException>(() =>
            opened.ReadRecords(_ => { }, CancellationToken.None).ToList());

        Assert.Same(cancellation, thrown);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Per record.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Verifies the token is checked per record: a cancellation requested after one record stops reading before
    ///     the next one is extracted.
    /// </summary>
    [Fact]
    public void ReadRecords_CancellationRequestedBetweenRecords_StopsBeforeTheNextRecord()
    {
        using var cancellationSource = new CancellationTokenSource();
        var sut = new MutagenPluginReader((_, _, _) =>
            CreateOverlay([CreateRecord("FirstNpc"), CreateRecord("SecondNpc")]));
        using var opened = sut.Open(CreateReadyPlugin());
        var yielded = new List<string>();

        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            foreach (var record in opened.ReadRecords(_ => { }, cancellationSource.Token))
            {
                yielded.Add(record.Entry);
                cancellationSource.Cancel();
            }
        });

        Assert.Equal(["FirstNpc"], yielded);
    }

    /// <summary>
    ///     Verifies cancellation nested inside a failure raised by one record's getter is rethrown as the original
    ///     cancellation, never reported as a record issue.
    /// </summary>
    [Fact]
    public void ReadRecords_CancellationNestedInARecordGetterFailure_ThrowsTheOriginalCancellation()
    {
        var cancellation = new OperationCanceledException("Record getter cancelled.");
        var record = new Mock<IMajorRecordGetter>();
        record.SetupGet(candidate => candidate.FormKey).Throws(new TargetInvocationException(cancellation));
        var sut = new MutagenPluginReader((_, _, _) => CreateOverlay([record.Object]));
        using var opened = sut.Open(CreateReadyPlugin());
        var issues = new List<string>();

        var thrown = Assert.ThrowsAny<OperationCanceledException>(() =>
            opened.ReadRecords(issues.Add, CancellationToken.None).ToList());

        Assert.Same(cancellation, thrown);
        Assert.Empty(issues);
    }

    /// <summary>
    ///     Verifies recoverable record issues reach the callback in observation order with ignorable ones filtered out:
    ///     a failed FormID drops its record, and a failed Entry falls back to the Synthesized Entry.
    /// </summary>
    [Fact]
    public void ReadRecords_RecoverableRecordIssues_ReachTheCallbackInOrderWithoutIgnorableOnes()
    {
        var sut = new MutagenPluginReader((_, _, _) => CreateOverlay(
        [
            CreateRecordWithUnreadableFormId("first issue"),
            CreateRecordWithUnreadableFormId("Unexpected record type in group."),
            CreateRecordWithUnreadableEditorId(0x000123, "second issue"),
            CreateRecord("ReadableNpc"),
            CreateRecordWithUnreadableFormId("third issue")
        ]));
        using var opened = sut.Open(CreateReadyPlugin());
        var issues = new List<string>();

        var records = opened.ReadRecords(issues.Add, CancellationToken.None).ToList();

        Assert.Equal(["first issue", "second issue", "third issue"], issues);
        Assert.Collection(
            records,
            synthesized =>
            {
                Assert.Equal("000123", synthesized.FormId);
                Assert.StartsWith("[", synthesized.Entry, StringComparison.Ordinal);
                Assert.EndsWith("_000123]", synthesized.Entry, StringComparison.Ordinal);
            },
            readable => Assert.Equal("ReadableNpc", readable.Entry));
    }

    /// <summary>
    ///     Verifies the callback is required up front rather than when the lazy sequence is first consumed.
    /// </summary>
    [Fact]
    public void ReadRecords_NullCallback_ThrowsBeforeEnumeration()
    {
        var sut = new MutagenPluginReader((_, _, _) => CreateOverlay([]));
        using var opened = sut.Open(CreateReadyPlugin());

        Assert.Throws<ArgumentNullException>(() => opened.ReadRecords(null!, CancellationToken.None));
    }

    /// <summary>
    ///     Creates a record whose FormKey getter fails with the given message, so Entry Extraction drops it.
    /// </summary>
    private static IMajorRecordGetter CreateRecordWithUnreadableFormId(string message)
    {
        var record = new Mock<IMajorRecordGetter>();
        record.SetupGet(candidate => candidate.FormKey).Throws(new InvalidDataException(message));
        return record.Object;
    }

    /// <summary>
    ///     Creates a record with a readable FormID whose EditorID getter fails with the given message, so Entry
    ///     Extraction falls back to its Synthesized Entry.
    /// </summary>
    private static IMajorRecordGetter CreateRecordWithUnreadableEditorId(uint formId, string message)
    {
        var record = new Mock<IMajorRecordGetter>();
        record
            .SetupGet(candidate => candidate.FormKey)
            .Returns(new FormKey(ModKey.FromNameAndExtension(PluginName), formId));
        record.SetupGet(candidate => candidate.EditorID).Throws(new InvalidDataException(message));
        return record.Object;
    }

    /// <summary>
    ///     Yields one record and then raises the given failure, as a lazily parsed overlay does midway through.
    /// </summary>
    private static IEnumerable<IMajorRecordGetter> YieldThenThrow(IMajorRecordGetter record, Exception exception)
    {
        yield return record;
        throw exception;
    }

    /// <summary>
    ///     Creates a Mutagen record-read failure carrying the given message.
    /// </summary>
    private static RecordException CreateRecordException(string message)
    {
        return new RecordException(
            formKey: null,
            recordType: null,
            modKey: ModKey.FromNameAndExtension(PluginName),
            edid: null,
            message: message);
    }

    /// <summary>
    ///     Creates a Mutagen record-read failure wrapping the given cause.
    /// </summary>
    private static RecordException CreateRecordException(Exception innerException)
    {
        return new RecordException(
            formKey: null,
            recordType: null,
            modKey: ModKey.FromNameAndExtension(PluginName),
            edid: null,
            innerException: innerException);
    }

    /// <summary>
    ///     Creates one exception of an expected opening failure family.
    /// </summary>
    /// <param name="family">The family's type name.</param>
    /// <param name="message">The exception's own message.</param>
    private static Exception CreateOpeningFailure(string family, string message)
    {
        return family switch
        {
            nameof(MalformedDataException) => new MalformedDataException(message),
            nameof(RecordException) => new RecordException(
                formKey: null,
                recordType: null,
                modKey: ModKey.FromNameAndExtension(PluginName),
                edid: null,
                message: message),
            nameof(ArgumentException) => new ArgumentException(message),
            nameof(OverflowException) => new OverflowException(message),
            nameof(IOException) => new IOException(message),
            nameof(UnauthorizedAccessException) => new UnauthorizedAccessException(message),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown opening failure family.")
        };
    }

    /// <summary>
    ///     Creates one record in a throwaway Skyrim Plugin, whose EditorID becomes its Entry.
    /// </summary>
    /// <param name="editorId">The record's EditorID.</param>
    /// <returns>An in-memory major record.</returns>
    private static IMajorRecordGetter CreateRecord(string editorId)
    {
        var sourcePlugin = new SkyrimMod(ModKey.FromNameAndExtension("Source.esp"), SkyrimRelease.SkyrimSE);
        return sourcePlugin.Npcs.AddNew(editorId);
    }

    /// <summary>
    ///     Creates a Moq'd overlay whose enumeration yields the given records.
    /// </summary>
    private static IModDisposeGetter CreateOverlay(IEnumerable<IMajorRecordGetter> records)
    {
        var overlay = new Mock<IModDisposeGetter>();
        overlay.Setup(plugin => plugin.EnumerateMajorRecords()).Returns(records);
        return overlay.Object;
    }

    /// <summary>
    ///     Creates a ready selected Plugin carrying a production-shaped capability.
    /// </summary>
    /// <param name="release">The GameRelease hidden inside the capability.</param>
    /// <param name="readParameters">The prepared read parameters, or the defaults.</param>
    private static SelectedPluginReady CreateReadyPlugin(
        GameRelease release = GameRelease.SkyrimSE,
        BinaryReadParameters? readParameters = null)
    {
        var capability = new PluginReadCapability(
            new MutagenPluginReadCapabilityPayload(release, readParameters ?? BinaryReadParameters.Default));
        return new SelectedPluginReady(PluginName, PluginPath, capability);
    }
}
