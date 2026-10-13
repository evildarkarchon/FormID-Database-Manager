#nullable enable

using System;
using System.IO;
using System.Linq;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers two of the production Mutagen overlay adapter's contracts: which failures it normalizes into an expected
///     Plugin-read failure, and the fact that an unsupported GameRelease is not one of them. Its third — preserving a
///     nested cancellation — is deliberately not covered, because Mutagen's overlay construction is synchronous and
///     not cancellable and no delegate-injection seam exists to drive one through.
/// </summary>
/// <remarks>
///     <para>
///         Every case here is driven by a real Mutagen call against a real path — a path that does not exist, a file
///         holding junk bytes, or a directory. No Plugin fixture is generated and no delegate-injection seam is used,
///         because ADR-0003 records that overlay construction is data in the Supported GameRelease table rather than a
///         seam of its own. This works because Mutagen walks a Plugin's mod header and top-level groups eagerly during
///         overlay construction, so a malformed file fails at construction rather than at first enumeration.
///     </para>
///     <para>
///         The happy path, per-release wiring, and the negative boundary of the classification list all need a
///         generated Plugin, so they live in <see cref="PluginOverlayConstructionTests" /> instead. Keeping them out
///         of this file is what lets the paragraph above stay true: the suite carries one way of making a Plugin, and
///         it is not a hand-assembled byte array here.
///     </para>
///     <para>
///         Known gap, recorded rather than papered over: the adapter walks an exception chain to the deepest cause
///         before using its message, but nothing here exercises that walk, because Mutagen produces no nested chain at
///         overlay-construction time. The eager path a mod header takes — <c>FillTypelessSubrecordTypes</c> — has no
///         exception enrichment at all; the paths that do wrap (<c>FillSubrecordTypes</c> and <c>FillMajorRecords</c>,
///         which raise <see cref="RecordException" />) belong to major-record parsing, which an overlay defers. Every
///         failure below therefore arrives exactly one level deep, so these tests pin message <em>forwarding</em> and
///         cannot pin the deepest-cause walk.
///     </para>
///     <para>
///         That gap is now known to be permanent, not merely deferred. It was expected to close once generated
///         fixtures let a Plugin's records actually be read, but reading records does not reach this adapter at all:
///         <c>PluginIngestion</c> catches <see cref="RecordException" /> itself while enumerating, and the adapter's
///         only job is to <em>construct</em> the overlay. So the deepest-cause walk and the
///         <see cref="RecordException" /> and <see cref="OverflowException" /> entries in the classification list are
///         unreachable from any caller, by either route. Recorded here rather than acted on: deleting them is a
///         narrowing of a deliberately conservative list and belongs in its own change.
///     </para>
/// </remarks>
public sealed class MutagenPluginOverlayReaderTests : IDisposable
{
    /// <summary>
    ///     A GameRelease Mutagen defines that this application deliberately does not support.
    /// </summary>
    /// <remarks>
    ///     Named rather than derived so the test reads concretely, but every use goes through
    ///     <see cref="AssertNotInTheSupportedTable" /> first: if this release is ever added to the table, the tests
    ///     using it fail with a message saying so instead of silently exercising a supported release. That keeps this
    ///     from becoming a stale copy of the table's own exclusion list.
    /// </remarks>
    private const GameRelease DeliberatelyUnsupportedRelease = GameRelease.OblivionRE;

    /// <summary>
    ///     An enum value Mutagen does not define at all, which the table treats by the same one rule.
    /// </summary>
    private const GameRelease UndefinedRelease = (GameRelease)999;

    private readonly IPluginOverlayReader _reader = new MutagenPluginOverlayReader();
    private readonly string _testDirectory;

