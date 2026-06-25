using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

public sealed class GameLoadOrderSnapshot(
    IReadOnlyList<string> listedPluginNames,
    IReadOnlyList<IModMasterStyledGetter>? masterStyles = null)
{
    private readonly HashSet<string> _membership = new(listedPluginNames, StringComparer.OrdinalIgnoreCase);

    public static GameLoadOrderSnapshot Empty { get; } = new([]);

    public IReadOnlyList<string> ListedPluginNames { get; } = listedPluginNames;

    public IReadOnlyList<IModMasterStyledGetter>? MasterStyles { get; } = masterStyles;

    public BinaryReadParameters ReadParameters { get; } = CreateReadParameters(masterStyles);

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

        return new BinaryReadParameters { MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(masterStyles) };
    }
}
