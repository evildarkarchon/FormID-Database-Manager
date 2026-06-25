using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.TestUtilities;

/// <summary>
///     Skip tests that expect failures when games aren't installed, if the games ARE actually installed.
///     These tests are designed to verify error handling when GameEnvironment fails.
/// </summary>
public sealed class ExpectsGameEnvironmentFailureFactAttribute : FactAttribute
{
    public ExpectsGameEnvironmentFailureFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : this(GetDefaultGames(), GameInstallationHelper.IsGameInstalled, sourceFilePath, sourceLineNumber)
    {
    }

    public ExpectsGameEnvironmentFailureFactAttribute(GameRelease game, params GameRelease[] additionalGames)
        : this(BuildGameList(game, additionalGames), GameInstallationHelper.IsGameInstalled, null, -1)
    {
    }

    // xUnit stores source metadata in caller-info parameters; forwarding captured values is intentional here.
    // ReSharper disable ExplicitCallerInfoArgument
    private ExpectsGameEnvironmentFailureFactAttribute(
        GameRelease[] games,
        Func<GameRelease, bool> isGameInstalled,
        string? sourceFilePath,
        int sourceLineNumber)
        : base(sourceFilePath, sourceLineNumber)
    {
        // Check if any of the games are actually installed
        var installedGames = new List<GameRelease>();
        foreach (var game in games)
        {
            if (isGameInstalled(game))
            {
                installedGames.Add(game);
            }
        }

        if (installedGames.Count > 0)
        {
            Skip =
                $"Test expects GameEnvironment failures but these games are installed: {string.Join(", ", installedGames)}";
        }
    }
    // ReSharper restore ExplicitCallerInfoArgument

    private static GameRelease[] GetDefaultGames()
    {
        return [GameRelease.SkyrimSE, GameRelease.Fallout4, GameRelease.Starfield, GameRelease.Oblivion];
    }

    private static GameRelease[] BuildGameList(GameRelease firstGame, GameRelease[] additionalGames)
    {
        var games = new GameRelease[additionalGames.Length + 1];
        games[0] = firstGame;
        Array.Copy(additionalGames, 0, games, 1, additionalGames.Length);
        return games;
    }
}
