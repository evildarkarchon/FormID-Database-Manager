#nullable enable

using System;
using System.IO;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class PluginListSourceTests
{
    // The type performs no I/O, so these directories never need to exist on disk.
    private readonly string _gameDirectory = Path.Combine(
        Path.GetTempPath(),
        $"plugin-list-source-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Create_EquivalentGameRootAndDataDirectory_ProducesOneCanonicalSource()
    {
        var fromGameRoot = PluginListSource.Create(GameRelease.SkyrimSE, _gameDirectory);
        var dataDirectoryName = OperatingSystem.IsWindows() ? "data" : "Data";
        var fromDataDirectory = PluginListSource.Create(
            GameRelease.SkyrimSE,
            Path.Combine(_gameDirectory, dataDirectoryName) + Path.DirectorySeparatorChar);
        var fromRootWithDotSegments = PluginListSource.Create(
            GameRelease.SkyrimSE,
            Path.Combine(_gameDirectory, "unused", ".."));
        var fromDataWithDotSegment = PluginListSource.Create(
            GameRelease.SkyrimSE,
            Path.Combine(_gameDirectory, "Data", "."));

        Assert.Equal(fromGameRoot, fromDataDirectory);
        Assert.Equal(fromGameRoot, fromRootWithDotSegments);
        Assert.Equal(fromGameRoot, fromDataWithDotSegment);
        Assert.Equal(fromGameRoot.GetHashCode(), fromDataDirectory.GetHashCode());
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(_gameDirectory, "Data"))),
            fromGameRoot.DataDirectory,
            ignoreCase: OperatingSystem.IsWindows());
    }

    /// <summary>
    ///     Verifies that a game root with a terminal separator resolves to the same canonical Data directory as the plain
    ///     root. This is where a selected-Plugin run's Data directory is decided: Plugin Ingestion reads it from the
    ///     request's source and no longer canonicalizes the directory itself (ADR-0010).
    /// </summary>
    /// <param name="separator">The terminal separator appended to the game root.</param>
    [Theory]
    [InlineData("/")]
    [InlineData("\\")]
    public void Create_GameRootWithTrailingSeparator_UsesTheRootsCanonicalDataDirectory(string separator)
    {
        if (separator == "\\" && !OperatingSystem.IsWindows())
        {
            // A backslash is an ordinary file-name character off Windows, so it is not a separator there.
            return;
        }

        var source = PluginListSource.Create(GameRelease.SkyrimSE, _gameDirectory + separator);

        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, _gameDirectory), source);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(_gameDirectory, "Data")),
            source.DataDirectory,
            ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public void Create_DifferentGameReleaseOrDirectory_ProducesDifferentSources()
    {
        var source = PluginListSource.Create(GameRelease.SkyrimSE, _gameDirectory);
        var differentRelease = PluginListSource.Create(GameRelease.SkyrimVR, _gameDirectory);
        var differentDirectory = PluginListSource.Create(
            GameRelease.SkyrimSE,
            Path.Combine(Path.GetTempPath(), $"different-plugin-list-source-{Guid.NewGuid():N}"));

        Assert.NotEqual(source, differentRelease);
        Assert.NotEqual(source, differentDirectory);
    }

    [Fact]
    public void Create_InvalidArguments_ThrowsBeforeDiscovery()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PluginListSource.Create(GameRelease.SkyrimSE, null!));
        Assert.Throws<ArgumentException>(() =>
            PluginListSource.Create(GameRelease.SkyrimSE, "   "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PluginListSource.Create((GameRelease)int.MaxValue, _gameDirectory));

        // Mutagen defines OblivionRE, so an enum-definedness check would have admitted it. The guard tests
        // supported-ness instead, which is what its message has always claimed (ADR-0003).
        // This is the only unsupported-release guard in front of a Plugin List refresh: the refresh takes a built
        // source, and an unsupported release once produced a Plugin List that hid nothing, because the Base Game
        // Plugin lookup answered with an empty set.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PluginListSource.Create(GameRelease.OblivionRE, _gameDirectory));
    }
}
