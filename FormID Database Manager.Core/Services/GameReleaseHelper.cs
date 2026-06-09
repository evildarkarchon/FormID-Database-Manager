using System;
using System.IO;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

public static class GameReleaseHelper
{
    /// <summary>
    /// Returns the whitelisted SQLite table name for a supported game release.
    /// </summary>
    /// <param name="release">The game release whose table name is needed.</param>
    /// <returns>The safe table name used by database commands.</returns>
    /// <exception cref="ArgumentException">Thrown when the game release is not supported by the database schema.</exception>
    public static string GetSafeTableName(GameRelease release) => release switch
    {
        GameRelease.SkyrimSE => "SkyrimSE",
        GameRelease.SkyrimSEGog => "SkyrimSEGog",
        GameRelease.SkyrimVR => "SkyrimVR",
        GameRelease.SkyrimLE => "SkyrimLE",
        GameRelease.Fallout4 => "Fallout4",
        GameRelease.Fallout4VR => "Fallout4VR",
        GameRelease.Starfield => "Starfield",
        GameRelease.Oblivion => "Oblivion",
        GameRelease.EnderalLE => "EnderalLE",
        GameRelease.EnderalSE => "EnderalSE",
        _ => throw new ArgumentException($"Unsupported game release: {release}", nameof(release))
    };

    /// <summary>
    /// Resolves a game root or data directory path to the data directory used for plugin lookup.
    /// </summary>
    /// <param name="gameDirectory">The selected game root or existing Data directory.</param>
    /// <returns>The path to the Data directory.</returns>
    public static string ResolveDataPath(string gameDirectory)
    {
        return Path.GetFileName(gameDirectory).Equals("Data", StringComparison.OrdinalIgnoreCase)
            ? gameDirectory
            : Path.Combine(gameDirectory, "Data");
    }
}
