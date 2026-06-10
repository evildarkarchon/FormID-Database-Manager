using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

public class GameLoadOrderProvider : IGameLoadOrderProvider
{
    private readonly Func<GameRelease, string, IReadOnlyList<ILoadOrderListingGetter>> _getListings;
    private readonly Func<GameRelease, bool> _usesSeparatedMasterLoadOrders;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<ModPath, GameRelease, IModMasterStyledGetter> _readMasterStyle;

    public GameLoadOrderProvider()
        : this(
            (release, dataPath) => LoadOrder.GetLoadOrderListings(release, dataPath, throwOnMissingMods: false).ToList(),
            release => GameConstants.Get(release).SeparateMasterLoadOrders,
            File.Exists,
            (modPath, release) => KeyedMasterStyle.FromPath(modPath, release))
    {
    }

    internal GameLoadOrderProvider(
        Func<GameRelease, string, IReadOnlyList<ILoadOrderListingGetter>> getListings,
        Func<GameRelease, bool> usesSeparatedMasterLoadOrders,
        Func<string, bool> fileExists,
        Func<ModPath, GameRelease, IModMasterStyledGetter> readMasterStyle)
    {
        _getListings = getListings;
        _usesSeparatedMasterLoadOrders = usesSeparatedMasterLoadOrders;
        _fileExists = fileExists;
        _readMasterStyle = readMasterStyle;
    }

    public GameLoadOrderSnapshot BuildSnapshot(
        GameRelease gameRelease,
        string dataPath,
        bool includeMasterFlagsLookup = false)
    {
        var listings = _getListings(gameRelease, dataPath);
        var listedPluginNames = listings
            .Select(listing => listing.ModKey.FileName.ToString())
            .ToList();

        if (!includeMasterFlagsLookup || !_usesSeparatedMasterLoadOrders(gameRelease))
        {
            return new GameLoadOrderSnapshot(listedPluginNames);
        }

        var masterStyles = new List<IModMasterStyledGetter>(listings.Count);
        foreach (var listing in listings)
        {
            var pluginPath = Path.Combine(dataPath, listing.ModKey.FileName.ToString());
            if (!_fileExists(pluginPath))
            {
                continue;
            }

            var modPath = new ModPath(listing.ModKey, pluginPath);
            masterStyles.Add(_readMasterStyle(modPath, gameRelease));
        }

        return new GameLoadOrderSnapshot(listedPluginNames, masterStyles);
    }

    public IReadOnlyList<string> GetListedPluginNames(GameRelease gameRelease, string dataPath)
    {
        return BuildSnapshot(gameRelease, dataPath, includeMasterFlagsLookup: false).ListedPluginNames;
    }
}
