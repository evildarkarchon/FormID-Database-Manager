using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

/// <summary>
///     The production lower Game Load Order environment adapter over Mutagen and the real filesystem.
/// </summary>
internal sealed class GameLoadOrderEnvironment : IGameLoadOrderEnvironment
{
    /// <inheritdoc />
    public GameLoadOrder ReadGameLoadOrder(GameRelease gameRelease, string canonicalDataDirectory)
    {
        // Materialize here so deferred Mutagen I/O stays inside discovery's external-work failure boundary.
        var listings = LoadOrder
            .GetLoadOrderListings(gameRelease, canonicalDataDirectory, false)
            .ToList();
        return TranslateGameLoadOrder(gameRelease, listings);
    }

    /// <inheritdoc />
    public PluginFileObservation ObservePluginFile(
        string canonicalDataDirectory,
        GameLoadOrderListing listing)
    {
        var resolvedPluginPath = Path.Combine(canonicalDataDirectory, listing.PluginName);
        return new PluginFileObservation(
            listing.PluginName,
            resolvedPluginPath,
            File.Exists(resolvedPluginPath));
    }

    /// <inheritdoc />
    public PluginReadCapability PreparePluginReads(
        GameRelease gameRelease,
        IReadOnlyList<PluginFileObservation> listedPluginFiles)
    {
        BinaryReadParameters readParameters;
        if (!GameConstants.Get(gameRelease).SeparateMasterLoadOrders)
        {
            readParameters = BinaryReadParameters.Default;
        }
        else
        {
            var masterStyles = listedPluginFiles
                .Where(static pluginFile => pluginFile.IsAvailable)
                .Select(pluginFile => KeyedMasterStyle.FromPath(
                    new ModPath(
                        ModKey.FromNameAndExtension(pluginFile.PluginName),
                        pluginFile.ResolvedPluginPath),
                    gameRelease))
                .ToList<IModMasterStyledGetter>();

            // Empty is intentionally different from null: separated releases always carry a lookup (ADR-0006).
            readParameters = new BinaryReadParameters
            {
                MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(masterStyles)
            };
        }

        return new PluginReadCapability(new MutagenPluginReadCapabilityPayload(gameRelease, readParameters));
    }

    /// <summary>
    ///     Translates Mutagen listings into the application-owned ordered Game Load Order observation.
    /// </summary>
    /// <param name="gameRelease">The exact GameRelease supplied to Mutagen.</param>
    /// <param name="listings">The Mutagen listings in Game Load Order order.</param>
    /// <returns>The retained GameRelease and ordered Plugin filenames without Mutagen listing types.</returns>
    internal static GameLoadOrder TranslateGameLoadOrder(
        GameRelease gameRelease,
        IEnumerable<ILoadOrderListingGetter> listings)
    {
        return new GameLoadOrder(
            gameRelease,
            listings.Select(listing => new GameLoadOrderListing(listing.ModKey.FileName.ToString())));
    }
}

/// <summary>
///     The production-only payload hidden inside an application-owned Plugin-read capability.
/// </summary>
/// <param name="GameRelease">The release whose overlay factory must consume the prepared state.</param>
/// <param name="ReadParameters">The shared Mutagen binary-read preparation.</param>
internal sealed record MutagenPluginReadCapabilityPayload(
    GameRelease GameRelease,
    BinaryReadParameters ReadParameters);
