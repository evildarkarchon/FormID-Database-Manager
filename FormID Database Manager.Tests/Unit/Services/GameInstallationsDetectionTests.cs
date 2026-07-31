#nullable enable

using System;
using System.IO;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Detection rules exercised against the production probe adapter. These keep their temp-tree fixtures for now;
///     replacing them with an in-memory suite is issue #40, which is why the rules move here unchanged rather than
///     being rewritten in the same step that moves the code.
/// </summary>
public class GameInstallationsDetectionTests : IDisposable
{
    private readonly GameInstallations _gameInstallations;
    private readonly string _testDirectory;

    public GameInstallationsDetectionTests()
    {
        _gameInstallations = new GameInstallations(new GameInstallationProbe());
        _testDirectory = Path.Combine(Path.GetTempPath(), $"GameInstallationsDetectionTests_{Guid.NewGuid()}");
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
            var dirInfo = new DirectoryInfo(path) { Attributes = FileAttributes.Normal };

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

    private (string gamePath, GameDetectionData detectionData) CreateStructureFromBuilder(
        string testName,
        GameDetectionBuilder builder)
    {
        var gamePath = Path.Combine(_testDirectory, testName);
        var detectionData = builder.WithDirectory(gamePath).Build();
        var dataPath = Path.Combine(detectionData.DirectoryPath, "Data");
        CreateGameStructure(detectionData.DirectoryPath, dataPath, detectionData.PluginFiles.ToArray());
        return (gamePath, detectionData);
    }

    [Fact]
    public void Detect_ReturnsNull_WhenDirectoryDoesNotExist()
    {
        var result = _gameInstallations.Detect(Path.Combine(_testDirectory, "NonExistent"));

        Assert.Null(result);
    }

    [Fact]
    public void Detect_ReturnsNull_WhenNoGameFilesFound()
    {
        var gamePath = Path.Combine(_testDirectory, "EmptyGame");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath);

        var result = _gameInstallations.Detect(gamePath);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("Skyrim.esm", GameRelease.SkyrimLE)]
    [InlineData("Oblivion.esm", GameRelease.Oblivion)]
    [InlineData("Fallout4.esm", GameRelease.Fallout4)]
    [InlineData("Starfield.esm", GameRelease.Starfield)]
    public void Detect_DetectsGame_FromRootDirectory(string masterFile, GameRelease expectedGame)
    {
        var gamePath = Path.Combine(_testDirectory, $"Game_{expectedGame}");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, masterFile);

        var result = _gameInstallations.Detect(gamePath);

