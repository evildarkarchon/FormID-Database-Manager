using System.Collections.Immutable;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Owns immutable Plugin List source, membership, selection, and UI-neutral current state.
/// </summary>
internal sealed class PluginList : IDisposable
{
    private readonly IPluginListDiscovery _discovery;
    private readonly GameDetectionService _gameDetectionService;
    private PluginListState _current = PluginListState.Initial;
    private long _membershipVersion;
    private long _stateRevision;
    private int _disposed;

    /// <summary>
    ///     Creates a workflow-scoped Plugin List over the supplied discovery adapter.
    /// </summary>
    /// <param name="gameDetectionService">The source of GameRelease-specific base Plugin rules.</param>
    /// <param name="discovery">The adapter that supplies ordered, available Plugin names.</param>
    /// <exception cref="ArgumentNullException">Either dependency is null.</exception>
    public PluginList(GameDetectionService gameDetectionService, IPluginListDiscovery discovery)
    {
        _gameDetectionService = gameDetectionService ?? throw new ArgumentNullException(nameof(gameDetectionService));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
    }

    /// <summary>
    ///     Signals that consumers should read <see cref="Current" /> again.
    /// </summary>
    /// <remarks>Handlers run synchronously on the thread that publishes state; the event carries no state snapshot.</remarks>
    public event EventHandler? Changed;

    /// <summary>
    ///     Gets the latest immutable Plugin List state.
    /// </summary>
    public PluginListState Current => Volatile.Read(ref _current);

    /// <summary>
    ///     Loads and confirms a Plugin List from a normalized source.
    /// </summary>
    /// <param name="gameRelease">The GameRelease whose load order and base Plugin rules apply.</param>
    /// <param name="gameDirectory">A game root or Data-directory path.</param>
    /// <param name="advancedMode">Whether base Plugins are included.</param>
    /// <param name="cancellationToken">Cancels the caller's discovery request.</param>
    /// <returns>A task that completes when discovery reaches its terminal state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gameDirectory" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="gameDirectory" /> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="gameRelease" /> or <paramref name="advancedMode" /> is undefined, or discovery returns an
    ///     unsupported result.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> is cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The Plugin List has been disposed.</exception>
    /// <remarks>
    ///     Discovery may run off the caller thread. State signals are synchronous on whichever thread publishes each fact.
    ///     Programming and fatal discovery failures propagate unchanged.
    /// </remarks>
    public async Task RefreshAsync(
        GameRelease gameRelease,
        string gameDirectory,
        AdvancedMode advancedMode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(advancedMode))
        {
            throw new ArgumentOutOfRangeException(nameof(advancedMode), advancedMode, "Unsupported Advanced Mode value.");
        }

        var source = PluginListSource.Create(gameRelease, gameDirectory);
        Publish(null, new PluginListRefreshingActivity(source, 0, 0));

        var progress = new DiscoveryProgress(this, source);
        var result = await _discovery
            .DiscoverAsync(source, progress, cancellationToken)
            .ConfigureAwait(false);

        ThrowIfDisposed();
        switch (result)
        {
            case PluginListDiscoveryCompleted completed:
                PublishCompleted(source, advancedMode, completed.PluginNames);
                return;

            case PluginListDiscoveryFailed failed:
                Publish(null, new PluginListFailedActivity(source, failed.ErrorMessage));
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, "Unsupported Plugin List discovery result.");
        }
    }

    /// <summary>
    ///     Invalidates any confirmed membership and returns the module to no-source activity.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The Plugin List has been disposed.</exception>
    public void Invalidate()
    {
        ThrowIfDisposed();
        Publish(null, new PluginListNoSourceActivity());
    }

    /// <summary>
    ///     Accepts selection intent through the stable Plugin List boundary.
    /// </summary>
    /// <param name="intent">The desired individual or whole-list selection state.</param>
    /// <exception cref="ArgumentNullException"><paramref name="intent" /> is null.</exception>
    /// <exception cref="ObjectDisposedException">The Plugin List has been disposed.</exception>
    /// <remarks>
    ///     Initial discovery deliberately leaves membership unselected while the legacy production path remains active;
    ///     authoritative versioned selection behavior is introduced by the dependent selection slice.
    /// </remarks>
    public void Apply(PluginSelectionIntent intent)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(intent);
        // The legacy path still owns selection in this expansion slice; accepting intent here stabilizes the final boundary.
    }

    /// <summary>
    ///     Releases this workflow-scoped Plugin List. Disposal is idempotent.
    /// </summary>
    /// <remarks>Disposal prevents subsequent state publication but does not block waiting for synchronous discovery work.</remarks>
    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
    }

    /// <summary>
    ///     Applies base-Plugin and uniqueness rules, then publishes a new immutable confirmed membership.
    /// </summary>
    /// <param name="source">The normalized source that produced the discovery result.</param>
    /// <param name="advancedMode">Whether base Plugins remain eligible for membership.</param>
    /// <param name="discoveredNames">Ordered available names reported by discovery.</param>
    private void PublishCompleted(
        PluginListSource source,
        AdvancedMode advancedMode,
        ImmutableArray<string> discoveredNames)
    {
        var basePlugins = new HashSet<string>(
            _gameDetectionService.GetBaseGamePlugins(source.GameRelease),
            StringComparer.OrdinalIgnoreCase);
        var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = ImmutableArray.CreateBuilder<PluginListEntry>();

        foreach (var pluginName in discoveredNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
            if (advancedMode == AdvancedMode.Off && basePlugins.Contains(pluginName))
            {
                continue;
            }

            // Discovery order is authoritative; the first case-insensitive occurrence owns position and casing.
            if (addedNames.Add(pluginName))
            {
                entries.Add(new PluginListEntry(pluginName));
            }
        }

        var membershipVersion = Interlocked.Increment(ref _membershipVersion);
        var confirmed = new ConfirmedPluginList(
            membershipVersion,
            source,
            advancedMode,
            entries.ToImmutable(),
            []);
        Publish(confirmed, new PluginListReadyActivity(source, membershipVersion));
    }

    /// <summary>
    ///     Atomically replaces current state and synchronously signals subscribers without capturing the state in the event.
    /// </summary>
    /// <param name="confirmed">The optional confirmed membership exposed by the new state.</param>
    /// <param name="activity">The UI-neutral activity exposed by the new state.</param>
    private void Publish(ConfirmedPluginList? confirmed, PluginListActivity activity)
    {
        var state = new PluginListState(
            Interlocked.Increment(ref _stateRevision),
            confirmed,
            activity);
        Volatile.Write(ref _current, state);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Publishes raw discovery counts as UI-neutral refreshing activity for the requested source.
    /// </summary>
    /// <param name="source">The source whose discovery emitted the counts.</param>
    /// <param name="progress">The scanned and total count facts.</param>
    private void PublishProgress(PluginListSource source, PluginListDiscoveryProgress progress)
    {
        ThrowIfDisposed();
        Publish(Current.Confirmed, new PluginListRefreshingActivity(source, progress.ScannedCount, progress.TotalCount));
    }

    /// <summary>
    ///     Rejects operations and state publication after the workflow-scoped module is disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private sealed class DiscoveryProgress(PluginList owner, PluginListSource source)
        : IProgress<PluginListDiscoveryProgress>
    {
        public void Report(PluginListDiscoveryProgress value)
        {
            owner.PublishProgress(source, value);
        }
    }
}
