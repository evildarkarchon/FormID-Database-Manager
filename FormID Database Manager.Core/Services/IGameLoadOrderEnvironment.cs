using System.Collections.Immutable;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Supplies application-level observations from Mutagen and the filesystem to the Game Load Orders module.
/// </summary>
internal interface IGameLoadOrderEnvironment
{
    /// <summary>
    ///     Reads one ordered Game Load Order for a GameRelease and canonical Data directory.
    /// </summary>
    /// <param name="gameRelease">The retained GameRelease supplied by the highest seam.</param>
    /// <param name="canonicalDataDirectory">The canonical Data directory used for external listing access.</param>
    /// <returns>The application-owned ordered listing observation.</returns>
    GameLoadOrder ReadGameLoadOrder(GameRelease gameRelease, string canonicalDataDirectory);

    /// <summary>
    ///     Resolves and observes one listed Plugin file without reading its header.
    /// </summary>
    /// <param name="canonicalDataDirectory">The canonical Data directory containing Plugin files.</param>
    /// <param name="listing">The application-owned listing being observed.</param>
    /// <returns>The resolved path and independent availability fact.</returns>
    PluginFileObservation ObservePluginFile(
        string canonicalDataDirectory,
        GameLoadOrderListing listing);

    /// <summary>
    ///     Eagerly prepares one opaque shared capability from the complete set of listed Plugin-file observations.
    /// </summary>
    /// <param name="gameRelease">The GameRelease whose read rules apply.</param>
    /// <param name="listedPluginFiles">All listed Plugin-file observations, in Game Load Order order.</param>
    /// <returns>One opaque capability shared by every ready selected Plugin.</returns>
    PluginReadCapability PreparePluginReads(
        GameRelease gameRelease,
        IReadOnlyList<PluginFileObservation> listedPluginFiles);
}

/// <summary>
///     The internal ordered listing and retained GameRelease observation returned by the lower environment seam.
/// </summary>
internal sealed class GameLoadOrder
{
    /// <summary>
    ///     Creates one immutable Game Load Order observation.
    /// </summary>
    /// <param name="gameRelease">The retained GameRelease used by the environment.</param>
    /// <param name="listings">The ordered application-owned listings.</param>
    internal GameLoadOrder(GameRelease gameRelease, IEnumerable<GameLoadOrderListing> listings)
    {
        GameRelease = gameRelease;
        Listings = listings.ToImmutableArray();
    }

    internal GameRelease GameRelease { get; }

    internal ImmutableArray<GameLoadOrderListing> Listings { get; }
}

/// <summary>
///     One application-owned Game Load Order listing.
/// </summary>
/// <param name="PluginName">The listed Plugin filename using its source casing.</param>
internal sealed record GameLoadOrderListing(string PluginName);

/// <summary>
///     The resolved path and independent availability fact for one Game Load Order listing.
/// </summary>
/// <param name="PluginName">The listed Plugin filename using its source casing.</param>
/// <param name="ResolvedPluginPath">The path resolved under the canonical Data directory.</param>
/// <param name="IsAvailable">Whether the Plugin file was available when observed.</param>
internal sealed record PluginFileObservation(
    string PluginName,
    string ResolvedPluginPath,
    bool IsAvailable);