    public MutagenPluginOverlayReaderTests()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"MutagenPluginOverlayReaderTests_{Guid.NewGuid()}");
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
    ///     Every Supported GameRelease, so a row added to the table inherits this file's failure coverage rather than
    ///     shipping untested.
    /// </summary>
    public static TheoryData<GameRelease> SupportedReleases =>
        [.. SupportedGameReleases.All.Select(row => row.Release)];

    // ---------------------------------------------------------------------------------------------------------
    // Unsupported GameRelease. The lookup sits outside the adapter's failure-handling block on purpose, and these
    // are the tests that pin it there: the fixture below is a Plugin that would itself fail to open, so if the
    // lookup moved inside the try, its ArgumentOutOfRangeException would be caught by the expected-failure check
    // (which treats ArgumentException, its base type, as a malformed-Plugin signal) and reported to the user as a
    // Failed Plugin instead of surfacing as the programming error it is (ADR-0003).
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Verifies a Mutagen-defined but deliberately unsupported release fails as a programming error naming the
    ///     release, and not as a Plugin-read failure.
    /// </summary>
    [Fact]
    public void ReadOverlay_DeliberatelyUnsupportedRelease_ThrowsUnsupportedReleaseRatherThanPluginReadFailure()
    {
        AssertNotInTheSupportedTable(DeliberatelyUnsupportedRelease);
        var malformedPlugin = WriteMalformedPlugin("Unsupported.esp");

        var exception = Record.Exception(() =>
            _reader.ReadOverlay(CreateReadyPlugin(malformedPlugin, DeliberatelyUnsupportedRelease)));

        // The "not a Failed Plugin" assertion comes first and is deliberately subsumed by the type assertion below.
        // It is the one that fires if the lookup ever moves inside the try, and it names the actual regression —
        // a programming error reaching the user as a Failed Plugin — rather than reporting a bare type mismatch.
        Assert.IsNotType<PluginOverlayReadException>(exception);
        var unsupported = Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Contains(DeliberatelyUnsupportedRelease.ToString(), unsupported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies an entirely undefined GameRelease value follows the same rule, so there is no third category of
    ///     unsupported release for a caller to reason about.
    /// </summary>
    [Fact]
    public void ReadOverlay_UndefinedGameReleaseValue_ThrowsUnsupportedReleaseRatherThanPluginReadFailure()
    {
        AssertNotInTheSupportedTable(UndefinedRelease);
        var malformedPlugin = WriteMalformedPlugin("Undefined.esp");

        var exception = Record.Exception(() =>
            _reader.ReadOverlay(CreateReadyPlugin(malformedPlugin, UndefinedRelease)));

        // Ordered for the same reason as the deliberately-unsupported case above.
        Assert.IsNotType<PluginOverlayReadException>(exception);
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>
    ///     Verifies the release is rejected before the Plugin path is consulted at all, so an unsupported release
    ///     cannot be masked by — or mistaken for — a problem with the file.
    /// </summary>
    [Fact]
    public void ReadOverlay_UnsupportedReleaseWithMissingPlugin_StillReportsTheUnsupportedRelease()
    {
        var missingPlugin = Path.Combine(_testDirectory, "Missing.esp");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _reader.ReadOverlay(CreateReadyPlugin(missingPlugin, DeliberatelyUnsupportedRelease)));
    }

    // ---------------------------------------------------------------------------------------------------------
    // Failure normalization. Each case below pins one entry of the adapter's expected-failure list by the input
    // that reaches it, never by inspecting the list itself: narrowing the list later fails one of these by name.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Verifies a Plugin whose file has gone missing becomes an expected Plugin-read failure — the filesystem
    ///     branch of the classification list — rather than escaping and aborting the Processing Run.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedReleases))]
    public void ReadOverlay_MissingPluginFile_ThrowsPluginReadFailure(GameRelease release)
    {
        var missingPlugin = Path.Combine(_testDirectory, "Missing.esp");

        var exception = Assert.Throws<PluginOverlayReadException>(() =>
            _reader.ReadOverlay(CreateReadyPlugin(missingPlugin, release)));

        Assert.IsType<FileNotFoundException>(exception.InnerException);
    }

    /// <summary>
    ///     Verifies a Plugin holding bytes that are not a valid mod header becomes an expected Plugin-read failure —
    ///     the malformed-data branch — so a corrupt download costs the user one Plugin rather than the whole run.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedReleases))]
    public void ReadOverlay_MalformedPluginBytes_ThrowsPluginReadFailure(GameRelease release)
    {
        var malformedPlugin = WriteMalformedPlugin("Malformed.esp");

        var exception = Assert.Throws<PluginOverlayReadException>(() =>
            _reader.ReadOverlay(CreateReadyPlugin(malformedPlugin, release)));

        Assert.IsType<MalformedDataException>(exception.InnerException);
    }

    /// <summary>
    ///     Verifies an empty Plugin file is malformed data rather than an empty-but-valid Plugin, so a truncated
    ///     download does not silently contribute zero records.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedReleases))]
    public void ReadOverlay_EmptyPluginFile_ThrowsPluginReadFailure(GameRelease release)
    {
        var emptyPlugin = Path.Combine(_testDirectory, "Empty.esp");
        File.WriteAllBytes(emptyPlugin, []);

        var exception = Assert.Throws<PluginOverlayReadException>(() =>
            _reader.ReadOverlay(CreateReadyPlugin(emptyPlugin, release)));

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
    public void ReadOverlay_PluginPathTheOperatingSystemRefusesToOpen_ThrowsPluginReadFailure(GameRelease release)
    {
        var unreadablePlugin = Path.Combine(_testDirectory, "Refused.esp");
        Directory.CreateDirectory(unreadablePlugin);

        var exception = Assert.Throws<PluginOverlayReadException>(() =>
            _reader.ReadOverlay(CreateReadyPlugin(unreadablePlugin, release)));

        Assert.IsType<UnauthorizedAccessException>(exception.InnerException);
    }

    /// <summary>
    ///     Verifies a path Mutagen cannot even read as a Plugin name becomes an expected Plugin-read failure — the
    ///     <see cref="ArgumentException" /> branch, which is the one the unsupported-release lookup is kept clear of.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedReleases))]
    public void ReadOverlay_PathThatIsNotAPluginName_ThrowsPluginReadFailure(GameRelease release)
    {
        var notAPluginName = Path.Combine(_testDirectory, "NotAPluginName");

        // Created so the path exists: without it a missing-file failure could produce the same verdict for the wrong
        // reason, and this case is meant to pin the name-parsing branch specifically.
        Directory.CreateDirectory(notAPluginName);

        var exception = Assert.Throws<PluginOverlayReadException>(() =>
            _reader.ReadOverlay(CreateReadyPlugin(notAPluginName, release)));

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
    public void ReadOverlay_MissingPluginFile_MessageIsTheUnderlyingCause()
    {
        var missingPlugin = Path.Combine(_testDirectory, "Missing.esp");

        var exception = Assert.Throws<PluginOverlayReadException>(() =>
            _reader.ReadOverlay(CreateReadyPlugin(missingPlugin, GameRelease.SkyrimSE)));

        // Asserted against the cause's own message, not against a test-side re-implementation of the adapter's
        // unwrapping rule — a helper that walked the chain the same way production does would agree with a wrong
        // policy as readily as a right one. See the class remarks for why the walk itself is unreachable here.
        Assert.Contains(missingPlugin, exception.Message, StringComparison.Ordinal);
        Assert.Equal(exception.InnerException!.Message, exception.Message);
    }

    /// <summary>
    ///     Verifies a malformed Plugin surfaces Mutagen's own parse-failure message, which names what could not be
    ///     read, rather than the same generic wording every failure would otherwise share.
    /// </summary>
    [Fact]
    public void ReadOverlay_MalformedPluginBytes_MessageIsTheUnderlyingCause()
    {
        var malformedPlugin = WriteMalformedPlugin("MalformedMessage.esp");

        var exception = Assert.Throws<PluginOverlayReadException>(() =>
            _reader.ReadOverlay(CreateReadyPlugin(malformedPlugin, GameRelease.SkyrimSE)));

        // Mutagen's own wording, pinned on purpose: this message is what a user reads on a Failed Plugin, so a
        // Mutagen upgrade that reworded it is a user-visible change worth failing on.
        Assert.Contains("Mod Header", exception.Message, StringComparison.Ordinal);
        Assert.Equal(exception.InnerException!.Message, exception.Message);
    }

    /// <summary>
    ///     Verifies two different underlying causes do not collapse to one message, which is what makes a Failed
    ///     Plugin message worth reading.
    /// </summary>
    [Fact]
    public void ReadOverlay_DifferentUnderlyingCauses_ProduceDifferentMessages()
    {
        var missingPlugin = Path.Combine(_testDirectory, "MissingDistinct.esp");
        var malformedPlugin = WriteMalformedPlugin("MalformedDistinct.esp");

        var missingFailure = Assert.Throws<PluginOverlayReadException>(() =>
            _reader.ReadOverlay(CreateReadyPlugin(missingPlugin, GameRelease.SkyrimSE)));
        var malformedFailure = Assert.Throws<PluginOverlayReadException>(() =>
            _reader.ReadOverlay(CreateReadyPlugin(malformedPlugin, GameRelease.SkyrimSE)));

        Assert.NotEqual(missingFailure.Message, malformedFailure.Message);
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
    ///     Creates one production-branded ready case so every adapter assertion enters through the live opaque seam.
    /// </summary>
    /// <param name="pluginPath">The exact path the adapter should attempt.</param>
    /// <param name="release">The GameRelease hidden inside the production capability.</param>
    /// <returns>The ready selected-Plugin case for the path and release.</returns>
    private static SelectedPluginReady CreateReadyPlugin(string pluginPath, GameRelease release)
    {
        var capability = new PluginReadCapability(
            new MutagenPluginReadCapabilityPayload(release, BinaryReadParameters.Default));
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
}
