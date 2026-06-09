using System.Collections.Generic;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Models;

public class ProcessingParameters
{
    public string? GameDirectory { get; init; }
    public string DatabasePath { get; set; } = string.Empty;
    public GameRelease GameRelease { get; init; }
    public List<PluginListItem> SelectedPlugins { get; init; } = [];
    public bool UpdateMode { get; init; }
    public bool DryRun { get; set; }
    public string? FormIdListPath { get; init; }
}
