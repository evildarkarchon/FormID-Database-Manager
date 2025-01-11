﻿// Services/GameDetectionService.cs

using Mutagen.Bethesda;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FormID_Database_Manager.Services;

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

    public HashSet<string> GetBaseGamePlugins(GameRelease gameRelease) => gameRelease switch
    {
        GameRelease.SkyrimSE => _skyrimPlugins,
        GameRelease.Fallout4 => _falloutPlugins,
        GameRelease.Starfield => _starfieldPlugins,
        _ => new HashSet<string>()
    };
}