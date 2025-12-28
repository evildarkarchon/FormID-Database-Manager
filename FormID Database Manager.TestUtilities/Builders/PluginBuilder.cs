using System.Collections.Generic;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.TestUtilities.Builders;

public class PluginBuilder
{
    private readonly List<(string FormId, string EditorId)> _entries = [];
    private GameRelease _gameRelease = GameRelease.SkyrimSE;
    private string _name = "TestPlugin.esp";

    public PluginBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public PluginBuilder WithGameRelease(GameRelease gameRelease)
    {
        _gameRelease = gameRelease;
        return this;
    }

    public PluginBuilder AddEntry(string formId, string editorId)
    {
        _entries.Add((formId, editorId));
        return this;
    }

    public PluginBuilder AddEntries(int count, string prefix = "Test")
    {
        for (var i = 0; i < count; i++)
        {
            _entries.Add(($"0x{i:X8}", $"{prefix}{i:D4}"));
        }

        return this;
    }

    public PluginBuilder AddNpc(string formId, string name)
    {
        _entries.Add((formId, $"NPC_{name}"));
        return this;
    }

    public PluginBuilder AddWeapon(string formId, string name)
    {
        _entries.Add((formId, $"WEAP_{name}"));
        return this;
    }

    public PluginBuilder AddArmor(string formId, string name)
    {
        _entries.Add((formId, $"ARMO_{name}"));
        return this;
    }

    public PluginBuilder AddCell(string formId, string name)
    {
        _entries.Add((formId, $"CELL_{name}"));
        return this;
    }

    public PluginData Build()
    {
        return new PluginData
        {
            Name = _name,
            GameRelease = _gameRelease,
            Entries = new List<(string FormId, string EditorId)>(_entries)
        };
    }
}

public class PluginData
{
    public string Name { get; set; } = string.Empty;
    public GameRelease GameRelease { get; set; }
    public List<(string FormId, string EditorId)> Entries { get; set; } = [];
}
