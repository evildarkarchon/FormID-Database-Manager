using System.Collections.Immutable;
using System.Security;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Owns available-Plugin discovery and selected-Plugin read preparation for Game Load Orders.
/// </summary>
public sealed class GameLoadOrders : IGameLoadOrders
{
    private readonly IGameLoadOrderEnvironment _environment;

    /// <summary>
    ///     Creates production Game Load Orders over Mutagen and the real filesystem.
    /// </summary>
    public GameLoadOrders()
        : this(new GameLoadOrderEnvironment())
    {
    }

    /// <summary>
    ///     Creates the module over one cohesive lower Game Load Order environment adapter.
    /// </summary>
    /// <param name="environment">The environment that observes Mutagen and filesystem facts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="environment" /> is null.</exception>
    internal GameLoadOrders(IGameLoadOrderEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <inheritdoc />
    public Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canonicalDataDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        // Mutagen listing access and filesystem observation are synchronous and must not block the caller's thread.
        return Task.Run(
            () => DiscoverAvailablePlugins(
                gameRelease,
                canonicalDataDirectory,
                progress,
                cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public ImmutableArray<PreparedSelectedPlugin> PrepareSelectedPlugins(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IReadOnlyList<string> selectedPluginNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canonicalDataDirectory);
        ArgumentNullException.ThrowIfNull(selectedPluginNames);
        cancellationToken.ThrowIfCancellationRequested();

        var loadOrder = _environment.ReadGameLoadOrder(gameRelease, canonicalDataDirectory);
        if (loadOrder.GameRelease != gameRelease)
        {
            throw new InvalidOperationException("The Game Load Order environment returned a different GameRelease.");
        }

        var listedPluginFiles = new List<PluginFileObservation>(loadOrder.Listings.Length);
        foreach (var listing in loadOrder.Listings)
        {
            // Availability remains a separate fact and is observed for every listing before shared preparation.
            cancellationToken.ThrowIfCancellationRequested();
            var observation = _environment.ObservePluginFile(canonicalDataDirectory, listing);
            cancellationToken.ThrowIfCancellationRequested();
            listedPluginFiles.Add(observation);
        }

        // Eager preparation happens once for the complete Game Load Order, so every ready selection shares one identity.
        cancellationToken.ThrowIfCancellationRequested();
        var readCapability = _environment.PreparePluginReads(gameRelease, listedPluginFiles);
        cancellationToken.ThrowIfCancellationRequested();

        var filesByPluginName = new Dictionary<string, PluginFileObservation>(StringComparer.OrdinalIgnoreCase);
        foreach (var listedPluginFile in listedPluginFiles)
        {
            filesByPluginName.TryAdd(listedPluginFile.PluginName, listedPluginFile);
        }

        var prepared = ImmutableArray.CreateBuilder<PreparedSelectedPlugin>(selectedPluginNames.Count);
        foreach (var selectedPluginName in selectedPluginNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!filesByPluginName.TryGetValue(selectedPluginName, out var listedPluginFile))
            {
                prepared.Add(new SelectedPluginNotListed(selectedPluginName));
                continue;
            }

            if (!listedPluginFile.IsAvailable)
            {
                prepared.Add(new SelectedPluginFileUnavailable(
                    selectedPluginName,
                    listedPluginFile.ResolvedPluginPath));
                continue;
            }

            prepared.Add(new SelectedPluginReady(
                selectedPluginName,
                listedPluginFile.ResolvedPluginPath,
                readCapability));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return prepared.ToImmutable();
    }

    /// <summary>
    ///     Performs offloaded ordered discovery while keeping expected external failures distinct from observer failures.
    /// </summary>
    /// <param name="gameRelease">The GameRelease whose Game Load Order is read.</param>
    /// <param name="canonicalDataDirectory">The canonical Data directory containing listed Plugin files.</param>
    /// <param name="progress">An optional synchronous observer of raw scan counts.</param>
    /// <param name="cancellationToken">Stops discovery before further file observation or normal completion.</param>
    /// <returns>Available Plugins in order, or one expected local-access failure fact.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    private AvailablePluginsDiscoveryResult DiscoverAvailablePlugins(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IProgress<GameLoadOrderDiscoveryProgress>? progress,
        CancellationToken cancellationToken)
    {
        GameLoadOrder loadOrder;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            loadOrder = _environment.ReadGameLoadOrder(gameRelease, canonicalDataDirectory);
        }
        catch (Exception exception) when (IsExpectedLocalAccessFailure(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Only expected failures raised by the external adapter are normal discovery results.
            return new GameLoadOrderLocalAccessFailure(exception.Message);
        }

        var availablePluginNames = ImmutableArray.CreateBuilder<string>();
        ReportProgress(progress, 0, loadOrder.Listings.Length);
        cancellationToken.ThrowIfCancellationRequested();

        for (var index = 0; index < loadOrder.Listings.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PluginFileObservation observation;
            try
            {
                observation = _environment.ObservePluginFile(canonicalDataDirectory, loadOrder.Listings[index]);
            }
            catch (Exception exception) when (IsExpectedLocalAccessFailure(exception))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Observer callbacks remain outside this catch so their failures cannot masquerade as local access.
                return new GameLoadOrderLocalAccessFailure(exception.Message);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (observation.IsAvailable)
            {
                availablePluginNames.Add(observation.PluginName);
            }

            ReportProgress(progress, index + 1, loadOrder.Listings.Length);
        }

        // A synchronous completion observer can cancel, so classify cancellation before publishing a completed list.
        cancellationToken.ThrowIfCancellationRequested();
        return new AvailablePluginsDiscovered(availablePluginNames.ToImmutable());
    }

    /// <summary>
    ///     Reports startup, every tenth entry, and completion without flooding synchronous observers.
    /// </summary>
    /// <param name="progress">The optional synchronous progress observer.</param>
    /// <param name="scannedCount">The raw number of listings inspected.</param>
    /// <param name="totalCount">The total raw listing count.</param>
    private static void ReportProgress(
        IProgress<GameLoadOrderDiscoveryProgress>? progress,
        int scannedCount,
        int totalCount)
    {
        if (progress is not null &&
            (scannedCount == 0 || scannedCount % 10 == 0 || scannedCount == totalCount))
        {
            progress.Report(new GameLoadOrderDiscoveryProgress(scannedCount, totalCount));
        }
    }

    private static bool IsExpectedLocalAccessFailure(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or SecurityException;
    }
}
