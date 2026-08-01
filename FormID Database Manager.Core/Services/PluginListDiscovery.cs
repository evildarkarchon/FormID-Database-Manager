using System.Collections.Immutable;
using System.Security;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Discovers ordered, available Plugin names through the production Game Load Order adapter and filesystem.
/// </summary>
internal sealed class PluginListDiscovery : IPluginListDiscovery
{
    private readonly IGameLoadOrderProvider _loadOrderProvider;

    /// <summary>
    ///     Creates production discovery backed by Mutagen's Game Load Order adapter.
    /// </summary>
    public PluginListDiscovery()
        : this(new GameLoadOrderProvider())
    {
    }

    /// <summary>
    ///     Creates discovery with a supplied Game Load Order adapter.
    /// </summary>
    /// <param name="loadOrderProvider">The existing adapter used to read ordered Plugin listings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loadOrderProvider" /> is null.</exception>
    public PluginListDiscovery(IGameLoadOrderProvider loadOrderProvider)
    {
        _loadOrderProvider = loadOrderProvider ?? throw new ArgumentNullException(nameof(loadOrderProvider));
    }

    /// <inheritdoc />
    public Task<PluginListDiscoveryResult> DiscoverAsync(
        PluginListSource source,
        IProgress<PluginListDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        // Mutagen load-order access and filesystem inspection are synchronous and must not block the caller's thread.
        return Task.Run(() => DiscoverCore(source, progress, cancellationToken), cancellationToken);
    }

    /// <summary>
    ///     Reads the source load order, normalizes expected local access failures, and retains only available Plugin files.
    /// </summary>
    /// <param name="source">The normalized source whose Data directory is inspected.</param>
    /// <param name="progress">An optional sink for raw scan counts.</param>
    /// <param name="cancellationToken">Cancels load-order reading or availability scanning.</param>
    /// <returns>Ordered available names or one expected local failure fact.</returns>
    private PluginListDiscoveryResult DiscoverCore(
        PluginListSource source,
        IProgress<PluginListDiscoveryProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> pluginNames;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            pluginNames = _loadOrderProvider
                .BuildSnapshot(source.GameRelease, source.DataDirectory)
                .ListedPluginNames;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Local load-order access failures are ordinary discovery facts; programming and fatal failures escape.
            return PluginListDiscoveryResult.Failed(exception.Message);
        }

        var availableNames = ImmutableArray.CreateBuilder<string>();
        ReportProgress(progress, 0, pluginNames.Count);
        for (var index = 0; index < pluginNames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pluginName = pluginNames[index];
            if (File.Exists(Path.Combine(source.DataDirectory, pluginName)))
            {
                availableNames.Add(pluginName);
            }

            ReportProgress(progress, index + 1, pluginNames.Count);
        }

        return PluginListDiscoveryResult.Completed(availableNames.ToImmutable());
    }

    /// <summary>
    ///     Reports raw scan facts at startup, completion, and bounded intervals for larger Plugin Lists.
    /// </summary>
    /// <param name="progress">The optional synchronous progress sink.</param>
    /// <param name="scannedCount">The number of raw load-order entries inspected.</param>
    /// <param name="totalCount">The total raw load-order entry count.</param>
    private static void ReportProgress(
        IProgress<PluginListDiscoveryProgress>? progress,
        int scannedCount,
        int totalCount)
    {
        if (progress is not null &&
            (scannedCount == 0 || scannedCount % 10 == 0 || scannedCount == totalCount))
        {
            // Bounding intermediate reports avoids flooding a later UI projection without hiding startup or completion.
            progress.Report(new PluginListDiscoveryProgress(scannedCount, totalCount));
        }
    }
}
