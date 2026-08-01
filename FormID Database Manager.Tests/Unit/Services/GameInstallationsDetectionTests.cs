#nullable enable

using System;
using System.IO;
using System.Linq;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using FormID_Database_Manager.Tests.Fakes;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     The detection rules, exercised against a declared in-memory layout rather than a directory tree on disk.
/// </summary>
/// <remarks>
///     Detection reads the outside world only through its probe, so a layout is the whole fixture: every case below
///     names a place that does not exist, which is what proves the rules go through the seam rather than around it.
///     The production adapter's own behaviour — what a real file system does that the port abstracts away — is covered
///     by <c>GameInstallationProbeTests</c>, and the canonicalization rule in isolation by <c>GameInstallationsTests</c>.
/// </remarks>
public class GameInstallationsDetectionTests
{
    /// <summary>
    ///     A game root that exists nowhere, so a rule consulting the real file system could not answer for it.
    /// </summary>
    private const string GameRoot = @"C:\NoSuchPlace\Test Game";

    [Fact]
    public void Detect_SkyrimMasterWithNoReleaseMarker_ReturnsSkyrimLegendaryEdition()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Skyrim.esm")
            .Build();

        Assert.Equal(GameRelease.SkyrimLE, DetectorFor(layout).Detect(layout.GameRoot));
    }

    [Fact]
    public void Detect_SkyrimMasterWithSkyrimSeExecutable_ReturnsSkyrimSpecialEdition()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Skyrim.esm")
            .AddRootFile("SkyrimSE.exe")
            .Build();

        Assert.Equal(GameRelease.SkyrimSE, DetectorFor(layout).Detect(layout.GameRoot));
    }

    [Fact]
    public void Detect_SkyrimMasterWithSkyrimVrExecutable_ReturnsSkyrimVr()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Skyrim.esm")
            .AddRootFile("SkyrimVR.exe")
            .Build();

        Assert.Equal(GameRelease.SkyrimVR, DetectorFor(layout).Detect(layout.GameRoot));
    }

    /// <summary>
    ///     Verifies the GOG release is recognized by its marker directory, which is the only rule reading the probe's
    ///     directory-existence member.
    /// </summary>
    [Fact]
    public void Detect_SkyrimMasterWithGogScriptsDirectory_ReturnsSkyrimGog()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Skyrim.esm")
            .AddRootDirectory("gogscripts")
            .Build();

        Assert.Equal(GameRelease.SkyrimSEGog, DetectorFor(layout).Detect(layout.GameRoot));
    }

    /// <summary>
    ///     Verifies the second GOG marker: an installation whose scripts directory is absent is still recognized by the
    ///     store's game-info file.
    /// </summary>
    [Fact]
    public void Detect_SkyrimMasterWithGogGameInfoFile_ReturnsSkyrimGog()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Skyrim.esm")
            .AddRootFile("goggame-1746476928.info")
            .Build();

        Assert.Equal(GameRelease.SkyrimSEGog, DetectorFor(layout).Detect(layout.GameRoot));
    }

    [Fact]
    public void Detect_EnderalMasterWithSkyrimSeExecutable_ReturnsEnderalSpecialEdition()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugins("Skyrim.esm", "Enderal - Forgotten Stories.esm")
            .AddRootFile("SkyrimSE.exe")
            .Build();

        Assert.Equal(GameRelease.EnderalSE, DetectorFor(layout).Detect(layout.GameRoot));
    }

    [Fact]
    public void Detect_EnderalMasterWithTesvExecutable_ReturnsEnderalLegendaryEdition()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugins("Skyrim.esm", "Enderal - Forgotten Stories.esm")
            .AddRootFile("TESV.exe")
            .Build();

        Assert.Equal(GameRelease.EnderalLE, DetectorFor(layout).Detect(layout.GameRoot));
    }

    /// <summary>
    ///     Verifies that the Enderal master alone does not claim the installation: with neither Skyrim executable
    ///     present the layout falls through to the plain Skyrim rules.
    /// </summary>
    [Fact]
    public void Detect_EnderalMasterWithoutASkyrimExecutable_FallsThroughToSkyrimLegendaryEdition()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugins("Skyrim.esm", "Enderal - Forgotten Stories.esm")
            .Build();

        Assert.Equal(GameRelease.SkyrimLE, DetectorFor(layout).Detect(layout.GameRoot));
    }

    [Fact]
    public void Detect_Fallout4Master_ReturnsFallout4()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Fallout4.esm")
            .Build();

        Assert.Equal(GameRelease.Fallout4, DetectorFor(layout).Detect(layout.GameRoot));
    }

    [Fact]
    public void Detect_Fallout4MasterWithVrExecutable_ReturnsFallout4Vr()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Fallout4.esm")
            .AddRootFile("Fallout4VR.exe")
            .Build();

        Assert.Equal(GameRelease.Fallout4VR, DetectorFor(layout).Detect(layout.GameRoot));
    }

    [Fact]
    public void Detect_OblivionMaster_ReturnsOblivion()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Oblivion.esm")
            .Build();

        Assert.Equal(GameRelease.Oblivion, DetectorFor(layout).Detect(layout.GameRoot));
    }

    [Fact]
    public void Detect_StarfieldMaster_ReturnsStarfield()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Starfield.esm")
            .Build();

        Assert.Equal(GameRelease.Starfield, DetectorFor(layout).Detect(layout.GameRoot));
    }

    /// <summary>
    ///     Verifies that a Data directory holding Plugins but no known game master reports the absence of a game rather
    ///     than failing.
    /// </summary>
    [Fact]
    public void Detect_LayoutWithoutAKnownMaster_ReturnsNull()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("SomeMod.esp")
            .Build();

        Assert.Null(DetectorFor(layout).Detect(layout.GameRoot));
    }

    /// <summary>
    ///     Verifies that an empty Data directory is a game-less directory, not a failure.
    /// </summary>
    [Fact]
    public void Detect_EmptyDataDirectory_ReturnsNull()
    {
        var layout = new GameDetectionBuilder().WithGameRoot(GameRoot).Build();

        Assert.Null(DetectorFor(layout).Detect(layout.GameRoot));
    }

    /// <summary>
    ///     Verifies that a directory the probe knows nothing about — the in-memory stand-in for one that does not
    ///     exist, with not even a Data directory beneath it — reports the absence of a game rather than failing.
    /// </summary>
    [Fact]
    public void Detect_DirectoryTheProbeDoesNotKnow_ReturnsNull()
    {
        var gameInstallations = new GameInstallations(new InMemoryGameInstallationProbe());

        Assert.Null(gameInstallations.Detect(Path.Combine(GameRoot, "Absent")));
    }

    /// <summary>
    ///     Verifies that nothing is read from the directory's name: a directory named after a game but holding no
    ///     master file still reports no game.
    /// </summary>
    /// <param name="gameLikeDirectoryName">A directory name a user might expect to be recognized on its own.</param>
    [Theory]
    [InlineData("Skyrim Special Edition")]
    [InlineData("SkyrimVR")]
    [InlineData("Fallout 4")]
    [InlineData("Starfield")]
    [InlineData("Oblivion")]
    public void Detect_GameLikeDirectoryNameWithoutAMaster_ReturnsNull(string gameLikeDirectoryName)
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(Path.Combine(@"C:\NoSuchPlace", gameLikeDirectoryName))
            .Build();

        Assert.Null(DetectorFor(layout).Detect(layout.GameRoot));
    }

    /// <summary>
    ///     Verifies the master-file precedence a mixed Data directory falls under: Skyrim is checked first, so a
    ///     directory holding both masters detects as Skyrim rather than as Fallout 4.
    /// </summary>
    [Fact]
    public void Detect_DataDirectoryWithTwoKnownMasters_ReturnsTheFirstRuleThatMatches()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugins("Skyrim.esm", "Fallout4.esm")
            .Build();

        Assert.Equal(GameRelease.SkyrimLE, DetectorFor(layout).Detect(layout.GameRoot));
    }

    /// <summary>
    ///     Verifies both accepted inputs resolve to one Game Installation, for a release identified by its master file
    ///     alone and for one identified by a marker beside the Data directory.
    /// </summary>
    /// <param name="masterFile">The master file the Data directory holds.</param>
    /// <param name="rootMarkerFile">A release marker in the game root, or null when the master alone decides.</param>
    /// <param name="expectedGame">The release both inputs must detect as.</param>
    [Theory]
    [InlineData("Skyrim.esm", null, GameRelease.SkyrimLE)]
    [InlineData("Skyrim.esm", "SkyrimVR.exe", GameRelease.SkyrimVR)]
    [InlineData("Oblivion.esm", null, GameRelease.Oblivion)]
    [InlineData("Fallout4.esm", "Fallout4VR.exe", GameRelease.Fallout4VR)]
    [InlineData("Starfield.esm", null, GameRelease.Starfield)]
    public void Detect_GameRootOrDataDirectory_ResolveToTheSameInstallation(
        string masterFile,
        string? rootMarkerFile,
        GameRelease expectedGame)
    {
        var builder = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin(masterFile);

        if (rootMarkerFile is not null)
        {
            builder.AddRootFile(rootMarkerFile);
        }

        var layout = builder.Build();
        var gameInstallations = DetectorFor(layout);

        Assert.Equal(expectedGame, gameInstallations.Detect(layout.GameRoot));
        Assert.Equal(expectedGame, gameInstallations.Detect(layout.DataDirectory));
    }

    /// <summary>
    ///     Verifies that a pasted path keeping its terminal separator reaches the same Game Installation, from either
    ///     accepted input.
    /// </summary>
    /// <param name="separator">A terminal separator in either of the spellings a user can produce.</param>
    [Theory]
    [InlineData("\\")]
    [InlineData("/")]
    public void Detect_TrailingSeparator_MatchesThePlainForm(string separator)
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Starfield.esm")
            .Build();
        var gameInstallations = DetectorFor(layout);

        Assert.Equal(GameRelease.Starfield, gameInstallations.Detect(layout.GameRoot + separator));
        Assert.Equal(GameRelease.Starfield, gameInstallations.Detect(layout.DataDirectory + separator));
    }

    /// <summary>
    ///     Verifies that a path spelled with the alternate separator throughout reaches the same Game Installation.
    /// </summary>
    [Fact]
    public void Detect_MixedSeparators_MatchesThePlainForm()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Oblivion.esm")
            .Build();
        var mixed = layout.GameRoot.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "/Data";

        Assert.Equal(GameRelease.Oblivion, DetectorFor(layout).Detect(mixed));
    }

    /// <summary>
    ///     Verifies that dot segments resolve before the Data check, so a path routed through a sibling directory and
    ///     back still names the same Game Installation.
    /// </summary>
    [Fact]
    public void Detect_DotSegments_MatchesThePlainForm()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Fallout4.esm")
            .Build();
        var gameInstallations = DetectorFor(layout);

        Assert.Equal(GameRelease.Fallout4, gameInstallations.Detect(Path.Combine(layout.DataDirectory, ".")));
        Assert.Equal(GameRelease.Fallout4, gameInstallations.Detect(Path.Combine(layout.GameRoot, "unused", "..")));
        Assert.Equal(
            GameRelease.Fallout4,
            gameInstallations.Detect(Path.Combine(layout.DataDirectory, "unused", "..")));
    }

    /// <summary>
    ///     Verifies that a Data directory spelled in any casing is recognized as one, rather than having a second Data
    ///     segment appended to it.
    /// </summary>
    /// <param name="dataFolderName">The Data segment as the file system might hand it back.</param>
    [Theory]
    [InlineData("Data")]
    [InlineData("data")]
    [InlineData("DATA")]
    public void Detect_DataDirectoryInAnyCasing_MatchesThePlainForm(string dataFolderName)
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Starfield.esm")
            .Build();

        Assert.Equal(
            GameRelease.Starfield,
            DetectorFor(layout).Detect(Path.Combine(layout.GameRoot, dataFolderName)));
    }

    /// <summary>
    ///     Verifies the narrowed contract: a path detection cannot use at all throws instead of reporting the absence
    ///     of a game, so the caller can tell the user which of the two actually happened.
    /// </summary>
    /// <param name="malformedDirectory">A directory value detection cannot resolve to a Game Installation.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\Games\\Sky\0rim")]
    public void Detect_MalformedDirectory_ThrowsRatherThanReturningNull(string malformedDirectory)
    {
        var gameInstallations = new GameInstallations(new InMemoryGameInstallationProbe());

        Assert.Throws<ArgumentException>(() => gameInstallations.Detect(malformedDirectory));
    }

    /// <summary>
    ///     Verifies that a missing directory value is rejected as the programming error it is, rather than read as a
    ///     game-less directory.
    /// </summary>
    [Fact]
    public void Detect_NullDirectory_ThrowsRatherThanReturningNull()
    {
        var gameInstallations = new GameInstallations(new InMemoryGameInstallationProbe());

        Assert.Throws<ArgumentNullException>(() => gameInstallations.Detect(null!));
    }

    /// <summary>
    ///     Verifies that detection reads the outside world only through the probe: every layout in this file exists
    ///     nowhere on disk, and this case says so explicitly rather than leaving it implied.
    /// </summary>
    [Fact]
    public void Detect_LayoutThatExistsOnlyInTheProbe_AppliesTheRealRules()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Skyrim.esm")
            .AddRootFile("SkyrimSE.exe")
            .Build();

        Assert.False(Directory.Exists(layout.GameRoot));
        Assert.Equal(GameRelease.SkyrimSE, DetectorFor(layout).Detect(layout.GameRoot));
    }

    /// <summary>
    ///     Verifies that parent-directory resolution is path arithmetic rather than a probe call: every path the module
    ///     probes is a concrete file or marker directory, never a bare parent lookup.
    /// </summary>
    [Fact]
    public void Detect_DataDirectoryInput_ResolvesTheGameRootWithoutProbingForIt()
    {
        var layout = new GameDetectionBuilder()
            .WithGameRoot(GameRoot)
            .AddPlugin("Skyrim.esm")
            .Build();
        var probe = new InMemoryGameInstallationProbe().WithLayout(layout);

        new GameInstallations(probe).Detect(layout.DataDirectory);

        var probedPaths = probe.ProbedPaths.Select(Path.TrimEndingDirectorySeparator).ToList();
        Assert.DoesNotContain(layout.GameRoot, probedPaths);
        Assert.DoesNotContain(layout.DataDirectory, probedPaths);
    }

    /// <summary>
    ///     Builds a detection module over a probe holding nothing but the supplied layout.
    /// </summary>
    /// <param name="layout">The declared Game Installation layout.</param>
    /// <returns>A module whose rules run against that layout and nothing else.</returns>
    private static GameInstallations DetectorFor(GameDetectionLayout layout)
    {
        return new GameInstallations(new InMemoryGameInstallationProbe().WithLayout(layout));
    }
}
