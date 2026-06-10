using System;
using System.Reflection;
using FormID_Database_Manager.TestUtilities;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.TestUtilities;

public class GameInstallationAttributeTests
{
    [Fact]
    public void RequiresGameInstallationFactAttribute_ExplicitGames_StoresOnlyRequestedGames()
    {
        var attribute = new RequiresGameInstallationFactAttribute(GameRelease.Oblivion, GameRelease.Fallout4);

        var games = GetPrivateGameList(attribute, "_requiredGames");

        Assert.Equal(new[] { GameRelease.Oblivion, GameRelease.Fallout4 }, games);
    }

    [Fact]
    public void RequiresGameInstallationTheoryAttribute_ExplicitGames_StoresOnlyRequestedGames()
    {
        var attribute = new RequiresGameInstallationTheoryAttribute(GameRelease.SkyrimSE, GameRelease.Starfield);

        var games = GetPrivateGameList(attribute, "_requiredGames");

        Assert.Equal(new[] { GameRelease.SkyrimSE, GameRelease.Starfield }, games);
    }

    [Fact]
    public void RequiresGameInstallationFactAttribute_ExplicitGameArray_PreservesSourceMetadata()
    {
        const string sourceFilePath = @"C:\tests\GameInstallationAttributeTests.cs";
        const int sourceLineNumber = 123;
        var attribute = new RequiresGameInstallationFactAttribute(
            [GameRelease.Oblivion, GameRelease.Fallout4],
            sourceFilePath,
            sourceLineNumber);

        var games = GetPrivateGameList(attribute, "_requiredGames");

        Assert.Equal(new[] { GameRelease.Oblivion, GameRelease.Fallout4 }, games);
        Assert.Equal(sourceFilePath, attribute.SourceFilePath);
        Assert.Equal(sourceLineNumber, attribute.SourceLineNumber);
    }

    [Fact]
    public void RequiresGameInstallationTheoryAttribute_ExplicitGameArray_PreservesSourceMetadata()
    {
        const string sourceFilePath = @"C:\tests\GameInstallationAttributeTests.cs";
        const int sourceLineNumber = 456;
        var attribute = new RequiresGameInstallationTheoryAttribute(
            [GameRelease.SkyrimSE, GameRelease.Starfield],
            sourceFilePath,
            sourceLineNumber);

        var games = GetPrivateGameList(attribute, "_requiredGames");

        Assert.Equal(new[] { GameRelease.SkyrimSE, GameRelease.Starfield }, games);
        Assert.Equal(sourceFilePath, attribute.SourceFilePath);
        Assert.Equal(sourceLineNumber, attribute.SourceLineNumber);
    }

    [Fact]
    public void ExpectsGameEnvironmentFailureFactAttribute_ExplicitGames_StoresOnlyRequestedGames()
    {
        var attribute = new ExpectsGameEnvironmentFailureFactAttribute(GameRelease.SkyrimVR, GameRelease.Oblivion);

        var games = GetPrivateGameList(attribute, "_games");

        Assert.Equal(new[] { GameRelease.SkyrimVR, GameRelease.Oblivion }, games);
    }

    private static GameRelease[] GetPrivateGameList(Attribute attribute, string fieldName)
    {
        var field = attribute.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        return Assert.IsType<GameRelease[]>(field.GetValue(attribute));
    }
}
