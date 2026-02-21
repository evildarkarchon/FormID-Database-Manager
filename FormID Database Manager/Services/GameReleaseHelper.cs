using System;
using System.IO;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

internal static class GameReleaseHelper
{
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

    internal static string ResolveDataPath(string gameDirectory)
    {
        return Path.GetFileName(gameDirectory).Equals("Data", StringComparison.OrdinalIgnoreCase)
            ? gameDirectory
            : Path.Combine(gameDirectory, "Data");
    }
}
