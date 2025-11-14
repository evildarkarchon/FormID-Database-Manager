using System;
using System.Collections.Generic;
using System.IO;
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

    public ExpectsGameEnvironmentFailureFactAttribute(params GameRelease[] games)
    {
        _games = games ?? Array.Empty<GameRelease>();

        if (_games.Length == 0)
        {
            _games = new[] { GameRelease.SkyrimSE, GameRelease.Fallout4, GameRelease.Starfield, GameRelease.Oblivion };
        }

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
