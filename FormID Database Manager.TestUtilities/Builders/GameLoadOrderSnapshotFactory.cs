using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Meta;

namespace FormID_Database_Manager.TestUtilities.Builders;

public static class GameLoadOrderSnapshotFactory
{
    public static GameLoadOrderSnapshot CreateSnapshot(params string[] pluginNames)
    {
        return new GameLoadOrderSnapshot(pluginNames);
    }

    /// <summary>
    ///     Creates the snapshot production's <see cref="GameLoadOrderProvider" /> builds for a game that separates
    ///     master load orders when its resolved Data directory holds none of the listed files.
    /// </summary>
    /// <param name="pluginNames">The listed Plugin names, in load order.</param>
    /// <returns>A snapshot whose master-flags lookup exists but is empty.</returns>
    /// <remarks>
    ///     The empty collection is the point, and is not the same as omitting it: the provider collects master styles
    ///     only for listings whose file exists on disk, and still supplies the resulting lookup, so Mutagen names the
    ///     master it cannot resolve rather than reporting only that no lookup was given (issue #52).
    /// </remarks>
    public static GameLoadOrderSnapshot CreateSnapshotWithoutAnyMasterOnDisk(params string[] pluginNames)
    {
        return new GameLoadOrderSnapshot(pluginNames, []);
    }

    /// <summary>
    ///     Creates the load-order snapshot generated Plugin fixtures are read through.
    /// </summary>
    /// <param name="release">The GameRelease the fixtures were generated for.</param>
    /// <param name="pluginNames">The fixture Plugin names, in load order.</param>
    /// <returns>A snapshot whose read parameters can resolve the master every fixture declares.</returns>
    /// <remarks>
    ///     Every fixture declares its game's main master, so a game with separated master load orders — Starfield, of
    ///     the Supported GameReleases — cannot read one with default binary read parameters: Mutagen raises
    ///     <c>MissingModMappingException</c> when masters are present and no master-flags lookup was supplied. The
    ///     separated-load-order condition is asked of Mutagen rather than listed here, which is the same condition
    ///     production's <see cref="GameLoadOrderProvider" /> uses, so a Mutagen upgrade that changes it cannot leave
    ///     this factory behind.
    /// </remarks>
    public static GameLoadOrderSnapshot CreateFixtureSnapshot(GameRelease release, params string[] pluginNames)
    {
        if (!GameConstants.Get(release).SeparateMasterLoadOrders)
        {
            return new GameLoadOrderSnapshot(pluginNames);
        }

        return new GameLoadOrderSnapshot(pluginNames, PluginFixture.MasterStylesFor(release));
    }
}
