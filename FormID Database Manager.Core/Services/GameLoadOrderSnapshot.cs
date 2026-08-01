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

    /// <summary>
    ///     Builds the read parameters every Plugin in this snapshot is opened with.
    /// </summary>
    /// <param name="masterStyles">
    ///     The collected master styles, or <see langword="null" /> when this GameRelease needs no lookup at all.
    /// </param>
    /// <returns>Read parameters carrying a master-flags lookup only for a GameRelease that needs one.</returns>
    /// <remarks>
    ///     Null and empty are deliberately different facts here. Null means the GameRelease does not separate master
    ///     load orders, so no lookup applies. Empty means it does and the Data directory supplied nothing — and passing
    ///     that empty lookup through is what makes Mutagen name the master it could not resolve rather than report only
    ///     that no lookup existed, which is the name the run's failure message carries (issue #52).
    /// </remarks>
    private static BinaryReadParameters CreateReadParameters(IReadOnlyList<IModMasterStyledGetter>? masterStyles)
    {
        if (masterStyles == null)
        {
            return BinaryReadParameters.Default;
        }

        return new BinaryReadParameters { MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(masterStyles) };
    }
}
