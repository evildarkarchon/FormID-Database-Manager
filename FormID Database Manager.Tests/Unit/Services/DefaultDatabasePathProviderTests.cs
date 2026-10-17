#nullable enable

using System;
using System.IO;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class DefaultDatabasePathProviderTests
{
    /// <summary>
    /// Verifies that generated database paths live under the injected root and use the database-safe game name, and
    /// that computing one creates nothing: the FormID Record Store creates the parent folder when it opens (ADR-0001).
    /// </summary>
    [Fact]
    public void CreateDefaultDatabasePath_InjectedRoot_ReturnsSafeFilenameUnderRootAndCreatesNothing()
    {
        // Never created, so any directory found afterwards was made by the computation under test.
        var defaultDatabaseRoot = CreateUncreatedTemporaryPath();

        var databasePath = DefaultDatabasePathProvider.CreateDefaultDatabasePath(
            GameRelease.Fallout4VR,
            defaultDatabaseRoot);

        Assert.Equal(
            Path.Combine(defaultDatabaseRoot, "FormID Database Manager", "Databases", "Fallout4VR.db"),
            databasePath);
        Assert.False(Directory.Exists(defaultDatabaseRoot),
            $"Computing the default database path must not create the root: {defaultDatabaseRoot}");
    }

    /// <summary>
    /// Verifies that generated paths do not inherit process or system working directories.
    /// </summary>
    [Fact]
    public void CreateDefaultDatabasePath_DoesNotUseCurrentDirectoryOrSystemDirectory()
    {
        var currentDirectory = Path.Combine(Path.GetTempPath(), $"FormIdTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(currentDirectory);
        var defaultDatabaseRoot = CreateUncreatedTemporaryPath();
        var previousCurrentDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(currentDirectory);

            var databasePath = DefaultDatabasePathProvider.CreateDefaultDatabasePath(
                GameRelease.SkyrimSEGog,
                defaultDatabaseRoot);

            Assert.StartsWith(defaultDatabaseRoot, databasePath, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(Path.Combine("Databases", "SkyrimSEGog.db"), databasePath,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(databasePath.StartsWith(currentDirectory, StringComparison.OrdinalIgnoreCase));
            Assert.False(databasePath.StartsWith(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Directory.Delete(currentDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a root the composition could not resolve fails the default with today's explanation instead of
    /// producing a path relative to the working directory.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDefaultDatabasePath_UnresolvedRoot_ThrowsInvalidOperationException(string? defaultDatabaseRoot)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DefaultDatabasePathProvider.CreateDefaultDatabasePath(GameRelease.SkyrimSE, defaultDatabaseRoot));

        Assert.Equal(
            "Could not resolve the local application data directory for the current user.",
            exception.Message);
    }

    /// <summary>
    /// Returns a unique temporary path that does not exist and is deliberately never created.
    /// </summary>
    private static string CreateUncreatedTemporaryPath()
    {
        return Path.Combine(Path.GetTempPath(), $"FormIdTests-{Guid.NewGuid():N}");
    }
}
