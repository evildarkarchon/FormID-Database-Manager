using System;
using System.IO;
using System.Linq;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class HasRecordsTests : IDisposable
{
    private readonly string _testDirectory;

    public HasRecordsTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"hasrecords_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        // Allow GC to collect any Mutagen binary overlays holding file handles
        GC.Collect();
        GC.WaitForPendingFinalizers();

        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; temp directory will be cleaned eventually
            }
        }
    }

    [Fact]
    public void HasRecords_ReturnsFalse_ForHeaderOnlyPlugin()
    {
        // Arrange - Create a valid plugin with no records
        var pluginPath = Path.Combine(_testDirectory, "Empty.esp");
        var mod = new SkyrimMod(ModKey.FromFileName("Empty.esp"), SkyrimRelease.SkyrimSE);
        mod.WriteToBinary(pluginPath);

        // Act
        var result = PluginListManager.HasRecords(pluginPath, GameRelease.SkyrimSE);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasRecords_ReturnsTrue_ForPluginWithRecords()
    {
        // Arrange - Create a plugin with at least one record
        var pluginPath = Path.Combine(_testDirectory, "WithRecords.esp");
        var mod = new SkyrimMod(ModKey.FromFileName("WithRecords.esp"), SkyrimRelease.SkyrimSE);
        mod.Npcs.AddNew("TestNpc");
        mod.WriteToBinary(pluginPath);

        // Act
        var result = PluginListManager.HasRecords(pluginPath, GameRelease.SkyrimSE);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRecords_ReturnsTrue_WhenFileDoesNotExist()
    {
        // Arrange - Non-existent path
        var pluginPath = Path.Combine(_testDirectory, "DoesNotExist.esp");

        // Act - Fail-open: unreadable plugins should still appear
        var result = PluginListManager.HasRecords(pluginPath, GameRelease.SkyrimSE);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRecords_ReturnsTrue_ForCorruptPlugin()
    {
        // Arrange - Write garbage bytes
        var pluginPath = Path.Combine(_testDirectory, "Corrupt.esp");
        File.WriteAllBytes(pluginPath, new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0x00, 0x00 });

        // Act - Fail-open: corrupt plugins should still appear
        var result = PluginListManager.HasRecords(pluginPath, GameRelease.SkyrimSE);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRecords_ReturnsTrue_ForLargeFile_WithoutMutagen()
    {
        // Arrange - Create a file larger than the size threshold (> 1KB)
        var pluginPath = Path.Combine(_testDirectory, "Large.esp");
        var largeContent = new byte[2048]; // 2KB - above the 1KB threshold
        // Fill with non-zero data to make it look like a real file
        for (var i = 0; i < largeContent.Length; i++)
        {
            largeContent[i] = 0xFF;
        }

        File.WriteAllBytes(pluginPath, largeContent);

        // Act - Should return true immediately via size heuristic without Mutagen
        var result = PluginListManager.HasRecords(pluginPath, GameRelease.SkyrimSE);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRecords_ReturnsTrue_ForUnsupportedGameRelease()
    {
        // Arrange - Valid file but unsupported game
        var pluginPath = Path.Combine(_testDirectory, "Unsupported.esp");
        var mod = new SkyrimMod(ModKey.FromFileName("Unsupported.esp"), SkyrimRelease.SkyrimSE);
        mod.WriteToBinary(pluginPath);

        // Act - Fail-open for unsupported game releases
        var result = PluginListManager.HasRecords(pluginPath, (GameRelease)999);

        // Assert
        Assert.True(result);
    }
}
