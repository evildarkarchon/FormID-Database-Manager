using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Xunit;

namespace FormID_Database_Manager.TestUtilities;

/// <summary>
///     Skip tests that require actual game installations when the games are not present.
/// </summary>
public sealed class RequiresGameInstallationFactAttribute : FactAttribute
{
    private readonly GameRelease[] _requiredGames;

    public RequiresGameInstallationFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : this(GetDefaultRequiredGames(), sourceFilePath, sourceLineNumber)
    {
    }

    public RequiresGameInstallationFactAttribute(GameRelease requiredGame, params GameRelease[] additionalRequiredGames)
        : this(BuildGameList(requiredGame, additionalRequiredGames), null, -1)
    {
    }

    public RequiresGameInstallationFactAttribute(
        GameRelease[] requiredGames,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        _requiredGames = requiredGames;
        var availableGames = new List<GameRelease>();
        foreach (var game in _requiredGames)
        {
            if (IsGameInstalled(game))
            {
                availableGames.Add(game);
            }
        }

        if (availableGames.Count == 0)
        {
            Skip = $"Requires one of the following games to be installed: {string.Join(", ", _requiredGames)}";
        }
    }

    private static GameRelease[] GetDefaultRequiredGames()
    {
        // If no specific games are requested, check for any common Bethesda game.
        return
        [
            GameRelease.SkyrimSE, GameRelease.SkyrimVR, GameRelease.Fallout4, GameRelease.Starfield,
            GameRelease.Oblivion
        ];
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
            // Try to get the game environment - this will fail if the game isn't installed
            var env = GameEnvironment.Typical.Construct(gameRelease);
            return env != null && Directory.Exists(env.DataFolderPath);
        }
        catch
        {
            // If we can't construct the environment, the game isn't installed
            return false;
        }
    }
}

/// <summary>
///     Skip theory tests that require actual game installations when the games are not present.
/// </summary>
public sealed class RequiresGameInstallationTheoryAttribute : TheoryAttribute
{
    private readonly GameRelease[] _requiredGames;

    public RequiresGameInstallationTheoryAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : this(GetDefaultRequiredGames(), sourceFilePath, sourceLineNumber)
    {
    }

    public RequiresGameInstallationTheoryAttribute(GameRelease requiredGame, params GameRelease[] additionalRequiredGames)
        : this(BuildGameList(requiredGame, additionalRequiredGames), null, -1)
    {
    }

    public RequiresGameInstallationTheoryAttribute(
        GameRelease[] requiredGames,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        _requiredGames = requiredGames;
        var availableGames = new List<GameRelease>();
        foreach (var game in _requiredGames)
        {
            if (IsGameInstalled(game))
            {
                availableGames.Add(game);
            }
        }

        if (availableGames.Count == 0)
        {
            Skip = $"Requires one of the following games to be installed: {string.Join(", ", _requiredGames)}";
        }
    }

    private static GameRelease[] GetDefaultRequiredGames()
    {
        return
        [
            GameRelease.SkyrimSE, GameRelease.SkyrimVR, GameRelease.Fallout4, GameRelease.Starfield,
            GameRelease.Oblivion
        ];
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
