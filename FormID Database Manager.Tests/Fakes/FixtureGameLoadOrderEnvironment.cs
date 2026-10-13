using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     Supplies deterministic listing facts while delegating file observation and opaque read preparation to the
///     production Game Load Order environment for real-overlay fixtures.
/// </summary>
/// <param name="listedPluginNames">The ordered Plugin names exposed as the fixture's Game Load Order.</param>
internal sealed class FixtureGameLoadOrderEnvironment(IReadOnlyList<string> listedPluginNames)
    : IGameLoadOrderEnvironment
{
    private readonly IReadOnlyList<string> _listedPluginNames =
        listedPluginNames ?? throw new ArgumentNullException(nameof(listedPluginNames));
    private readonly GameLoadOrderEnvironment _productionEnvironment = new();

    /// <inheritdoc />
    public GameLoadOrder ReadGameLoadOrder(GameRelease gameRelease, string canonicalDataDirectory)
    {
        return new GameLoadOrder(
            gameRelease,
            _listedPluginNames.Select(static pluginName => new GameLoadOrderListing(pluginName)));
    }

    /// <inheritdoc />
    public PluginFileObservation ObservePluginFile(
        string canonicalDataDirectory,
        GameLoadOrderListing listing)
    {
        return _productionEnvironment.ObservePluginFile(canonicalDataDirectory, listing);
    }

    /// <inheritdoc />
    public PluginReadCapability PreparePluginReads(
        GameRelease gameRelease,
        IReadOnlyList<PluginFileObservation> listedPluginFiles)
    {
        return _productionEnvironment.PreparePluginReads(gameRelease, listedPluginFiles);
    }
}
