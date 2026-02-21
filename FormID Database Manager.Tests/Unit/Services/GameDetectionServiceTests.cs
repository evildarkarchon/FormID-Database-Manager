using System;
using System.IO;
using System.Linq;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class GameDetectionServiceTests : IDisposable
{
    private readonly GameDetectionService _service;
    private readonly string _testDirectory;

    public GameDetectionServiceTests()
    {
        _service = new GameDetectionService();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"GameDetectionTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                // Remove readonly attributes before deleting
                RemoveReadOnlyAttributes(_testDirectory);
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private void RemoveReadOnlyAttributes(string path)
    {
        try
        {
            var dirInfo = new DirectoryInfo(path);
            dirInfo.Attributes = FileAttributes.Normal;

            foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }

            foreach (var dir in dirInfo.GetDirectories("*", SearchOption.AllDirectories))
            {
                dir.Attributes = FileAttributes.Normal;
            }
        }
        catch
        {
            // Best effort
        }
    }

    private void CreateGameStructure(string gamePath, string dataPath, params string[] pluginFiles)
    {
        Directory.CreateDirectory(gamePath);
        Directory.CreateDirectory(dataPath);

        foreach (var plugin in pluginFiles)
        {
            File.WriteAllText(Path.Combine(dataPath, plugin), "");
        }
    }

    private (string gamePath, string dataPath, GameDetectionData detectionData) CreateStructureFromBuilder(
        string testName,
        GameDetectionBuilder builder)
    {
        var gamePath = Path.Combine(_testDirectory, testName);
        var detectionData = builder.WithDirectory(gamePath).Build();
        var dataPath = Path.Combine(detectionData.DirectoryPath, "Data");
        CreateGameStructure(detectionData.DirectoryPath, dataPath, detectionData.PluginFiles.ToArray());
        return (gamePath, dataPath, detectionData);
    }

    [Fact]
    public void DetectGame_ReturnsNull_WhenDirectoryDoesNotExist()
    {
        var result = _service.DetectGame(Path.Combine(_testDirectory, "NonExistent"));

        Assert.Null(result);
    }

    [Fact]
    public void DetectGame_ReturnsNull_WhenNoGameFilesFound()
    {
        var gamePath = Path.Combine(_testDirectory, "EmptyGame");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath);

        var result = _service.DetectGame(gamePath);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("Skyrim.esm", GameRelease.SkyrimLE)]
    [InlineData("Oblivion.esm", GameRelease.Oblivion)]
    [InlineData("Fallout4.esm", GameRelease.Fallout4)]
    [InlineData("Starfield.esm", GameRelease.Starfield)]
    public void DetectGame_DetectsGame_FromRootDirectory(string masterFile, GameRelease expectedGame)
    {
        var gamePath = Path.Combine(_testDirectory, $"Game_{expectedGame}");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, masterFile);

        var result = _service.DetectGame(gamePath);

        Assert.Equal(expectedGame, result);
    }

    [Theory]
    [InlineData("Skyrim.esm", GameRelease.SkyrimLE)]
    [InlineData("Oblivion.esm", GameRelease.Oblivion)]
    [InlineData("Fallout4.esm", GameRelease.Fallout4)]
    [InlineData("Starfield.esm", GameRelease.Starfield)]
    public void DetectGame_DetectsGame_FromDataDirectory(string masterFile, GameRelease expectedGame)
    {
        var gamePath = Path.Combine(_testDirectory, $"Game_{expectedGame}_Data");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, masterFile);

        var result = _service.DetectGame(dataPath);

        Assert.Equal(expectedGame, result);
    }

    [Fact]
    public void DetectGame_DetectsSkyrimVR_WhenVRExecutableExists()
    {
        var gamePath = Path.Combine(_testDirectory, "SkyrimVR");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, "Skyrim.esm");
        File.WriteAllText(Path.Combine(gamePath, "SkyrimVR.exe"), "");

        var result = _service.DetectGame(gamePath);

        Assert.Equal(GameRelease.SkyrimVR, result);
    }

    [Fact]
    public void DetectGame_DetectsSkyrimVR_FromDataDirectory()
    {
        var gamePath = Path.Combine(_testDirectory, "SkyrimVR_Data");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, "Skyrim.esm");
        File.WriteAllText(Path.Combine(gamePath, "SkyrimVR.exe"), "");

        var result = _service.DetectGame(dataPath);

        Assert.Equal(GameRelease.SkyrimVR, result);
    }

    [Fact]
    public void DetectGame_DetectsSkyrimSE_WhenSkyrimSEExecutableExists()
    {
        var gamePath = Path.Combine(_testDirectory, "SkyrimSE");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, "Skyrim.esm");
        File.WriteAllText(Path.Combine(gamePath, "SkyrimSE.exe"), string.Empty);

        var result = _service.DetectGame(gamePath);

        Assert.Equal(GameRelease.SkyrimSE, result);
    }

    [Fact]
    public void DetectGame_DetectsSkyrimLE_UsingGameDetectionBuilder()
    {
        var builder = new GameDetectionBuilder()
            .WithGame(GameRelease.SkyrimLE)
            .AddPlugin("Skyrim.esm");
        var (gamePath, _, detectionData) = CreateStructureFromBuilder("SkyrimLE_Builder", builder);

        var result = _service.DetectGame(gamePath);

        Assert.Equal(detectionData.ExpectedGame, result);
    }

    [Fact]
    public void DetectGame_DetectsEnderalSE_UsingGameDetectionBuilder()
    {
        var builder = new GameDetectionBuilder()
            .WithGame(GameRelease.EnderalSE)
            .AddPlugins("Skyrim.esm", "Enderal - Forgotten Stories.esm");
        var (gamePath, _, detectionData) = CreateStructureFromBuilder("EnderalSE_Builder", builder);
        File.WriteAllText(Path.Combine(gamePath, "SkyrimSE.exe"), string.Empty);

        var result = _service.DetectGame(gamePath);

        Assert.Equal(detectionData.ExpectedGame, result);
    }

    [Fact]
    public void DetectGame_DetectsEnderalLE_UsingGameDetectionBuilder()
    {
        var builder = new GameDetectionBuilder()
            .WithGame(GameRelease.EnderalLE)
            .AddPlugins("Skyrim.esm", "Enderal - Forgotten Stories.esm");
        var (gamePath, _, detectionData) = CreateStructureFromBuilder("EnderalLE_Builder", builder);
        File.WriteAllText(Path.Combine(gamePath, "TESV.exe"), string.Empty);

        var result = _service.DetectGame(gamePath);

        Assert.Equal(detectionData.ExpectedGame, result);
    }

    [Fact]
    public void DetectGame_DetectsSkyrimGOG_WhenGOGFilesExist()
    {
        var gamePath = Path.Combine(_testDirectory, "SkyrimGOG");
        var dataPath = Path.Combine(gamePath, "Data");
        var gogScriptsPath = Path.Combine(gamePath, "gogscripts");

        CreateGameStructure(gamePath, dataPath, "Skyrim.esm");
        Directory.CreateDirectory(gogScriptsPath);

        var result = _service.DetectGame(gamePath);

        Assert.Equal(GameRelease.SkyrimSEGog, result);
    }

    [Fact]
    public void DetectGame_DetectsSkyrimGOG_WithGOGGameInfoFile()
    {
        var gamePath = Path.Combine(_testDirectory, "SkyrimGOG_Info");
        var dataPath = Path.Combine(gamePath, "Data");

        CreateGameStructure(gamePath, dataPath, "Skyrim.esm");
        File.WriteAllText(Path.Combine(gamePath, "goggame-1746476928.info"), "");

        var result = _service.DetectGame(gamePath);

        Assert.Equal(GameRelease.SkyrimSEGog, result);
    }

    [Fact]
    public void DetectGame_DetectsFallout4VR_WhenVRExecutableExists()
    {
        var gamePath = Path.Combine(_testDirectory, "Fallout4VR");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, "Fallout4.esm");
        File.WriteAllText(Path.Combine(gamePath, "Fallout4VR.exe"), "");

        var result = _service.DetectGame(gamePath);

        Assert.Equal(GameRelease.Fallout4VR, result);
    }

    [Fact]
    public void DetectGame_HandlesExceptionGracefully()
    {
        var restrictedPath = Path.Combine(_testDirectory, "RestrictedAccess");
        Directory.CreateDirectory(restrictedPath);

        if (OperatingSystem.IsWindows())
        {
            var dirInfo = new DirectoryInfo(restrictedPath);
            dirInfo.Attributes = FileAttributes.ReadOnly;
        }

        var result = _service.DetectGame(restrictedPath);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(GameRelease.SkyrimSE, "Skyrim.esm")]
    [InlineData(GameRelease.SkyrimVR, "Skyrim.esm")]
    [InlineData(GameRelease.SkyrimSEGog, "Skyrim.esm")]
    [InlineData(GameRelease.Oblivion, "Oblivion.esm")]
    [InlineData(GameRelease.Fallout4, "Fallout4.esm")]
    [InlineData(GameRelease.Fallout4VR, "Fallout4.esm")]
    [InlineData(GameRelease.Starfield, "Starfield.esm")]
    public void GetBaseGamePlugins_ReturnsCorrectPlugins(GameRelease gameRelease, string expectedPlugin)
    {
        var plugins = _service.GetBaseGamePlugins(gameRelease);

        Assert.NotEmpty(plugins);
        Assert.Contains(expectedPlugin, plugins);
    }

    [Fact]
    public void GetBaseGamePlugins_ReturnsSkyrimPlugins_ForAllSkyrimVariants()
    {
        var skyrimSE = _service.GetBaseGamePlugins(GameRelease.SkyrimSE);
        var skyrimVR = _service.GetBaseGamePlugins(GameRelease.SkyrimVR);
        var skyrimGOG = _service.GetBaseGamePlugins(GameRelease.SkyrimSEGog);

        Assert.Equal(skyrimSE, skyrimVR);
        Assert.Equal(skyrimSE, skyrimGOG);
        Assert.Contains("Skyrim.esm", skyrimSE);
        Assert.Contains("Update.esm", skyrimSE);
        Assert.Contains("Dawnguard.esm", skyrimSE);
        Assert.Contains("HearthFires.esm", skyrimSE);
        Assert.Contains("Dragonborn.esm", skyrimSE);
    }

    [Fact]
    public void GetBaseGamePlugins_ReturnsFalloutPlugins_ForAllFalloutVariants()
    {
        var fallout4 = _service.GetBaseGamePlugins(GameRelease.Fallout4);
        var fallout4VR = _service.GetBaseGamePlugins(GameRelease.Fallout4VR);

        Assert.Equal(fallout4, fallout4VR);
        Assert.Contains("Fallout4.esm", fallout4);
        Assert.Contains("DLCRobot.esm", fallout4);
        Assert.Contains("DLCCoast.esm", fallout4);
        Assert.Contains("DLCNukaWorld.esm", fallout4);
    }

    [Fact]
    public void GetBaseGamePlugins_ReturnsStarfieldPlugins()
    {
        var plugins = _service.GetBaseGamePlugins(GameRelease.Starfield);

        Assert.Contains("Starfield.esm", plugins);
        Assert.Contains("BlueprintShips-Starfield.esm", plugins);
        Assert.Contains("OldMars.esm", plugins);
        Assert.Contains("Constellation.esm", plugins);
    }

    [Fact]
    public void GetBaseGamePlugins_ReturnsOblivionPlugins()
    {
        var plugins = _service.GetBaseGamePlugins(GameRelease.Oblivion);

        Assert.Contains("Oblivion.esm", plugins);
        Assert.Contains("DLCShiveringIsles.esp", plugins);
        Assert.Contains("Knights.esp", plugins);
    }

    [Fact]
    public void GetBaseGamePlugins_ReturnsEmptySet_ForUnsupportedGame()
    {
        var unsupportedGame = (GameRelease)999;
        var plugins = _service.GetBaseGamePlugins(unsupportedGame);

        Assert.Empty(plugins);
    }

    [Fact]
    public void GetBaseGamePlugins_PluginsAreCaseInsensitive()
    {
        var plugins = _service.GetBaseGamePlugins(GameRelease.SkyrimSE);

        Assert.Contains("skyrim.esm", plugins, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SKYRIM.ESM", plugins, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Skyrim.ESM", plugins, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("C:\\Steam\\steamapps\\common\\Skyrim Special Edition\\Data")]
    [InlineData("D:\\Games\\SkyrimSE\\Data")]
    [InlineData("/home/user/.steam/steam/steamapps/common/Skyrim Special Edition/Data")]
    public void DetectGame_HandlesVariousPathFormats(string pathPattern)
    {
        if (!OperatingSystem.IsWindows() && pathPattern.Contains(":\\"))
        {
            return;
        }

        if (OperatingSystem.IsWindows() && pathPattern.StartsWith("/"))
        {
            return;
        }

        var testPath = Path.Combine(_testDirectory, "PathTest", "Data");
        CreateGameStructure(Path.GetDirectoryName(testPath)!, testPath, "Skyrim.esm");

        var result = _service.DetectGame(testPath);

        Assert.Equal(GameRelease.SkyrimLE, result);
    }
}
