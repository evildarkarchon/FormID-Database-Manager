using System;
using System.IO;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class DefaultDatabasePathProviderTests
{
    /// <summary>
    /// Verifies that generated database paths live under app-local user data and use the database-safe game name.
    /// </summary>
    [Fact]
    public void CreateDefaultDatabasePath_CreatesAppDataDirectoryAndUsesSafeFilename()
    {
        var appDataRoot = CreateTemporaryDirectory();

        try
        {
            var databasePath = DefaultDatabasePathProvider.CreateDefaultDatabasePath(
                GameRelease.Fallout4VR,
                appDataRoot);

            var expectedDirectory = Path.Combine(appDataRoot, "FormID Database Manager", "Databases");
            Assert.Equal(Path.Combine(expectedDirectory, "Fallout4VR.db"), databasePath);
            Assert.True(Directory.Exists(expectedDirectory),
                $"Expected generated directory to exist: {expectedDirectory}");
        }
        finally
        {
            Directory.Delete(appDataRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that generated paths do not inherit process or system working directories.
    /// </summary>
    [Fact]
    public void CreateDefaultDatabasePath_DoesNotUseCurrentDirectoryOrSystemDirectory()
    {
        var currentDirectory = CreateTemporaryDirectory();
        var appDataRoot = CreateTemporaryDirectory();
        var previousCurrentDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(currentDirectory);

            var databasePath = DefaultDatabasePathProvider.CreateDefaultDatabasePath(
                GameRelease.SkyrimSEGog,
                appDataRoot);

            Assert.StartsWith(appDataRoot, databasePath, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(Path.Combine("Databases", "SkyrimSEGog.db"), databasePath,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(databasePath.StartsWith(currentDirectory, StringComparison.OrdinalIgnoreCase));
            Assert.False(databasePath.StartsWith(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Directory.Delete(currentDirectory, recursive: true);
            Directory.Delete(appDataRoot, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FormIdTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
