using System.Collections.Generic;
using System.Linq;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Performance;

/// <summary>
///     Supplies a deterministic Plugin load order for performance scenarios that generate their own Plugin files.
/// </summary>
internal sealed class StaticGameLoadOrderProvider(IEnumerable<string> pluginNames) : IGameLoadOrderProvider
{
    private readonly IReadOnlyList<string> _pluginNames = pluginNames.ToArray();

    /// <inheritdoc />
    public GameLoadOrderSnapshot BuildSnapshot(
        GameRelease gameRelease,
        string dataPath,
        bool includeMasterFlagsLookup = false)
    {
        return new GameLoadOrderSnapshot(_pluginNames);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetListedPluginNames(GameRelease gameRelease, string dataPath)
    {
        return _pluginNames;
    }
}
