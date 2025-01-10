using System.Collections.Generic;
using FormID_Database_Manager.Models;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Models;

public class ProcessingParameters
{
    public string? GameDirectory { get; set; }
    public string DatabasePath { get; set; } = string.Empty;
    public GameRelease GameRelease { get; set; }
    public List<PluginListItem> SelectedPlugins { get; set; } = new();
    public bool UpdateMode { get; set; }
    public bool DryRun { get; set; }
    public string? FormIdListPath { get; set; }
}