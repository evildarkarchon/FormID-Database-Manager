#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the Game Installation module's location member: which directories a GameRelease is installed in,
///     answered through the same probe detection reads (ADR-0002).
/// </summary>
public class GameInstallationsLocationTests
{
    private readonly InMemoryGameInstallationProbe _probe = new();

    /// <summary>
    ///     Verifies recorded installs come back in record order, so the first one stays the one a caller offers first.
    /// </summary>
    [Fact]
    public void GetInstalledDirectories_RecordedInstalls_ReturnsThemInRecordOrder()
    {
        _probe.WithInstalledDirectories(
            GameRelease.SkyrimSE,
            @"C:\Games\Skyrim Special Edition",
            @"D:\Games\Skyrim Special Edition");

        var directories = CreateSut().GetInstalledDirectories(GameRelease.SkyrimSE);

        Assert.Equal(
            [@"C:\Games\Skyrim Special Edition", @"D:\Games\Skyrim Special Edition"],
            directories);
    }

    /// <summary>
    ///     Verifies a GameRelease with nothing recorded reports no installs rather than failing, which is what lets the
    ///     caller tell the user to Browse instead.
    /// </summary>
    [Fact]
    public void GetInstalledDirectories_NoRecordedInstalls_ReturnsEmpty()
    {
        _probe.WithInstalledDirectories(GameRelease.SkyrimSE, @"C:\Games\Skyrim Special Edition");

        var directories = CreateSut().GetInstalledDirectories(GameRelease.Fallout4);

        Assert.Empty(directories);
    }

    /// <summary>
    ///     Verifies each requested GameRelease is looked up on its own terms rather than answered from a cached first
    ///     result.
    /// </summary>
    [Fact]
    public void GetInstalledDirectories_DifferentReleases_AnswersEachFromItsOwnRecords()
    {
        _probe
            .WithInstalledDirectories(GameRelease.SkyrimSE, @"C:\Games\Skyrim Special Edition")
            .WithInstalledDirectories(GameRelease.Fallout4, @"C:\Games\Fallout 4");
        var sut = CreateSut();

        Assert.Equal([@"C:\Games\Skyrim Special Edition"], sut.GetInstalledDirectories(GameRelease.SkyrimSE));
        Assert.Equal([@"C:\Games\Fallout 4"], sut.GetInstalledDirectories(GameRelease.Fallout4));
        Assert.Equal(
            [GameRelease.SkyrimSE, GameRelease.Fallout4],
            _probe.InstalledDirectoryLookups);
    }

    /// <summary>
    ///     Verifies the in-memory adapter alone can supply install locations: the answer comes from the declared
    ///     records, and nothing about it reaches the real file system or the platform's install records.
    /// </summary>
    [Fact]
    public void GetInstalledDirectories_InMemoryAdapter_AnswersWithoutTouchingTheFileSystem()
    {
        var absentDirectory = Path.Combine(Path.GetTempPath(), $"absent-install-{Guid.NewGuid():N}");
        _probe.WithInstalledDirectories(GameRelease.Starfield, absentDirectory);

        var directories = CreateSut().GetInstalledDirectories(GameRelease.Starfield);

        // The declared record is returned as declared even though no such directory exists on this machine, which is
        // only possible if the module never went to disk to check.
        Assert.False(Directory.Exists(absentDirectory));
        Assert.Equal([absentDirectory], directories);
        Assert.Empty(_probe.ProbedPaths);
    }

    // Immutability of the returned collection is not asserted here: it is guaranteed by the ImmutableArray return
    // type at compile time, and a test of that would be asserting the BCL's contract rather than this module's.

    /// <summary>
    ///     Verifies a probe that fails propagates rather than being swallowed here. Turning a failure into "nothing
    ///     recorded" is the production adapter's job, and doing it twice would hide it from the caller (ADR-0002).
    /// </summary>
    [Fact]
    public void GetInstalledDirectories_ProbeFailure_Propagates()
    {
        _probe.BeforeInstalledDirectoriesLookup = _ =>
            throw new InvalidOperationException("install records unreadable");
        var sut = CreateSut();

        var failure = Assert.Throws<InvalidOperationException>(
            () => sut.GetInstalledDirectories(GameRelease.SkyrimSE));

        Assert.Equal("install records unreadable", failure.Message);
    }

    private GameInstallations CreateSut()
    {
        return new GameInstallations(_probe);
    }
}
