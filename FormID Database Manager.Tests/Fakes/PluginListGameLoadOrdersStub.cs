using System.Collections.Immutable;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     Supplies the Game Load Orders role used by Plugin List tests while rejecting unexpected preparation calls.
/// </summary>
internal abstract class PluginListGameLoadOrdersStub : IGameLoadOrders
{
    /// <inheritdoc />
    public abstract Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Rejects the selected-Plugin preparation role because Plugin List must depend only on discovery.
    /// </summary>
    /// <param name="gameRelease">The unexpected GameRelease supplied by the caller.</param>
    /// <param name="canonicalDataDirectory">The unexpected canonical Data directory supplied by the caller.</param>
    /// <param name="selectedPluginNames">The unexpected ordered selection supplied by the caller.</param>
    /// <param name="cancellationToken">The unexpected preparation lifetime supplied by the caller.</param>
    /// <exception cref="InvalidOperationException">Always thrown when Plugin List crosses into preparation.</exception>
    public ImmutableArray<PreparedSelectedPlugin> PrepareSelectedPlugins(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IReadOnlyList<string> selectedPluginNames,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Plugin List must not prepare selected Plugins.");
    }

    /// <summary>
    ///     Creates one immutable successful discovery fact from configured Plugin names.
    /// </summary>
    /// <param name="pluginNames">The ordered Plugin names the test double should return.</param>
    /// <returns>A successful immutable Game Load Order discovery fact.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pluginNames" /> is null.</exception>
    public static AvailablePluginsDiscoveryResult Discovered(IEnumerable<string> pluginNames)
    {
        ArgumentNullException.ThrowIfNull(pluginNames);
        return new AvailablePluginsDiscovered(pluginNames.ToImmutableArray());
    }

    /// <summary>
    ///     Creates one expected local-access failure fact for caller behavior tests.
    /// </summary>
    /// <param name="errorMessage">The UI-neutral failure detail the test double should return.</param>
    /// <returns>An expected local-access failure fact.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="errorMessage" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="errorMessage" /> is empty or whitespace.</exception>
    public static AvailablePluginsDiscoveryResult LocalAccessFailure(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new GameLoadOrderLocalAccessFailure(errorMessage);
    }
}
