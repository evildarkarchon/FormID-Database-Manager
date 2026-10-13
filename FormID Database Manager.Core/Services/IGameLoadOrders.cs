using System.Collections.Immutable;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Provides role-oriented Game Load Order discovery and selected-Plugin read preparation.
/// </summary>
public interface IGameLoadOrders
{
    /// <summary>
    ///     Discovers available Plugins in Game Load Order order without blocking the caller's thread.
    /// </summary>
    /// <param name="gameRelease">The Supported GameRelease whose Game Load Order is read.</param>
    /// <param name="canonicalDataDirectory">The canonical Data directory containing listed Plugin files.</param>
    /// <param name="progress">An optional synchronous observer of raw listing scan counts.</param>
    /// <param name="cancellationToken">Stops discovery before further application-controlled filesystem work.</param>
    /// <returns>Available Plugins in order, or one expected local-access failure fact.</returns>
    Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Prepares one immutable case per selected Plugin and one shared opaque read capability for ready cases.
    /// </summary>
    /// <param name="gameRelease">The Supported GameRelease whose Game Load Order is read.</param>
    /// <param name="canonicalDataDirectory">The canonical Data directory containing listed Plugin files.</param>
    /// <param name="selectedPluginNames">The complete ordered Plugin selection.</param>
    /// <param name="cancellationToken">Stops preparation before further environment work.</param>
    /// <returns>Exactly one prepared case per selection, preserving selection order and casing.</returns>
    ImmutableArray<PreparedSelectedPlugin> PrepareSelectedPlugins(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IReadOnlyList<string> selectedPluginNames,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Reports raw Game Load Order discovery counts without presentation wording.
/// </summary>
/// <param name="ScannedCount">The number of ordered listings inspected.</param>
/// <param name="TotalCount">The total number of ordered listings.</param>
public readonly record struct GameLoadOrderDiscoveryProgress(int ScannedCount, int TotalCount);

/// <summary>
///     Describes how available-Plugin discovery ended normally.
/// </summary>
public abstract record AvailablePluginsDiscoveryResult;

/// <summary>
///     Carries the available Plugin names in Game Load Order order.
/// </summary>
/// <param name="PluginNames">The immutable ordered available Plugin names.</param>
public sealed record AvailablePluginsDiscovered(ImmutableArray<string> PluginNames)
    : AvailablePluginsDiscoveryResult;

/// <summary>
///     Carries an expected local-access failure from Game Load Order discovery.
/// </summary>
/// <param name="ErrorMessage">The UI-neutral external failure detail.</param>
public sealed record GameLoadOrderLocalAccessFailure(string ErrorMessage)
    : AvailablePluginsDiscoveryResult;

/// <summary>
///     One immutable selected-Plugin preparation result, preserving the caller's Plugin-name casing.
/// </summary>
/// <param name="PluginName">The selected Plugin name exactly as supplied by the caller.</param>
public abstract record PreparedSelectedPlugin(string PluginName);

/// <summary>
///     A selected Plugin that is not a member of the Game Load Order.
/// </summary>
/// <param name="PluginName">The selected Plugin name exactly as supplied by the caller.</param>
public sealed record SelectedPluginNotListed(string PluginName) : PreparedSelectedPlugin(PluginName);

/// <summary>
///     A listed selected Plugin whose resolved file is unavailable.
/// </summary>
/// <param name="PluginName">The selected Plugin name exactly as supplied by the caller.</param>
/// <param name="ResolvedPluginPath">The path whose availability was observed.</param>
public sealed record SelectedPluginFileUnavailable(string PluginName, string ResolvedPluginPath)
    : PreparedSelectedPlugin(PluginName);

/// <summary>
///     A listed selected Plugin that is ready for overlay construction with shared prepared read state.
/// </summary>
/// <param name="PluginName">The selected Plugin name exactly as supplied by the caller.</param>
/// <param name="ResolvedPluginPath">The available Plugin path resolved from its Game Load Order listing.</param>
/// <param name="ReadCapability">The opaque application-owned read capability shared by this preparation.</param>
public sealed record SelectedPluginReady(
    string PluginName,
    string ResolvedPluginPath,
    PluginReadCapability ReadCapability) : PreparedSelectedPlugin(PluginName);

/// <summary>
///     An opaque application-owned capability carrying prepared Plugin-read state between matched adapters.
/// </summary>
public sealed class PluginReadCapability
{
    private readonly object _payload;

    /// <summary>
    ///     Creates one capability around an adapter-owned payload without exposing its concrete type to callers.
    /// </summary>
    /// <param name="payload">The matched adapter's private prepared state.</param>
    internal PluginReadCapability(object payload)
    {
        _payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    /// <summary>
    ///     Reads the payload only when the consuming adapter is the matched owner type.
    /// </summary>
    /// <typeparam name="TPayload">The private payload type expected by the consuming adapter.</typeparam>
    /// <returns>The matched payload.</returns>
    /// <exception cref="InvalidOperationException">The capability belongs to another adapter.</exception>
    internal TPayload RequirePayload<TPayload>() where TPayload : class
    {
        return _payload as TPayload
               ?? throw new InvalidOperationException("The Plugin-read capability belongs to a different adapter.");
    }
}
