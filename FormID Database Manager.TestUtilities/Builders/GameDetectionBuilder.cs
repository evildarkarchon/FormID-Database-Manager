using System.Collections.Generic;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.TestUtilities.Builders;

public class GameDetectionBuilder
{
    private readonly List<string> _pluginFiles = new();
    private string _directoryPath = "C:\\Games\\TestGame";
    private GameRelease _expectedGame = GameRelease.SkyrimSE;

    public GameDetectionBuilder WithDirectory(string path)
    {
        _directoryPath = path;
        return this;
    }

    public GameDetectionBuilder WithGame(GameRelease game)
    {
        _expectedGame = game;
        return this;
    }

    public GameDetectionBuilder AddPlugin(string fileName)
    {
        _pluginFiles.Add(fileName);
        return this;
    }

    public GameDetectionBuilder AddPlugins(params string[] fileNames)
    {
        _pluginFiles.AddRange(fileNames);
        return this;
    }

    public GameDetectionBuilder SetupForSkyrimSE()
    {
        _expectedGame = GameRelease.SkyrimSE;
        _pluginFiles.Clear();
        _pluginFiles.AddRange(
            new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" });
        return this;
    }

    public GameDetectionBuilder SetupForSkyrimVR()
    {
        _expectedGame = GameRelease.SkyrimVR;
        _pluginFiles.Clear();
        _pluginFiles.AddRange(new[] { "Skyrim.esm", "Update.esm", "SkyrimVR.esm" });
        return this;
    }

    public GameDetectionBuilder SetupForFallout4()
    {
        _expectedGame = GameRelease.Fallout4;
        _pluginFiles.Clear();
        _pluginFiles.AddRange(new[] { "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm" });
        return this;
    }

    public GameDetectionBuilder SetupForStarfield()
    {
        _expectedGame = GameRelease.Starfield;
        _pluginFiles.Clear();
        _pluginFiles.AddRange(new[] { "Starfield.esm", "BlueprintShips-Starfield.esm" });
        return this;
    }

    public GameDetectionBuilder SetupForOblivion()
    {
        _expectedGame = GameRelease.Oblivion;
        _pluginFiles.Clear();
        _pluginFiles.AddRange(new[] { "Oblivion.esm" });
        return this;
    }

    public GameDetectionData Build()
    {
        return new GameDetectionData
        {
            DirectoryPath = _directoryPath,
            PluginFiles = new List<string>(_pluginFiles),
            ExpectedGame = _expectedGame
        };
    }
}

public class GameDetectionData
{
    public string DirectoryPath { get; set; } = string.Empty;
    public List<string> PluginFiles { get; set; } = new();
    public GameRelease ExpectedGame { get; set; }
}
