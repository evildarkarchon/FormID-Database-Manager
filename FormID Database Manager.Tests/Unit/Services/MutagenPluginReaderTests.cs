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
///     overlays standing in for Plugins that would otherwise need real broken files, and then through its production
///     overlay factory against real paths, as proof that the same rules reach real Mutagen calls.
/// </summary>
/// <remarks>
///     The real-path cases moved here from the retired overlay adapter's tests when ADR-0015 deleted that adapter.
/// </remarks>
public sealed class MutagenPluginReaderTests : IDisposable
{
    private const string PluginName = "Tested.esp";
    private const string PluginPath = @"C:\Data\Tested.esp";

    /// <summary>
    ///     The message every test-built wrapper carries, distinct from any cause's, so a diagnostic that reports it
    ///     instead of the classified cause's message fails by name.
    /// </summary>
    private const string WrapperWording = "Wrapper wording.";

    /// <summary>
    ///     A GameRelease Mutagen defines that this application deliberately does not support.
    /// </summary>
    /// <remarks>
    ///     Named rather than derived so the test reads concretely, but every real-path use goes through
    ///     <see cref="AssertNotInTheSupportedTable" /> first: if this release is ever added to the table, the tests
    ///     using it fail with a message saying so instead of silently exercising a supported release. That keeps this
    ///     from becoming a stale copy of the table's own exclusion list.
    /// </remarks>
    private const GameRelease DeliberatelyUnsupportedRelease = GameRelease.OblivionRE;

    /// <summary>
    ///     An enum value Mutagen does not define at all, which the table treats by the same one rule.
    /// </summary>
    private const GameRelease UndefinedRelease = (GameRelease)999;

    private readonly IPluginReader _productionReader = new MutagenPluginReader();
    private readonly string _testDirectory;

