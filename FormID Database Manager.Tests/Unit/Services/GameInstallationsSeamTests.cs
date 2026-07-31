#nullable enable

using System.IO;
using System.Linq;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers <see cref="GameInstallations" /> through its one seam: the rules run against a declared layout, with no
///     directory tree on disk to back them. The production adapter's own behaviour is covered by
///     <c>GameInstallationProbeTests</c>, and the rules themselves by <c>GameInstallationsDetectionTests</c>.
/// </summary>
public class GameInstallationsSeamTests
{
    private const string GameRoot = @"C:\NoSuchPlace\Skyrim Special Edition";

    /// <summary>
    ///     Verifies that detection reads the outside world only through the probe: the layout below exists nowhere on
    ///     disk, so a rule consulting the real file system directly could not produce this answer.
    /// </summary>
    [Fact]
    public void Detect_LayoutThatExistsOnlyInTheProbe_AppliesTheRealRules()
    {
        var probe = new InMemoryGameInstallationProbe()
            .WithFile(Path.Combine(GameRoot, "Data", "Skyrim.esm"))
            .WithFile(Path.Combine(GameRoot, "SkyrimSE.exe"));
        var gameInstallations = new GameInstallations(probe);

        Assert.False(Directory.Exists(GameRoot));
        Assert.Equal(GameRelease.SkyrimSE, gameInstallations.Detect(GameRoot));
        Assert.Equal(GameRelease.SkyrimSE, gameInstallations.Detect(Path.Combine(GameRoot, "Data")));
    }

    /// <summary>
    ///     Verifies that the GOG marker directory is recognized through the probe's directory-existence member, which
    ///     is the only rule that needs it.
    /// </summary>
    [Fact]
    public void Detect_GogMarkerDirectory_IsReadThroughTheProbe()
    {
        var probe = new InMemoryGameInstallationProbe()
            .WithFile(Path.Combine(GameRoot, "Data", "Skyrim.esm"))
            .WithDirectory(Path.Combine(GameRoot, "gogscripts"));
        var gameInstallations = new GameInstallations(probe);

        Assert.Equal(GameRelease.SkyrimSEGog, gameInstallations.Detect(GameRoot));
    }

    /// <summary>
    ///     Verifies that a layout with no known game master reports absence rather than failing.
    /// </summary>
    [Fact]
    public void Detect_LayoutWithoutAKnownMaster_ReturnsNull()
    {
        var probe = new InMemoryGameInstallationProbe()
            .WithFile(Path.Combine(GameRoot, "Data", "SomeMod.esp"));
        var gameInstallations = new GameInstallations(probe);

        Assert.Null(gameInstallations.Detect(GameRoot));
    }

    /// <summary>
    ///     Verifies that parent-directory resolution is path arithmetic rather than a probe call: every path the
    ///     module probes is a concrete file or marker directory, never a bare parent lookup.
    /// </summary>
    [Fact]
    public void Detect_ResolvesTheGameRootWithoutProbingForIt()
    {
        var probe = new InMemoryGameInstallationProbe()
            .WithFile(Path.Combine(GameRoot, "Data", "Skyrim.esm"));
        var gameInstallations = new GameInstallations(probe);

        gameInstallations.Detect(Path.Combine(GameRoot, "Data"));

        Assert.DoesNotContain(GameRoot, probe.ProbedPaths.Select(Path.TrimEndingDirectorySeparator));
        Assert.DoesNotContain(
            Path.Combine(GameRoot, "Data"),
            probe.ProbedPaths.Select(Path.TrimEndingDirectorySeparator));
    }
}
