using System;
using System.Collections.Generic;
using System.IO;
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

    public RequiresGameInstallationFactAttribute(params GameRelease[] requiredGames)
    {
        _requiredGames = requiredGames ?? Array.Empty<GameRelease>();

        if (_requiredGames.Length == 0)
        {
            // If no specific games specified, check for any common Bethesda game
            _requiredGames = new[]
            {
                GameRelease.SkyrimSE, GameRelease.SkyrimVR, GameRelease.Fallout4, GameRelease.Starfield,
                GameRelease.Oblivion
            };
        }

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

    public RequiresGameInstallationTheoryAttribute(params GameRelease[] requiredGames)
    {
        _requiredGames = requiredGames ?? Array.Empty<GameRelease>();

        if (_requiredGames.Length == 0)
        {
            _requiredGames = new[]
            {
                GameRelease.SkyrimSE, GameRelease.SkyrimVR, GameRelease.Fallout4, GameRelease.Starfield,
                GameRelease.Oblivion
            };
        }

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
