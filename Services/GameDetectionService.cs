// Services/GameDetectionService.cs

using Mutagen.Bethesda;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Provides services for detecting the game type and retrieving game-specific data.
/// This includes checking directories for game master files to determine the game version
/// and retrieving base game plugins for supported games.
/// </summary>
public class GameDetectionService
{
    private readonly HashSet<string> _skyrimPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm",
        "Dragonborn.esm", "ccBGSSSE001-Fish.esm", "ccQDRSSE001-SurvivalMode.esm"
    };

    private readonly HashSet<string> _falloutPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm",
        "DLCCoast.esm", "DLCworkshop02.esm", "DLCworkshop03.esm",
        "DLCNukaWorld.esm"
    };

    private readonly HashSet<string> _starfieldPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "Starfield.esm", "BlueprintShips-Starfield.esm",
        "OldMars.esm", "Constellation.esm"
    };

    /// <summary>
    /// Detects the game based on the specified game directory by checking for
    /// the presence of game master files in the directory or its subdirectory.
    /// Returns the detected game as a <see cref="GameRelease"/> enum, or null if no game is detected.
    /// </summary>
    /// <param name="gameDirectory">The directory path to check for game master files.
    /// It can be the "Data" directory or the game's root directory.</param>
    /// <returns>A <see cref="GameRelease"/> enum value representing the detected game,
    /// or null if the game could not be determined.</returns>
    public Task<GameRelease?> DetectGame(string gameDirectory)
    {
        return Task.Run(new Func<GameRelease?>(() =>
        {
            try
            {
                // If this is already a data directory, use it directly
                if (Path.GetFileName(gameDirectory).Equals("Data", StringComparison.OrdinalIgnoreCase))
                {
                    // Check for game-specific master files in the current directory
                    if (File.Exists(Path.Combine(gameDirectory, "Skyrim.esm")))
                        return GameRelease.SkyrimSE;
                    if (File.Exists(Path.Combine(gameDirectory, "Fallout4.esm")))
                        return GameRelease.Fallout4;
                    if (File.Exists(Path.Combine(gameDirectory, "Starfield.esm")))
                        return GameRelease.Starfield;
                }
                else
                {
                    // Check in the Data subdirectory
                    var dataPath = Path.Combine(gameDirectory, "Data");
                    if (Directory.Exists(dataPath))
                    {
                        if (File.Exists(Path.Combine(dataPath, "Skyrim.esm")))
                            return GameRelease.SkyrimSE;
                        if (File.Exists(Path.Combine(dataPath, "Fallout4.esm")))
                            return GameRelease.Fallout4;
                        if (File.Exists(Path.Combine(dataPath, "Starfield.esm")))
                            return GameRelease.Starfield;
                    }
                }
            }
            catch (Exception)
            {
                // Return null if any error occurs during detection
            }

            return null;
        }));
    }

    /// <summary>
    /// Retrieves the base game plugins for the specified game release.
    /// The base game plugins are predefined for each supported game version and include essential game master files.
    /// </summary>
    /// <param name="gameRelease">The <see cref="GameRelease"/> value representing the target game version.</param>
    /// <returns>A <see cref="HashSet{T}"/> containing the base game plugin filenames for the specified game release.
    /// Returns an empty set if the game release is unsupported.</returns>
    public HashSet<string> GetBaseGamePlugins(GameRelease gameRelease) => gameRelease switch
    {
        GameRelease.SkyrimSE => _skyrimPlugins,
        GameRelease.Fallout4 => _falloutPlugins,
        GameRelease.Starfield => _starfieldPlugins,
        _ => new HashSet<string>()
    };
}