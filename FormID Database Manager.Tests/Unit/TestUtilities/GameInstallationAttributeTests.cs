using System;
using System.Collections.Generic;
using System.Reflection;
using FormID_Database_Manager.TestUtilities;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.TestUtilities;

public class GameInstallationAttributeTests
{
    [Fact]
    public void RequiresGameInstallationFactAttribute_ExplicitGames_ChecksOnlyRequestedGames()
    {
        var checkedGames = new List<GameRelease>();
        var attribute = CreateRequiresGameInstallationAttribute<RequiresGameInstallationFactAttribute>(
            GameRelease.Oblivion,
            [GameRelease.Fallout4],
            game =>
            {
                checkedGames.Add(game);
                return false;
            });

        Assert.Equal([GameRelease.Oblivion, GameRelease.Fallout4], checkedGames);
        Assert.Equal("Requires one of the following games to be installed: Oblivion, Fallout4", attribute.Skip);
    }

    [Fact]
    public void RequiresGameInstallationTheoryAttribute_ExplicitGames_ChecksOnlyRequestedGames()
    {
        var checkedGames = new List<GameRelease>();
        var attribute = CreateRequiresGameInstallationAttribute<RequiresGameInstallationTheoryAttribute>(
            GameRelease.SkyrimSE,
            [GameRelease.Starfield],
            game =>
            {
                checkedGames.Add(game);
                return false;
            });

        Assert.Equal([GameRelease.SkyrimSE, GameRelease.Starfield], checkedGames);
        Assert.Equal("Requires one of the following games to be installed: SkyrimSE, Starfield", attribute.Skip);
    }

    [Fact]
    public void RequiresGameInstallationFactAttribute_ExplicitGameArray_PreservesSourceMetadata()
    {
        const string sourceFilePath = @"C:\tests\GameInstallationAttributeTests.cs";
        const int sourceLineNumber = 123;
        var attribute = CreateAttributeWithSourceMetadata<RequiresGameInstallationFactAttribute>(
            [GameRelease.Oblivion, GameRelease.Fallout4],
            sourceFilePath,
            sourceLineNumber);

        Assert.Equal(sourceFilePath, attribute.SourceFilePath);
        Assert.Equal(sourceLineNumber, attribute.SourceLineNumber);
    }

    [Fact]
    public void RequiresGameInstallationTheoryAttribute_ExplicitGameArray_PreservesSourceMetadata()
    {
        const string sourceFilePath = @"C:\tests\GameInstallationAttributeTests.cs";
        const int sourceLineNumber = 456;
        var attribute = CreateAttributeWithSourceMetadata<RequiresGameInstallationTheoryAttribute>(
            [GameRelease.SkyrimSE, GameRelease.Starfield],
            sourceFilePath,
            sourceLineNumber);

        Assert.Equal(sourceFilePath, attribute.SourceFilePath);
        Assert.Equal(sourceLineNumber, attribute.SourceLineNumber);
    }

    [Fact]
    public void ExpectsGameEnvironmentFailureFactAttribute_ExplicitGames_ChecksOnlyRequestedGames()
    {
        var checkedGames = new List<GameRelease>();
        var attribute = CreateExpectsGameEnvironmentFailureAttribute(
            [GameRelease.SkyrimVR, GameRelease.Oblivion],
            game =>
            {
                checkedGames.Add(game);
                return game == GameRelease.Oblivion;
            });

        Assert.Equal([GameRelease.SkyrimVR, GameRelease.Oblivion], checkedGames);
        Assert.Equal(
            "Test expects GameEnvironment failures but these games are installed: Oblivion",
            attribute.Skip);
    }

    private static TAttribute CreateAttributeWithSourceMetadata<TAttribute>(
        GameRelease[] games,
        string sourceFilePath,
        int sourceLineNumber)
        where TAttribute : Attribute
    {
        var constructor = typeof(TAttribute).GetConstructor([typeof(GameRelease[]), typeof(string), typeof(int)]);

        Assert.NotNull(constructor);
        return Assert.IsType<TAttribute>(constructor.Invoke([games, sourceFilePath, sourceLineNumber]));
    }

    private static TAttribute CreateRequiresGameInstallationAttribute<TAttribute>(
        GameRelease game,
        GameRelease[] additionalGames,
        Func<GameRelease, bool> isGameInstalled)
        where TAttribute : Attribute
    {
        var constructor = typeof(TAttribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(GameRelease), typeof(GameRelease[]), typeof(Func<GameRelease, bool>), typeof(string), typeof(int)],
            null);

        Assert.NotNull(constructor);
        return Assert.IsType<TAttribute>(constructor.Invoke([game, additionalGames, isGameInstalled, null, -1]));
    }

    private static ExpectsGameEnvironmentFailureFactAttribute CreateExpectsGameEnvironmentFailureAttribute(
        GameRelease[] games,
        Func<GameRelease, bool> isGameInstalled)
    {
        var constructor = typeof(ExpectsGameEnvironmentFailureFactAttribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(GameRelease[]), typeof(Func<GameRelease, bool>), typeof(string), typeof(int)],
            null);

        Assert.NotNull(constructor);
        return Assert.IsType<ExpectsGameEnvironmentFailureFactAttribute>(
            constructor.Invoke([games, isGameInstalled, null, -1]));
    }
}
