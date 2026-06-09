using System;
using System.Collections.Generic;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

public sealed class GameLoadOrderSnapshot
{
    private readonly HashSet<string> _membership;

    public static GameLoadOrderSnapshot Empty { get; } = new([]);

    public GameLoadOrderSnapshot(
        IReadOnlyList<string> listedPluginNames,
        IReadOnlyList<IModMasterStyledGetter>? masterStyles = null)
    {
        ListedPluginNames = listedPluginNames;
        MasterStyles = masterStyles;
        _membership = new HashSet<string>(listedPluginNames, StringComparer.OrdinalIgnoreCase);
        ReadParameters = CreateReadParameters(masterStyles);
    }

    public IReadOnlyList<string> ListedPluginNames { get; }

    public IReadOnlyList<IModMasterStyledGetter>? MasterStyles { get; }

    public BinaryReadParameters ReadParameters { get; }

    public bool ContainsPlugin(string pluginName)
    {
        return _membership.Contains(pluginName);
    }

    private static BinaryReadParameters CreateReadParameters(IReadOnlyList<IModMasterStyledGetter>? masterStyles)
    {
        if (masterStyles == null || masterStyles.Count == 0)
        {
            return BinaryReadParameters.Default;
        }

        return new BinaryReadParameters
        {
            MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(masterStyles)
        };
    }
}
