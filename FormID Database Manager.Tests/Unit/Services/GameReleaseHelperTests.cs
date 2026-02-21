using System;
using System.IO;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class GameReleaseHelperTests
{
    [Theory]
    [InlineData(GameRelease.Oblivion, "Oblivion")]
    [InlineData(GameRelease.SkyrimLE, "SkyrimLE")]
    [InlineData(GameRelease.SkyrimSE, "SkyrimSE")]
    [InlineData(GameRelease.SkyrimSEGog, "SkyrimSEGog")]
    [InlineData(GameRelease.SkyrimVR, "SkyrimVR")]
    [InlineData(GameRelease.EnderalLE, "EnderalLE")]
    [InlineData(GameRelease.EnderalSE, "EnderalSE")]
    [InlineData(GameRelease.Fallout4, "Fallout4")]
    [InlineData(GameRelease.Fallout4VR, "Fallout4VR")]
    [InlineData(GameRelease.Starfield, "Starfield")]
    public void GetSafeTableName_SupportedRelease_ReturnsExpectedTableName(GameRelease release, string expected)
    {
        var tableName = GameReleaseHelper.GetSafeTableName(release);
        Assert.Equal(expected, tableName);
    }

    [Fact]
    public void GetSafeTableName_UnsupportedRelease_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => GameReleaseHelper.GetSafeTableName(GameRelease.OblivionRE));
        Assert.Contains("OblivionRE", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Data")]
    [InlineData("data")]
    public void ResolveDataPath_DataDirectoryInput_ReturnsUnchangedPath(string dataFolderName)
    {
        var gameDirectory = Path.Combine("C:\\Games", "SkyrimSE", dataFolderName);
        var resolved = GameReleaseHelper.ResolveDataPath(gameDirectory);
        Assert.Equal(gameDirectory, resolved);
    }

    [Fact]
    public void ResolveDataPath_GameRootInput_AppendsDataFolder()
    {
        var gameDirectory = Path.Combine("C:\\Games", "SkyrimSE");
        var resolved = GameReleaseHelper.ResolveDataPath(gameDirectory);
        Assert.Equal(Path.Combine(gameDirectory, "Data"), resolved);
    }
}
