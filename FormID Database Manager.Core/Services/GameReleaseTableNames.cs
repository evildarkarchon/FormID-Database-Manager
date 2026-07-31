using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     The whitelist of SQLite table names for supported game releases.
/// </summary>
internal static class GameReleaseTableNames
{
    /// <summary>
    /// Returns the whitelisted SQLite table name for a supported game release.
    /// </summary>
    /// <param name="release">The game release whose table name is needed.</param>
    /// <returns>The safe table name used by database commands.</returns>
    /// <exception cref="ArgumentException">Thrown when the game release is not supported by the database schema.</exception>
    internal static string GetSafeTableName(GameRelease release) => release switch
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
}
