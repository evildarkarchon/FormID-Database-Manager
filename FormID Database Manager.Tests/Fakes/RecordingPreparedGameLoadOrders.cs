using System.Collections.Immutable;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     Returns caller-supplied prepared selected-Plugin cases while recording the highest-seam preparation request.
/// </summary>
internal sealed class RecordingPreparedGameLoadOrders(
    ImmutableArray<PreparedSelectedPlugin> preparedPlugins) : IGameLoadOrders
{
    /// <summary>
    ///     Gets the number of complete-selection preparation requests.
    /// </summary>
    public int PrepareCallCount { get; private set; }

    /// <summary>
    ///     Gets the canonical Data directory from the latest preparation request.
    /// </summary>
    public string CapturedCanonicalDataDirectory { get; private set; } = null!;

    /// <summary>
    ///     Gets the complete ordered selection from the latest preparation request.
    /// </summary>
    public IReadOnlyList<string> CapturedSelection { get; private set; } = [];

    /// <inheritdoc />
    public Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public ImmutableArray<PreparedSelectedPlugin> PrepareSelectedPlugins(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IReadOnlyList<string> selectedPluginNames,
        CancellationToken cancellationToken = default)
    {
        PrepareCallCount++;
        CapturedCanonicalDataDirectory = canonicalDataDirectory;
        CapturedSelection = selectedPluginNames.ToArray();
        return preparedPlugins;
    }
}
