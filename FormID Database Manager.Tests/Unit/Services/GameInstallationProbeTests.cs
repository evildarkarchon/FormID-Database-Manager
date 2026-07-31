#nullable enable

using System;
using System.IO;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the production probe adapter itself — the behaviour the port abstracts away, which only a real file
///     system and a real install-record lookup can show.
/// </summary>
public class GameInstallationProbeTests : IDisposable
{
    private readonly GameInstallationProbe _probe = new();
    private readonly string _testDirectory;

    public GameInstallationProbeTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"GameInstallationProbeTests_{Guid.NewGuid()}");
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
    ///     Verifies the adapter maps onto an actual file system rather than answering from anything of its own.
    /// </summary>
    [Fact]
    public void FileExists_RealFile_ReportsPresenceAndAbsence()
    {
        var presentFile = Path.Combine(_testDirectory, "Skyrim.esm");
        File.WriteAllText(presentFile, string.Empty);

        Assert.True(_probe.FileExists(presentFile));
        Assert.False(_probe.FileExists(Path.Combine(_testDirectory, "Absent.esm")));
        // A directory is not a file, which is what keeps the two members from collapsing into one.
        Assert.False(_probe.FileExists(_testDirectory));
    }

    /// <summary>
    ///     Verifies the adapter reports directories, including one that does not exist.
    /// </summary>
    [Fact]
    public void DirectoryExists_RealDirectory_ReportsPresenceAndAbsence()
    {
        var presentDirectory = Path.Combine(_testDirectory, "gogscripts");
        Directory.CreateDirectory(presentDirectory);

        Assert.True(_probe.DirectoryExists(presentDirectory));
        Assert.False(_probe.DirectoryExists(Path.Combine(_testDirectory, "Absent")));
    }

    /// <summary>
    ///     Verifies a read-only directory still reads normally: the existence APIs report facts rather than failing,
    ///     which is why detection can treat a false answer as "no such file" rather than as an error.
    /// </summary>
    [Fact]
    public void FileExists_InsideReadOnlyDirectory_StillReportsPresence()
    {
        var readOnlyDirectory = Path.Combine(_testDirectory, "ReadOnly");
        Directory.CreateDirectory(readOnlyDirectory);
        var presentFile = Path.Combine(readOnlyDirectory, "Starfield.esm");
        File.WriteAllText(presentFile, string.Empty);

        var directoryInfo = new DirectoryInfo(readOnlyDirectory);
        directoryInfo.Attributes |= FileAttributes.ReadOnly;
        try
        {
            Assert.True(_probe.DirectoryExists(readOnlyDirectory));
            Assert.True(_probe.FileExists(presentFile));
        }
        finally
        {
            // Restore before cleanup, which cannot delete a read-only directory.
            directoryInfo.Attributes &= ~FileAttributes.ReadOnly;
        }
    }

    /// <summary>
    ///     Verifies the install-record lookup answers rather than throwing, whatever this machine has recorded.
    ///     Missing or malformed registry state is the case the adapter's catch-all exists for.
    /// </summary>
    [Theory]
    [InlineData(GameRelease.SkyrimSE)]
    [InlineData(GameRelease.Fallout4)]
    [InlineData(GameRelease.Starfield)]
    [InlineData(GameRelease.Oblivion)]
    public void GetInstalledDirectories_AnyRelease_AnswersWithoutThrowing(GameRelease release)
    {
        var directories = _probe.GetInstalledDirectories(release);

        Assert.NotNull(directories);
        Assert.All(directories, directory => Assert.True(Directory.Exists(directory)));
    }
}