    public MutagenPluginReaderTests()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"MutagenPluginReaderTests_{Guid.NewGuid():N}");
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
    // Opening. One entry per family on the Plugin-data family list, each raised directly and behind each of
    // Mutagen's enrichment wrappers, so narrowing the list or the look-through fails a case by name (ADR-0016).
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     The Plugin-data failure families that make a Failed Plugin, by name. <see cref="RecordException" /> stands
    ///     for the bare one Mutagen raises itself, with no inner cause.
    /// </summary>
    private static readonly string[] PluginDataFailureFamilyNames =
    [
        nameof(MalformedDataException),
        nameof(RecordException),
        nameof(ArgumentException),
        nameof(OverflowException),
        nameof(IOException),
        nameof(UnauthorizedAccessException),
        nameof(InvalidDataException),
        nameof(IndexOutOfRangeException),
        nameof(RecordCollisionException)
    ];

    /// <summary>
    ///     Mutagen's enrichment wrappers, which the classification looks through to the cause rather than matching.
    /// </summary>
    private static readonly string[] EnrichmentWrapperNames =
    [
        nameof(RecordException),
        nameof(SubrecordException),
        nameof(ModGroupsMalformedException)
    ];

    /// <summary>
    ///     Every Plugin-data failure family, by name.
    /// </summary>
    public static TheoryData<string> PluginDataFailureFamilies => [.. PluginDataFailureFamilyNames];

    /// <summary>
    ///     Every enrichment wrapper, by name.
    /// </summary>
    public static TheoryData<string> EnrichmentWrappers => [.. EnrichmentWrapperNames];

    /// <summary>
    ///     Every Plugin-data family behind every enrichment wrapper.
    /// </summary>
    public static TheoryData<string, string> PluginDataFailureFamiliesBehindEachEnrichmentWrapper
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var family in PluginDataFailureFamilyNames)
            {
                foreach (var wrapper in EnrichmentWrapperNames)
                {
                    data.Add(family, wrapper);
                }
            }

            return data;
        }
    }

    /// <summary>
    ///     Verifies each Plugin-data failure raised directly becomes a Failed Plugin in the opening phase carrying its
    ///     own message.
    /// </summary>
    [Theory]
    [MemberData(nameof(PluginDataFailureFamilies))]
    public void Open_PluginDataFailure_ThrowsPluginReadFailureInTheOpeningPhase(string family)
    {
        var failure = CreatePluginDataFailure(family, "The Plugin header is unreadable.");
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Assert.Throws<PluginReadException>(() => sut.Open(CreateReadyPlugin()));

        Assert.Equal(PluginReadPhase.OpeningPlugin, thrown.Phase);
        Assert.Equal(failure.Message, thrown.DiagnosticMessage);
        Assert.Same(failure, thrown.InnerException);
    }

    /// <summary>
    ///     Verifies each Plugin-data failure behind each enrichment wrapper becomes a Failed Plugin in the opening phase,
    ///     and that its diagnostic is the listed cause's message rather than the wrapper's (ADR-0016 rules 3 and 6).
    /// </summary>
    [Theory]
    [MemberData(nameof(PluginDataFailureFamiliesBehindEachEnrichmentWrapper))]
    public void Open_PluginDataFailureBehindAnEnrichmentWrapper_ThrowsPluginReadFailureWithTheCausesMessage(
        string family,
        string wrapper)
    {
        var cause = CreatePluginDataFailure(family, "Cause wording.");
        var failure = Wrap(wrapper, cause);
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Assert.Throws<PluginReadException>(() => sut.Open(CreateReadyPlugin()));

        Assert.Equal(PluginReadPhase.OpeningPlugin, thrown.Phase);
        Assert.NotEqual(WrapperWording, cause.Message);
        Assert.Equal(cause.Message, thrown.DiagnosticMessage);
        Assert.Same(failure, thrown.InnerException);
    }

    /// <summary>
    ///     Verifies a listed cause behind Mutagen's wrappers stacked as opening stacks them is still found, the whole
    ///     original chain is kept, and the message is the cause's, not that of any wrapper.
    /// </summary>
    [Fact]
    public void Open_PluginDataFailureBehindStackedEnrichmentWrappers_ThrowsPluginReadFailureWithTheCausesMessage()
    {
        var failure = Wrap(
            nameof(ModGroupsMalformedException),
            Wrap(nameof(RecordException), Wrap(nameof(SubrecordException), new IOException("Cause wording."))));
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Assert.Throws<PluginReadException>(() => sut.Open(CreateReadyPlugin()));

        Assert.Equal("Cause wording.", thrown.DiagnosticMessage);
        Assert.Same(failure, thrown.InnerException);
    }

    /// <summary>
    ///     Verifies the diagnostic is the classified cause's message and no longer the deepest one in the chain: a
    ///     listed cause with an inner exception of its own reports its own message (ADR-0016 rule 6).
    /// </summary>
    [Fact]
    public void Open_ListedCauseWithAnInnerException_ReportsTheListedCausesMessageNotTheDeepest()
    {
        var failure = Wrap(
            nameof(RecordException),
            new IOException("Cause wording.", new InvalidOperationException("Deepest wording.")));
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Assert.Throws<PluginReadException>(() => sut.Open(CreateReadyPlugin()));

        Assert.Equal("Cause wording.", thrown.DiagnosticMessage);
    }

    /// <summary>
    ///     Verifies a bare <see cref="SubrecordException" />, which Mutagen raises itself for an unreadable subrecord,
    ///     is a bare <see cref="RecordException" /> and so a Failed Plugin carrying its own message.
    /// </summary>
    [Fact]
    public void Open_BareSubrecordException_ThrowsPluginReadFailureWithItsOwnMessage()
    {
        var failure = SubrecordException.Create("Subrecord is truncated.", new RecordType("EDID"));
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Assert.Throws<PluginReadException>(() => sut.Open(CreateReadyPlugin()));

        Assert.Equal(PluginReadPhase.OpeningPlugin, thrown.Phase);
        Assert.Equal("Subrecord is truncated.", thrown.DiagnosticMessage);
        Assert.Same(failure, thrown.InnerException);
    }

    /// <summary>
    ///     Verifies an opening failure outside the expected list fails the Processing Run as an unrecognized Plugin-read
    ///     failure that names the Plugin and the opening phase and keeps the original exception, rather than becoming a
    ///     Failed Plugin or propagating without the Plugin's name (ADR-0016 rule 7).
    /// </summary>
    [Fact]
    public void Open_UnexpectedOpeningFailure_ThrowsUnrecognizedPluginReadFailureNamingThePlugin()
    {
        var failure = new InvalidOperationException("Adapter state is broken.", new NullReferenceException());
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Record.Exception(() => sut.Open(CreateReadyPlugin()));

        var unrecognized = Assert.IsType<UnrecognizedPluginReadFailureException>(thrown);
        Assert.Equal(PluginName, unrecognized.PluginName);
        Assert.Equal(PluginReadPhase.OpeningPlugin, unrecognized.Phase);
        Assert.Same(failure, unrecognized.InnerException);
    }

    /// <summary>
    ///     Verifies an unlisted cause behind each enrichment wrapper fails the run as an unrecognized Plugin-read
    ///     failure naming the Plugin, rather than becoming a Failed Plugin because Mutagen enriched it (ADR-0016 rule 3).
    /// </summary>
    [Theory]
    [MemberData(nameof(EnrichmentWrappers))]
    public void Open_UnlistedCauseBehindAnEnrichmentWrapper_ThrowsUnrecognizedPluginReadFailureNamingThePlugin(
        string wrapper)
    {
        var failure = Wrap(wrapper, new NullReferenceException("Mutagen defect."));
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Record.Exception(() => sut.Open(CreateReadyPlugin()));

        var unrecognized = Assert.IsType<UnrecognizedPluginReadFailureException>(thrown);
        Assert.Equal(PluginName, unrecognized.PluginName);
        Assert.Equal(PluginReadPhase.OpeningPlugin, unrecognized.Phase);
        Assert.Same(failure, unrecognized.InnerException);
    }

    /// <summary>
    ///     Verifies only Mutagen's enrichment wrappers are looked through: a listed cause wrapped by an exception the
    ///     application does not recognise is classified by that wrapper, which is the cause here, and fails the run.
    /// </summary>
    [Fact]
    public void Open_ListedCauseBehindAnUnrecognizedWrapper_ThrowsUnrecognizedPluginReadFailure()
    {
        var failure = Wrap(nameof(InvalidOperationException), new IOException("Cause wording."));
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Record.Exception(() => sut.Open(CreateReadyPlugin()));

        var unrecognized = Assert.IsType<UnrecognizedPluginReadFailureException>(thrown);
        Assert.Same(failure, unrecognized.InnerException);
    }

    /// <summary>
    ///     Verifies a <see cref="ModGroupsMalformedException" /> with no cause is not a Failed Plugin: unlike a bare
    ///     <see cref="RecordException" />, it is only ever an enrichment of some other failure, so with nothing behind it
    ///     there is no Plugin-data cause to classify.
    /// </summary>
    [Fact]
    public void Open_ModGroupsMalformedWithoutACause_ThrowsUnrecognizedPluginReadFailure()
    {
        var failure = new ModGroupsMalformedException(ModKey.FromNameAndExtension(PluginName), "Groups malformed.");
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Record.Exception(() => sut.Open(CreateReadyPlugin()));

        Assert.IsType<UnrecognizedPluginReadFailureException>(thrown);
    }

    /// <summary>
    ///     Verifies cancellation nested in an otherwise unrecognized opening failure is rethrown as the original
    ///     cancellation, so it is never reported as a failure of the Plugin or the run.
    /// </summary>
    [Fact]
    public void Open_CancellationNestedInAnUnrecognizedOpeningFailure_ThrowsTheOriginalCancellation()
    {
        var cancellation = new OperationCanceledException("Opening cancelled.");
        var sut = new MutagenPluginReader((_, _, _) =>
            throw new InvalidOperationException("Adapter state is broken.", cancellation));

        var thrown = Assert.ThrowsAny<OperationCanceledException>(() => sut.Open(CreateReadyPlugin()));

        Assert.Same(cancellation, thrown);
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
    ///     Verifies the master classification runs before the Plugin-data family list, so a master-resolution failure
    ///     wrapping a listed cause is an Unresolvable Master naming its master, never a Failed Plugin (ADR-0016 rule 1).
    /// </summary>
    [Fact]
    public void Open_MissingMasterWrappingAListedCause_ThrowsUnresolvableMasterNamingIt()
    {
        var failure = new MissingModException(
            ModKey.FromNameAndExtension("Starfield.esm"),
            "Master could not be read.",
            new IOException("Master file is locked."));
        var sut = new MutagenPluginReader((_, _, _) => throw failure);

        var thrown = Record.Exception(() => sut.Open(CreateReadyPlugin(GameRelease.Starfield)));

        var unresolvableMaster = Assert.IsType<UnresolvableMasterException>(thrown);
        Assert.Equal(PluginName, unresolvableMaster.PluginName);
        Assert.Equal("Starfield.esm", unresolvableMaster.MasterName);
        Assert.Same(failure, unresolvableMaster.InnerException);
    }

    /// <summary>
    ///     The wrappers a master-resolution failure can sit behind: each Mutagen enrichment wrapper, a listed Plugin-data
    ///     cause, and an exception the application does not recognise. The master check must see through all of them.
    /// </summary>
    public static TheoryData<string> MasterFailureWrappers =>
    [
        nameof(RecordException),
        nameof(SubrecordException),
        nameof(ModGroupsMalformedException),
        nameof(IOException),
        nameof(InvalidOperationException)
    ];

    /// <summary>
    ///     Verifies a named missing master found anywhere in the inner-exception chain fails the run as an Unresolvable
    ///     Master naming that master, keeping Mutagen's master exception as the inner exception (ADR-0016 rule 1).
    /// </summary>
    [Theory]
    [MemberData(nameof(MasterFailureWrappers))]
    public void Open_MissingMasterWrappedInTheChain_ThrowsUnresolvableMasterNamingIt(string wrapper)
    {
        var missingMaster = new MissingModException(ModKey.FromNameAndExtension("Starfield.esm"));
        var sut = new MutagenPluginReader((_, _, _) => throw Wrap(wrapper, missingMaster));

        var thrown = Record.Exception(() => sut.Open(CreateReadyPlugin(GameRelease.Starfield)));

        var unresolvableMaster = Assert.IsType<UnresolvableMasterException>(thrown);
        Assert.Equal(PluginName, unresolvableMaster.PluginName);
        Assert.Equal("Starfield.esm", unresolvableMaster.MasterName);
        Assert.Same(missingMaster, unresolvableMaster.InnerException);
    }

    /// <summary>
    ///     Verifies a missing master-flags lookup found anywhere in the inner-exception chain fails the run as an
    ///     Unresolvable Master naming no master (ADR-0016 rule 1).
    /// </summary>
    [Theory]
    [MemberData(nameof(MasterFailureWrappers))]
    public void Open_MasterFlagsLookupMissingWrappedInTheChain_ThrowsUnresolvableMasterWithoutNamingAMaster(
        string wrapper)
    {
        var missingMapping = new MissingModMappingException("No master flags lookup was supplied.");
        var sut = new MutagenPluginReader((_, _, _) => throw Wrap(wrapper, missingMapping));

        var thrown = Record.Exception(() => sut.Open(CreateReadyPlugin(GameRelease.Starfield)));

        var unresolvableMaster = Assert.IsType<UnresolvableMasterException>(thrown);
        Assert.Equal(PluginName, unresolvableMaster.PluginName);
        Assert.Null(unresolvableMaster.MasterName);
        Assert.Same(missingMapping, unresolvableMaster.InnerException);
    }

    /// <summary>
    ///     Verifies cancellation is rethrown before the master classification, so a cancelled read that Mutagen happened
    ///     to report through a master-resolution failure is still a cancellation.
    /// </summary>
    [Fact]
    public void Open_CancellationNestedInAMissingMaster_ThrowsTheOriginalCancellation()
    {
        var cancellation = new OperationCanceledException("Opening cancelled.");
        var sut = new MutagenPluginReader((_, _, _) => throw new MissingModException(
            ModKey.FromNameAndExtension("Starfield.esm"),
            "Master read cancelled.",
            cancellation));

        var thrown = Assert.ThrowsAny<OperationCanceledException>(() =>
            sut.Open(CreateReadyPlugin(GameRelease.Starfield)));

        Assert.Same(cancellation, thrown);
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
    // purpose (#127); its message is the RecordException's own, not the classified cause's that opening reports.
    // Enumeration moves onto the shared fault-scope classification in #138.
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

    // ---------------------------------------------------------------------------------------------------------
    // Real paths. These drive the production reader, with its real overlay factory, against a path that does not
    // exist, a file holding junk bytes, or a directory. No Plugin fixture is generated: Mutagen walks a Plugin's
    // mod header and top-level groups eagerly while constructing the overlay, so a malformed file fails at
    // opening rather than at first enumeration. Mutagen raises no nested chain at that point, so these pin message
    // forwarding and each family's real trigger, while the delegate-fed cases above pin the look-through to the
    // cause behind Mutagen's enrichment wrappers and nested cancellation. The happy path and the master-resolution boundary need a generated Plugin, so they
    // live in PluginOverlayConstructionTests.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Every Supported GameRelease, so a row added to the table inherits this file's failure coverage rather than
    ///     shipping untested.
    /// </summary>
    public static TheoryData<GameRelease> SupportedReleases =>
        [.. SupportedGameReleases.All.Select(row => row.Release)];

    // ---------------------------------------------------------------------------------------------------------
    // Unsupported GameRelease. The lookup sits outside the adapter's failure-handling block on purpose, and these
    // are the tests that pin it there: the fixture below is a Plugin that would itself fail to open, so if the
    // lookup moved inside the try, its ArgumentOutOfRangeException would be caught by the Plugin-read classification
    // (whose family list treats ArgumentException, its base type, as a malformed-Plugin signal) and reported to the user as a
    // Failed Plugin instead of surfacing as the programming error it is (ADR-0003).
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Verifies a Mutagen-defined but deliberately unsupported release fails as a programming error naming the
    ///     release, and not as a Plugin-read failure.
    /// </summary>
    [Fact]
    public void Open_DeliberatelyUnsupportedRelease_ThrowsUnsupportedReleaseRatherThanPluginReadFailure()
    {
        AssertNotInTheSupportedTable(DeliberatelyUnsupportedRelease);
        var malformedPlugin = WriteMalformedPlugin("Unsupported.esp");

        var exception = Record.Exception(() =>
            _productionReader.Open(CreateReadyPluginAt(malformedPlugin, DeliberatelyUnsupportedRelease)));

        // The "not a Failed Plugin" assertion comes first and is deliberately subsumed by the type assertion below.
        // It is the one that fires if the lookup ever moves inside the try, and it names the actual regression —
        // a programming error reaching the user as a Failed Plugin — rather than reporting a bare type mismatch.
        Assert.IsNotType<PluginReadException>(exception);
        var unsupported = Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Contains(DeliberatelyUnsupportedRelease.ToString(), unsupported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies an entirely undefined GameRelease value follows the same rule, so there is no third category of
    ///     unsupported release for a caller to reason about.
    /// </summary>
    [Fact]
    public void Open_UndefinedGameReleaseValue_ThrowsUnsupportedReleaseRatherThanPluginReadFailure()
    {
        AssertNotInTheSupportedTable(UndefinedRelease);
        var malformedPlugin = WriteMalformedPlugin("Undefined.esp");

        var exception = Record.Exception(() =>
            _productionReader.Open(CreateReadyPluginAt(malformedPlugin, UndefinedRelease)));

        // Ordered for the same reason as the deliberately-unsupported case above.
        Assert.IsNotType<PluginReadException>(exception);
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>
    ///     Verifies the release is rejected before the Plugin path is consulted at all, so an unsupported release
    ///     cannot be masked by — or mistaken for — a problem with the file.
    /// </summary>
    [Fact]
    public void Open_UnsupportedReleaseWithMissingPlugin_StillReportsTheUnsupportedRelease()
    {
        var missingPlugin = Path.Combine(_testDirectory, "Missing.esp");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _productionReader.Open(CreateReadyPluginAt(missingPlugin, DeliberatelyUnsupportedRelease)));
    }

    // ---------------------------------------------------------------------------------------------------------
    // Failure normalization. Each case below pins one entry of the adapter's Plugin-data family list by the input
    // that reaches it, never by inspecting the list itself: narrowing the list later fails one of these by name.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Verifies a Plugin whose file has gone missing becomes an expected Plugin-read failure — the filesystem
    ///     branch of the classification list — rather than escaping and aborting the Processing Run.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedReleases))]
    public void Open_MissingPluginFile_ThrowsPluginReadFailure(GameRelease release)
    {
        var missingPlugin = Path.Combine(_testDirectory, "Missing.esp");

        var exception = Assert.Throws<PluginReadException>(() =>
            _productionReader.Open(CreateReadyPluginAt(missingPlugin, release)));

        Assert.Equal(PluginReadPhase.OpeningPlugin, exception.Phase);
        Assert.IsType<FileNotFoundException>(exception.InnerException);
    }

    /// <summary>
    ///     Verifies a Plugin holding bytes that are not a valid mod header becomes an expected Plugin-read failure —
    ///     the malformed-data branch — so a corrupt download costs the user one Plugin rather than the whole run.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedReleases))]
    public void Open_MalformedPluginBytes_ThrowsPluginReadFailure(GameRelease release)
    {
        var malformedPlugin = WriteMalformedPlugin("Malformed.esp");

        var exception = Assert.Throws<PluginReadException>(() =>
            _productionReader.Open(CreateReadyPluginAt(malformedPlugin, release)));

        Assert.Equal(PluginReadPhase.OpeningPlugin, exception.Phase);
        Assert.IsType<MalformedDataException>(exception.InnerException);
    }

    /// <summary>
    ///     Verifies an empty Plugin file is malformed data rather than an empty-but-valid Plugin, so a truncated
    ///     download does not silently contribute zero records.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedReleases))]
    public void Open_EmptyPluginFile_ThrowsPluginReadFailure(GameRelease release)
    {
        var emptyPlugin = Path.Combine(_testDirectory, "Empty.esp");
        File.WriteAllBytes(emptyPlugin, []);

        var exception = Assert.Throws<PluginReadException>(() =>
            _productionReader.Open(CreateReadyPluginAt(emptyPlugin, release)));

        Assert.Equal(PluginReadPhase.OpeningPlugin, exception.Phase);
        Assert.IsType<MalformedDataException>(exception.InnerException);
    }

    /// <summary>
    ///     Verifies a Plugin path the operating system refuses to open becomes an expected Plugin-read failure, so a
    ///     Plugin the user cannot read is recoverable rather than fatal to the Processing Run.
    /// </summary>
    /// <remarks>
    ///     The refused path is a <em>directory</em> named like a Plugin: the name parses as a ModKey, so Mutagen gets
    ///     past ModKey construction and attempts the open, which Windows refuses with
    ///     <see cref="UnauthorizedAccessException" />. That is the same exception — and so the same classification
    ///     branch — a permission-denied file produces, reached without ACLs or elevation, which is what keeps this
    ///     test deterministic and environment-independent. It stands in for a denied file; it is not one.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SupportedReleases))]
    public void Open_PluginPathTheOperatingSystemRefusesToOpen_ThrowsPluginReadFailure(GameRelease release)
    {
        var unreadablePlugin = Path.Combine(_testDirectory, "Refused.esp");
        Directory.CreateDirectory(unreadablePlugin);

        var exception = Assert.Throws<PluginReadException>(() =>
            _productionReader.Open(CreateReadyPluginAt(unreadablePlugin, release)));

        Assert.Equal(PluginReadPhase.OpeningPlugin, exception.Phase);
        Assert.IsType<UnauthorizedAccessException>(exception.InnerException);
    }

    /// <summary>
    ///     Verifies a path Mutagen cannot even read as a Plugin name becomes an expected Plugin-read failure — the
    ///     <see cref="ArgumentException" /> branch, which is the one the unsupported-release lookup is kept clear of.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedReleases))]
    public void Open_PathThatIsNotAPluginName_ThrowsPluginReadFailure(GameRelease release)
    {
        var notAPluginName = Path.Combine(_testDirectory, "NotAPluginName");

        // Created so the path exists: without it a missing-file failure could produce the same verdict for the wrong
        // reason, and this case is meant to pin the name-parsing branch specifically.
        Directory.CreateDirectory(notAPluginName);

        var exception = Assert.Throws<PluginReadException>(() =>
            _productionReader.Open(CreateReadyPluginAt(notAPluginName, release)));

        Assert.Equal(PluginReadPhase.OpeningPlugin, exception.Phase);
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Message normalization.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Verifies the Failed Plugin message a user reads is the underlying cause verbatim — here, the missing path —
    ///     rather than a message the adapter authored, so a missing file is distinguishable from a corrupt one.
    /// </summary>
    [Fact]
    public void Open_MissingPluginFile_MessageIsTheUnderlyingCause()
    {
        var missingPlugin = Path.Combine(_testDirectory, "Missing.esp");

        var exception = Assert.Throws<PluginReadException>(() =>
            _productionReader.Open(CreateReadyPluginAt(missingPlugin, GameRelease.SkyrimSE)));

        // Asserted against the cause's own message, not against a test-side re-implementation of the adapter's
        // unwrapping rule — a helper that walked the chain the same way production does would agree with a wrong
        // policy as readily as a right one. A real overlay raises no nested chain here, so the walk itself is
        // pinned by Open_PluginDataFailureBehindAnEnrichmentWrapper_ThrowsPluginReadFailureWithTheCausesMessage instead.
        Assert.Contains(missingPlugin, exception.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Equal(exception.InnerException!.Message, exception.DiagnosticMessage);
    }

    /// <summary>
    ///     Verifies a malformed Plugin surfaces Mutagen's own parse-failure message, which names what could not be
    ///     read, rather than the same generic wording every failure would otherwise share.
    /// </summary>
    [Fact]
    public void Open_MalformedPluginBytes_MessageIsTheUnderlyingCause()
    {
        var malformedPlugin = WriteMalformedPlugin("MalformedMessage.esp");

        var exception = Assert.Throws<PluginReadException>(() =>
            _productionReader.Open(CreateReadyPluginAt(malformedPlugin, GameRelease.SkyrimSE)));

        // Mutagen's own wording, pinned on purpose: this message is what a user reads on a Failed Plugin, so a
        // Mutagen upgrade that reworded it is a user-visible change worth failing on.
        Assert.Contains("Mod Header", exception.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Equal(exception.InnerException!.Message, exception.DiagnosticMessage);
    }

    /// <summary>
    ///     Verifies two different underlying causes do not collapse to one message, which is what makes a Failed
    ///     Plugin message worth reading.
    /// </summary>
    [Fact]
    public void Open_DifferentUnderlyingCauses_ProduceDifferentMessages()
    {
        var missingPlugin = Path.Combine(_testDirectory, "MissingDistinct.esp");
        var malformedPlugin = WriteMalformedPlugin("MalformedDistinct.esp");

        var missingFailure = Assert.Throws<PluginReadException>(() =>
            _productionReader.Open(CreateReadyPluginAt(missingPlugin, GameRelease.SkyrimSE)));
        var malformedFailure = Assert.Throws<PluginReadException>(() =>
            _productionReader.Open(CreateReadyPluginAt(malformedPlugin, GameRelease.SkyrimSE)));

        Assert.NotEqual(missingFailure.DiagnosticMessage, malformedFailure.DiagnosticMessage);
    }

    /// <summary>
    ///     Writes a file whose bytes are too short to be any release's mod header.
    /// </summary>
    /// <param name="fileName">The Plugin file name, which must still parse as a ModKey.</param>
    /// <returns>The full path to the written file.</returns>
    private string WriteMalformedPlugin(string fileName)
    {
        var path = Path.Combine(_testDirectory, fileName);

        // Shorter than the smallest mod header any Supported GameRelease uses, so no release can parse it.
        File.WriteAllBytes(path, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C]);
        return path;
    }

    /// <summary>
    ///     Creates one production-branded ready case at a given path, so a real-path assertion enters through the live
    ///     opaque seam.
    /// </summary>
    /// <param name="pluginPath">
    ///     The exact path the adapter should attempt; its file name becomes the Plugin name.
    /// </param>
    /// <param name="release">The GameRelease hidden inside the production capability.</param>
    /// <param name="readParameters">The prepared read parameters, or the defaults.</param>
    /// <returns>The ready selected-Plugin case for the path and release.</returns>
    private static SelectedPluginReady CreateReadyPluginAt(
        string pluginPath,
        GameRelease release,
        BinaryReadParameters? readParameters = null)
    {
        var capability = new PluginReadCapability(
            new MutagenPluginReadCapabilityPayload(release, readParameters ?? BinaryReadParameters.Default));
        return new SelectedPluginReady(Path.GetFileName(pluginPath), pluginPath, capability);
    }

    /// <summary>
    ///     Guards a release this file treats as unsupported against the Supported GameRelease table actually gaining
    ///     it, so a test cannot quietly start exercising a supported release.
    /// </summary>
    /// <param name="release">The release expected to be absent from the table.</param>
    private static void AssertNotInTheSupportedTable(GameRelease release)
    {
        Assert.False(
            SupportedGameReleases.IsSupported(release),
            $"{release} is now a Supported GameRelease, so it can no longer stand in for an unsupported one here. " +
            $"Pick another release Mutagen defines but this application excludes, or delete this test if none remain.");
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
    ///     Creates one exception of a Plugin-data failure family.
    /// </summary>
    /// <param name="family">The family's type name.</param>
    /// <param name="message">
    ///     The exception's own message. <see cref="RecordCollisionException" /> composes its own message from the
    ///     colliding FormKey instead, so assertions compare against the created exception's message.
    /// </param>
    private static Exception CreatePluginDataFailure(string family, string message)
    {
        return family switch
        {
            nameof(MalformedDataException) => new MalformedDataException(message),
            nameof(RecordException) => CreateRecordException(message),
            nameof(ArgumentException) => new ArgumentException(message),
            nameof(OverflowException) => new OverflowException(message),
            nameof(IOException) => new IOException(message),
            nameof(UnauthorizedAccessException) => new UnauthorizedAccessException(message),
            nameof(InvalidDataException) => new InvalidDataException(message),
            nameof(IndexOutOfRangeException) => new IndexOutOfRangeException(message),
            nameof(RecordCollisionException) => new RecordCollisionException(
                ModKey.FromNameAndExtension(PluginName),
                new FormKey(ModKey.FromNameAndExtension(PluginName), 0x000800),
                typeof(INpcGetter)),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown Plugin-data failure family.")
        };
    }

    /// <summary>
    ///     Wraps a cause in one named wrapper carrying <see cref="WrapperWording" /> as its own message.
    /// </summary>
    /// <param name="wrapper">
    ///     The wrapper's type name: one of Mutagen's three enrichment wrappers, a listed Plugin-data cause
    ///     (<see cref="IOException" />), or an exception the application does not recognise
    ///     (<see cref="InvalidOperationException" />).
    /// </param>
    /// <param name="cause">The exception to wrap.</param>
    /// <remarks>
    ///     Mutagen's own enrichment copies the inner message. These wrappers carry distinct wording instead, so an
    ///     assertion on the diagnostic message proves which exception's message was reported.
    /// </remarks>
    private static Exception Wrap(string wrapper, Exception cause)
    {
        return wrapper switch
        {
            nameof(RecordException) => new RecordException(
                formKey: null,
                recordType: null,
                modKey: ModKey.FromNameAndExtension(PluginName),
                edid: null,
                message: WrapperWording,
                innerException: cause),
            nameof(SubrecordException) => new SubrecordException(
                new RecordType("EDID"),
                formKey: null,
                majorRecordType: null,
                modKey: null,
                edid: null,
                message: WrapperWording,
                innerException: cause),
            nameof(ModGroupsMalformedException) => new ModGroupsMalformedException(
                ModKey.FromNameAndExtension(PluginName),
                WrapperWording,
                cause),
            nameof(IOException) => new IOException(WrapperWording, cause),
            nameof(InvalidOperationException) => new InvalidOperationException(WrapperWording, cause),
            _ => throw new ArgumentOutOfRangeException(nameof(wrapper), wrapper, "Unknown wrapper.")
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
        // PluginPath's file name is PluginName, so the case keeps the name every assertion in this file expects.
        return CreateReadyPluginAt(PluginPath, release, readParameters);
    }
}
