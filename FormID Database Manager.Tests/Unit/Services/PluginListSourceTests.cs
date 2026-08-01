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
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PluginListSource.Create(GameRelease.OblivionRE, _gameDirectory));
    }
}
