using System.Collections.Generic;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.TestUtilities.Builders;

public class GameDetectionBuilder
{
    private readonly List<string> _pluginFiles = [];
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

    public GameDetectionData Build()
    {
        return new GameDetectionData
        {
            DirectoryPath = _directoryPath, PluginFiles = [.._pluginFiles], ExpectedGame = _expectedGame
        };
    }
}

public class GameDetectionData
{
    public string DirectoryPath { get; set; } = string.Empty;
    public List<string> PluginFiles { get; set; } = [];
    public GameRelease ExpectedGame { get; set; }
}
