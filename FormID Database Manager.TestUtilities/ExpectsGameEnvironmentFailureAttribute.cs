using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Xunit;

namespace FormID_Database_Manager.TestUtilities;

/// <summary>
///     Skip tests that expect failures when games aren't installed, if the games ARE actually installed.
///     These tests are designed to verify error handling when GameEnvironment fails.
/// </summary>
public sealed class ExpectsGameEnvironmentFailureFactAttribute : FactAttribute
{
    private readonly GameRelease[] _games;

    public ExpectsGameEnvironmentFailureFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : this(GetDefaultGames(), sourceFilePath, sourceLineNumber)
    {
    }

    public ExpectsGameEnvironmentFailureFactAttribute(GameRelease game, params GameRelease[] additionalGames)
        : this(BuildGameList(game, additionalGames), null, -1)
    {
    }

    private ExpectsGameEnvironmentFailureFactAttribute(
        GameRelease[] games,
        string? sourceFilePath,
        int sourceLineNumber)
        : base(sourceFilePath, sourceLineNumber)
    {
        _games = games;
        // Check if any of the games are actually installed
        var installedGames = new List<GameRelease>();
        foreach (var game in _games)
        {
            if (IsGameInstalled(game))
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

    private static bool IsGameInstalled(GameRelease gameRelease)
    {
        try
        {
            var env = GameEnvironment.Typical.Construct(gameRelease);
            return env != null && Directory.Exists(env.DataFolderPath);
        }
        catch
        {
            return false;
        }
    }
}