        Assert.Equal(expectedGame, result);
    }

    [Theory]
    [InlineData("Skyrim.esm", GameRelease.SkyrimLE)]
    [InlineData("Oblivion.esm", GameRelease.Oblivion)]
    [InlineData("Fallout4.esm", GameRelease.Fallout4)]
    [InlineData("Starfield.esm", GameRelease.Starfield)]
    public void Detect_DetectsGame_FromDataDirectory(string masterFile, GameRelease expectedGame)
    {
        var gamePath = Path.Combine(_testDirectory, $"Game_{expectedGame}_Data");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, masterFile);

        var result = _gameInstallations.Detect(dataPath);

        Assert.Equal(expectedGame, result);
    }

    /// <summary>
    ///     Verifies that the spellings canonicalization equalizes reach the same Game Installation.
    /// </summary>
    [Theory]
    [InlineData("\\")]
    [InlineData("/")]
    public void Detect_DataDirectoryWithTrailingSeparator_MatchesThePlainForm(string separator)
    {
        var gamePath = Path.Combine(_testDirectory, $"TrailingSeparator_{(int)separator[0]}");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, "Starfield.esm");

        Assert.Equal(GameRelease.Starfield, _gameInstallations.Detect(dataPath + separator));
        Assert.Equal(GameRelease.Starfield, _gameInstallations.Detect(gamePath + separator));
        Assert.Equal(
            GameRelease.Starfield,
            _gameInstallations.Detect(Path.Combine(dataPath, "unused", "..")));
    }

    [Fact]
    public void Detect_DetectsSkyrimVR_WhenVRExecutableExists()
    {
        var gamePath = Path.Combine(_testDirectory, "SkyrimVR");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, "Skyrim.esm");
        File.WriteAllText(Path.Combine(gamePath, "SkyrimVR.exe"), "");

        var result = _gameInstallations.Detect(gamePath);

        Assert.Equal(GameRelease.SkyrimVR, result);
    }

    [Fact]
    public void Detect_DetectsSkyrimVR_FromDataDirectory()
    {
        var gamePath = Path.Combine(_testDirectory, "SkyrimVR_Data");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, "Skyrim.esm");
        File.WriteAllText(Path.Combine(gamePath, "SkyrimVR.exe"), "");

        var result = _gameInstallations.Detect(dataPath);

        Assert.Equal(GameRelease.SkyrimVR, result);
    }

    [Fact]
    public void Detect_DetectsSkyrimSE_WhenSkyrimSEExecutableExists()
    {
        var gamePath = Path.Combine(_testDirectory, "SkyrimSE");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, "Skyrim.esm");
        File.WriteAllText(Path.Combine(gamePath, "SkyrimSE.exe"), string.Empty);

        var result = _gameInstallations.Detect(gamePath);

        Assert.Equal(GameRelease.SkyrimSE, result);
    }

    [Fact]
    public void Detect_DetectsSkyrimLE_UsingGameDetectionBuilder()
    {
        var builder = new GameDetectionBuilder()
            .WithGame(GameRelease.SkyrimLE)
            .AddPlugin("Skyrim.esm");
        var (gamePath, detectionData) = CreateStructureFromBuilder("SkyrimLE_Builder", builder);

        var result = _gameInstallations.Detect(gamePath);

        Assert.Equal(detectionData.ExpectedGame, result);
    }

    [Fact]
    public void Detect_DetectsEnderalSE_UsingGameDetectionBuilder()
    {
        var builder = new GameDetectionBuilder()
            .WithGame(GameRelease.EnderalSE)
            .AddPlugins("Skyrim.esm", "Enderal - Forgotten Stories.esm");
        var (gamePath, detectionData) = CreateStructureFromBuilder("EnderalSE_Builder", builder);
        File.WriteAllText(Path.Combine(gamePath, "SkyrimSE.exe"), string.Empty);

        var result = _gameInstallations.Detect(gamePath);

        Assert.Equal(detectionData.ExpectedGame, result);
    }

    [Fact]
    public void Detect_DetectsEnderalLE_UsingGameDetectionBuilder()
    {
        var builder = new GameDetectionBuilder()
            .WithGame(GameRelease.EnderalLE)
            .AddPlugins("Skyrim.esm", "Enderal - Forgotten Stories.esm");
        var (gamePath, detectionData) = CreateStructureFromBuilder("EnderalLE_Builder", builder);
        File.WriteAllText(Path.Combine(gamePath, "TESV.exe"), string.Empty);

        var result = _gameInstallations.Detect(gamePath);

        Assert.Equal(detectionData.ExpectedGame, result);
    }

    [Fact]
    public void Detect_DetectsSkyrimGOG_WhenGOGFilesExist()
    {
        var gamePath = Path.Combine(_testDirectory, "SkyrimGOG");
        var dataPath = Path.Combine(gamePath, "Data");
        var gogScriptsPath = Path.Combine(gamePath, "gogscripts");

        CreateGameStructure(gamePath, dataPath, "Skyrim.esm");
        Directory.CreateDirectory(gogScriptsPath);

        var result = _gameInstallations.Detect(gamePath);

        Assert.Equal(GameRelease.SkyrimSEGog, result);
    }

    [Fact]
    public void Detect_DetectsSkyrimGOG_WithGOGGameInfoFile()
    {
        var gamePath = Path.Combine(_testDirectory, "SkyrimGOG_Info");
        var dataPath = Path.Combine(gamePath, "Data");

        CreateGameStructure(gamePath, dataPath, "Skyrim.esm");
        File.WriteAllText(Path.Combine(gamePath, "goggame-1746476928.info"), "");

        var result = _gameInstallations.Detect(gamePath);

        Assert.Equal(GameRelease.SkyrimSEGog, result);
    }

    [Fact]
    public void Detect_DetectsFallout4VR_WhenVRExecutableExists()
    {
        var gamePath = Path.Combine(_testDirectory, "Fallout4VR");
        var dataPath = Path.Combine(gamePath, "Data");
        CreateGameStructure(gamePath, dataPath, "Fallout4.esm");
        File.WriteAllText(Path.Combine(gamePath, "Fallout4VR.exe"), "");

        var result = _gameInstallations.Detect(gamePath);

        Assert.Equal(GameRelease.Fallout4VR, result);
    }

    /// <summary>
    ///     Verifies the narrowed contract: a path detection cannot use at all throws instead of reporting the absence
    ///     of a game. This replaces the previous test, which asserted the old swallow-everything behaviour by way of a
    ///     read-only directory — a case the file-existence APIs never failed on anyway.
    /// </summary>
    /// <param name="malformedDirectory">A directory value detection cannot resolve to a Game Installation.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\Games\\Sky\0rim")]
    public void Detect_MalformedDirectory_ThrowsRatherThanReturningNull(string malformedDirectory)
    {
        Assert.Throws<ArgumentException>(() => _gameInstallations.Detect(malformedDirectory));
    }

    /// <summary>
    ///     Verifies that a missing directory value is rejected as the programming error it is, rather than read as a
    ///     game-less directory.
    /// </summary>
    [Fact]
    public void Detect_NullDirectory_ThrowsRatherThanReturningNull()
    {
        Assert.Throws<ArgumentNullException>(() => _gameInstallations.Detect(null!));
    }

    /// <summary>
    ///     Verifies that a directory the user cannot fully read still reports the absence of a game rather than
    ///     failing: the file-existence probe reports false, which is a game-less directory, not a malformed path.
    /// </summary>
    [Fact]
    public void Detect_ReadOnlyDirectoryWithoutGameFiles_ReturnsNull()
    {
        var restrictedPath = Path.Combine(_testDirectory, "RestrictedAccess");
        Directory.CreateDirectory(restrictedPath);

        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(restrictedPath, FileAttributes.ReadOnly);
        }

        var result = _gameInstallations.Detect(restrictedPath);

        Assert.Null(result);
    }

    // Base game Plugin coverage lives in BaseGamePluginsTests: the sets moved to the BaseGamePlugins constant
    // table, which detection no longer owns.

    [Theory]
    [InlineData("C:\\Steam\\steamapps\\common\\Skyrim Special Edition\\Data")]
    [InlineData("D:\\Games\\SkyrimSE\\Data")]
    [InlineData("/home/user/.steam/steam/steamapps/common/Skyrim Special Edition/Data")]
    public void Detect_HandlesVariousPathFormats(string pathPattern)
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

        var result = _gameInstallations.Detect(testPath);

        Assert.Equal(GameRelease.SkyrimLE, result);
    }
}
