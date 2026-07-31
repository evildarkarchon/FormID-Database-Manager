// Services/GameDetectionService.cs

using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Provides services for detecting the game type from a directory, by checking for game master files.
/// </summary>
/// <remarks>
///     Base game Plugins are not here: they are a constant table in <c>BaseGamePlugins</c>, read directly by the
///     Plugin List, so a change to detection cannot affect Plugin List membership (ADR-0002).
/// </remarks>
public class GameDetectionService
{
    /// <summary>
    ///     Detects the game based on the specified game directory by checking for
    ///     the presence of game master files in the directory or its subdirectory.
    ///     Returns the detected game as a <see cref="GameRelease" /> enum, or null if no game is detected.
    /// </summary>
    /// <param name="gameDirectory">
    ///     The directory path to check for game master files.
    ///     It can be the "Data" directory or the game's root directory.
    /// </param>
    /// <returns>
    ///     A <see cref="GameRelease" /> enum value representing the detected game,
    ///     or null if the game could not be determined.
    /// </returns>
    public virtual GameRelease? DetectGame(string gameDirectory)
    {
        try
        {
            // One canonical Data directory covers both accepted inputs, so a game root and its Data directory —
            // however either is spelled — probe the same place. The game root is then path arithmetic on that result.
            var dataPath = GameInstallations.CanonicalizeDataDirectory(gameDirectory);

            if (Directory.Exists(dataPath))
            {
                var gameRoot = Path.GetDirectoryName(dataPath) ?? dataPath;
                return DetectGameFromDataDirectory(dataPath, gameRoot);
            }
        }
        catch (Exception)
        {
            // Return null if any error occurs during detection
        }

        return null;
    }

    private static GameRelease? DetectGameFromDataDirectory(string dataPath, string gameRoot)
    {
        if (File.Exists(Path.Combine(dataPath, "Skyrim.esm")))
        {
            return DetectSkyrimRelease(dataPath, gameRoot);
        }

        if (File.Exists(Path.Combine(dataPath, "Oblivion.esm")))
        {
            return GameRelease.Oblivion;
        }

        if (File.Exists(Path.Combine(dataPath, "Fallout4.esm")))
        {
            if (File.Exists(Path.Combine(gameRoot, "Fallout4VR.exe")))
            {
                return GameRelease.Fallout4VR;
            }

            return GameRelease.Fallout4;
        }

        if (File.Exists(Path.Combine(dataPath, "Starfield.esm")))
        {
            return GameRelease.Starfield;
        }

        return null;
    }

    private static GameRelease DetectSkyrimRelease(string dataPath, string gameRoot)
    {
        var hasSkyrimSeExecutable = File.Exists(Path.Combine(gameRoot, "SkyrimSE.exe"));

        if (File.Exists(Path.Combine(dataPath, "Enderal - Forgotten Stories.esm")))
        {
            if (hasSkyrimSeExecutable)
            {
                return GameRelease.EnderalSE;
            }

            if (File.Exists(Path.Combine(gameRoot, "TESV.exe")))
            {
                return GameRelease.EnderalLE;
            }
        }

        if (Directory.Exists(Path.Combine(gameRoot, "gogscripts")) ||
            File.Exists(Path.Combine(gameRoot, "goggame-1746476928.info")))
        {
            return GameRelease.SkyrimSEGog;
        }

        if (File.Exists(Path.Combine(gameRoot, "SkyrimVR.exe")))
        {
            return GameRelease.SkyrimVR;
        }

        return hasSkyrimSeExecutable ? GameRelease.SkyrimSE : GameRelease.SkyrimLE;
    }
}
