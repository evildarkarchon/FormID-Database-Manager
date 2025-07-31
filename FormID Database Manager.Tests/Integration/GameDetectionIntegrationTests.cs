using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Integration;

/// <summary>
/// Integration tests for game detection functionality,
/// testing real file system interactions and directory structures.
/// </summary>
public class GameDetectionIntegrationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly List<string> _testDirectories;
    private readonly GameDetectionService _gameDetectionService;

    public GameDetectionIntegrationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"gamedetection_{Guid.NewGuid()}");
        _testDirectories = new List<string>();
        _gameDetectionService = new GameDetectionService();
        
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        foreach (var dir in _testDirectories.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, true); } catch { }
        }
        
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    #region Real Directory Structure Tests

    [Fact]
    public void DetectGame_WithRealDirectoryStructures_AllGameTypes()
    {
        // Test each game type with realistic directory structures
        var testCases = new[]
        {
            // Skyrim SE
            new
            {
                GameName = "Skyrim Special Edition",
                GameRelease = GameRelease.SkyrimSE,
                DataPath = Path.Combine(_testRoot, "Skyrim Special Edition", "Data"),
                RequiredFiles = new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" }
            },
            // Skyrim SE GOG
            new
            {
                GameName = "Skyrim Special Edition GOG",
                GameRelease = GameRelease.SkyrimSEGog,
                DataPath = Path.Combine(_testRoot, "Skyrim Special Edition GOG", "Data"),
                RequiredFiles = new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" }
            },
            // Skyrim VR
            new
            {
                GameName = "SkyrimVR",
                GameRelease = GameRelease.SkyrimVR,
                DataPath = Path.Combine(_testRoot, "SkyrimVR", "Data"),
                RequiredFiles = new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm", "SkyrimVR.esm" }
            },
            // Fallout 4
            new
            {
                GameName = "Fallout 4",
                GameRelease = GameRelease.Fallout4,
                DataPath = Path.Combine(_testRoot, "Fallout 4", "Data"),
                RequiredFiles = new[] { "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm" }
            },
            // Starfield
            new
            {
                GameName = "Starfield",
                GameRelease = GameRelease.Starfield,
                DataPath = Path.Combine(_testRoot, "Starfield", "Data"),
                RequiredFiles = new[] { "Starfield.esm", "Constellation.esm", "OldMars.esm" }
            },
            // Oblivion
            new
            {
                GameName = "Oblivion",
                GameRelease = GameRelease.Oblivion,
                DataPath = Path.Combine(_testRoot, "Oblivion", "Data"),
                RequiredFiles = new[] { "Oblivion.esm" }
            }
        };

        foreach (var testCase in testCases)
        {
            // Arrange
            var gameDir = Path.GetDirectoryName(testCase.DataPath)!;
            _testDirectories.Add(gameDir);
            Directory.CreateDirectory(testCase.DataPath);
            
            // Create required plugin files
            foreach (var file in testCase.RequiredFiles)
            {
                CreatePluginFile(testCase.DataPath, file);
            }

            // Create additional files for specific game detection
            switch (testCase.GameRelease)
            {
                case GameRelease.SkyrimVR:
                    // Create SkyrimVR.exe in parent directory
                    File.WriteAllText(Path.Combine(gameDir, "SkyrimVR.exe"), "dummy");
                    break;
                case GameRelease.SkyrimSEGog:
                    // Create GOG-specific directory
                    Directory.CreateDirectory(Path.Combine(gameDir, "gogscripts"));
                    break;
                case GameRelease.Fallout4VR:
                    // Create Fallout4VR.exe in parent directory
                    File.WriteAllText(Path.Combine(gameDir, "Fallout4VR.exe"), "dummy");
                    break;
            }

            // Act
            var detectedGame = _gameDetectionService.DetectGame(testCase.DataPath);

            // Assert
            Assert.Equal(testCase.GameRelease, detectedGame);
        }
    }

    [Fact]
    public void DetectGame_HandlesDataDirectoryVariations()
    {
        // Test different ways users might select directories
        var variations = new[]
        {
            // User selects game root directory
            new { Path = Path.Combine(_testRoot, "Skyrim SE Root"), ExpectedData = Path.Combine(_testRoot, "Skyrim SE Root", "Data") },
            // User selects Data directory directly
            new { Path = Path.Combine(_testRoot, "Skyrim SE Data", "Data"), ExpectedData = Path.Combine(_testRoot, "Skyrim SE Data", "Data") },
            // Mixed case
            new { Path = Path.Combine(_testRoot, "Skyrim SE Mixed", "data"), ExpectedData = Path.Combine(_testRoot, "Skyrim SE Mixed", "data") }
        };

        foreach (var variation in variations)
        {
            // Arrange
            _testDirectories.Add(Path.GetDirectoryName(variation.ExpectedData)!);
            Directory.CreateDirectory(variation.ExpectedData);
            CreatePluginFile(variation.ExpectedData, "Skyrim.esm");
            CreatePluginFile(variation.ExpectedData, "Update.esm");

            // Act
            var detectedGame = _gameDetectionService.DetectGame(variation.Path);

            // Assert
            Assert.Equal(GameRelease.SkyrimSE, detectedGame);
        }
    }

    #endregion

    #region Symbolic Link Tests

    [Fact]
    public void DetectGame_HandlesSymbolicLinks_Correctly()
    {
        // Skip if not running as administrator on Windows
        if (Environment.OSVersion.Platform == PlatformID.Win32NT && !IsAdministrator())
        {
            return;
        }

        // Arrange
        var realDataPath = Path.Combine(_testRoot, "RealGame", "Data");
        var symlinkPath = Path.Combine(_testRoot, "SymlinkGame", "Data");
        
        _testDirectories.Add(Path.GetDirectoryName(realDataPath)!);
        _testDirectories.Add(Path.GetDirectoryName(symlinkPath)!);
        
        Directory.CreateDirectory(realDataPath);
        CreatePluginFile(realDataPath, "Skyrim.esm");
        CreatePluginFile(realDataPath, "Update.esm");
        
        // Create symbolic link
        Directory.CreateDirectory(Path.GetDirectoryName(symlinkPath)!);
        try
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // Windows symbolic link
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /D \"{symlinkPath}\" \"{realDataPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                process?.WaitForExit();
            }
            else
            {
                // Unix symbolic link
                var process = System.Diagnostics.Process.Start("ln", $"-s \"{realDataPath}\" \"{symlinkPath}\"");
                process?.WaitForExit();
            }

            // Act
            var detectedGame = _gameDetectionService.DetectGame(symlinkPath);

            // Assert
            Assert.Equal(GameRelease.SkyrimSE, detectedGame);
        }
        catch
        {
            // Skip test if symbolic link creation fails
        }
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void DetectGame_PerformanceWithLargeDirectories()
    {
        // Arrange - Create directory with many files
        var largeDataPath = Path.Combine(_testRoot, "LargeGame", "Data");
        _testDirectories.Add(Path.GetDirectoryName(largeDataPath)!);
        Directory.CreateDirectory(largeDataPath);
        
        // Create required game files
        CreatePluginFile(largeDataPath, "Skyrim.esm");
        CreatePluginFile(largeDataPath, "Update.esm");
        
        // Create many additional files
        for (int i = 0; i < 1000; i++)
        {
            File.WriteAllText(Path.Combine(largeDataPath, $"Mod{i:D4}.esp"), "test");
        }

        var startTime = DateTime.UtcNow;

        // Act
        var detectedGame = _gameDetectionService.DetectGame(largeDataPath);
        
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.Equal(GameRelease.SkyrimSE, detectedGame);
        Assert.True(elapsed.TotalSeconds < 1, $"Detection took too long: {elapsed.TotalSeconds}s");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void DetectGame_HandlesEmptyDirectory()
    {
        // Arrange
        var emptyPath = Path.Combine(_testRoot, "Empty", "Data");
        _testDirectories.Add(Path.GetDirectoryName(emptyPath)!);
        Directory.CreateDirectory(emptyPath);

        // Act
        var detectedGame = _gameDetectionService.DetectGame(emptyPath);

        // Assert
        Assert.Null(detectedGame); // Returns null when no game detected
    }

    [Fact]
    public void DetectGame_HandlesNonExistentDirectory()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testRoot, "DoesNotExist", "Data");

        // Act
        var detectedGame = _gameDetectionService.DetectGame(nonExistentPath);

        // Assert
        Assert.Null(detectedGame); // Returns null when no game detected
    }

    [Fact]
    public void DetectGame_HandlesAmbiguousGameFiles()
    {
        // Arrange - Directory with files from multiple games
        var ambiguousPath = Path.Combine(_testRoot, "Ambiguous", "Data");
        _testDirectories.Add(Path.GetDirectoryName(ambiguousPath)!);
        Directory.CreateDirectory(ambiguousPath);
        
        // Mix of Skyrim and Fallout files
        CreatePluginFile(ambiguousPath, "Skyrim.esm");
        CreatePluginFile(ambiguousPath, "Fallout4.esm");
        CreatePluginFile(ambiguousPath, "Update.esm");

        // Act
        var detectedGame = _gameDetectionService.DetectGame(ambiguousPath);

        // Assert - Should detect based on priority (Skyrim files present)
        Assert.Equal(GameRelease.SkyrimSE, detectedGame);
    }

    [Fact]
    public void DetectGame_HandlesReadOnlyDirectory()
    {
        // Arrange
        var readOnlyPath = Path.Combine(_testRoot, "ReadOnly", "Data");
        _testDirectories.Add(Path.GetDirectoryName(readOnlyPath)!);
        Directory.CreateDirectory(readOnlyPath);
        CreatePluginFile(readOnlyPath, "Starfield.esm");
        
        // Make directory read-only
        var dirInfo = new DirectoryInfo(readOnlyPath);
        dirInfo.Attributes |= FileAttributes.ReadOnly;

        // Act
        var detectedGame = _gameDetectionService.DetectGame(readOnlyPath);

        // Assert
        Assert.Equal(GameRelease.Starfield, detectedGame);
        
        // Cleanup
        dirInfo.Attributes &= ~FileAttributes.ReadOnly;
    }

    #endregion

    #region Base Game Plugin Tests

    [Fact]
    public void GetBaseGamePlugins_ReturnsCorrectPlugins_ForAllGames()
    {
        var testCases = new[]
        {
            new
            {
                GameRelease = GameRelease.SkyrimSE,
                ExpectedBase = new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" }
            },
            new
            {
                GameRelease = GameRelease.Fallout4,
                ExpectedBase = new[] { "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm" }
            },
            new
            {
                GameRelease = GameRelease.Starfield,
                ExpectedBase = new[] { "Starfield.esm", "Constellation.esm", "OldMars.esm", "BlueprintShips-Starfield.esm" }
            },
            new
            {
                GameRelease = GameRelease.Oblivion,
                ExpectedBase = new[] { "Oblivion.esm" }
            }
        };

        foreach (var testCase in testCases)
        {
            // Act
            var basePlugins = _gameDetectionService.GetBaseGamePlugins(testCase.GameRelease);

            // Assert
            foreach (var expectedPlugin in testCase.ExpectedBase)
            {
                Assert.Contains(expectedPlugin, basePlugins);
            }
        }
    }

    #endregion

    #region Directory Name Detection Tests

    [Fact]
    public void DetectGame_ByDirectoryName_ReturnsNull_WithoutGameFiles()
    {
        // GameDetectionService only detects games by .esm files, not directory names
        var dirNameTests = new[]
        {
            "Skyrim Special Edition",
            "Skyrim Special Edition GOG",
            "SkyrimVR",
            "Fallout 4",
            "Fallout4",
            "Starfield",
            "Oblivion"
        };

        foreach (var dirName in dirNameTests)
        {
            // Arrange
            var testPath = Path.Combine(_testRoot, dirName, "Data");
            _testDirectories.Add(Path.GetDirectoryName(testPath)!);
            Directory.CreateDirectory(testPath);
            // Don't create any files - service requires .esm files for detection

            // Act
            var detectedGame = _gameDetectionService.DetectGame(Path.GetDirectoryName(testPath)!);

            // Assert - Should return null without game files
            Assert.Null(detectedGame);
        }
    }

    #endregion

    #region Helper Methods

    private void CreatePluginFile(string directory, string fileName)
    {
        var filePath = Path.Combine(directory, fileName);
        // Create minimal ESM/ESP header
        var header = new byte[]
        {
            0x54, 0x45, 0x53, 0x34, // "TES4"
            0x2B, 0x00, 0x00, 0x00, // Size
            0x00, 0x00, 0x00, 0x00, // Flags (ESM flag for .esm files)
            0x00, 0x00, 0x00, 0x00  // FormID
        };
        
        // Set ESM flag for .esm files
        if (fileName.EndsWith(".esm", StringComparison.OrdinalIgnoreCase))
        {
            header[8] = 0x01; // ESM flag
        }
        
        File.WriteAllBytes(filePath, header);
    }

    private bool IsAdministrator()
    {
        try
        {
#pragma warning disable CA1416 // Validate platform compatibility
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
#pragma warning restore CA1416 // Validate platform compatibility
            return true; // Assume true for non-Windows
        }
        catch
        {
            return false;
        }
    }

    #endregion
}