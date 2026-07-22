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
    private readonly object _gate = new();
    private RefreshOperation? _activeRefresh;
    private PluginListState _current = PluginListState.Initial;
    private long _membershipVersion;
    private long _refreshGeneration;
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
    /// <exception cref="OperationCanceledException">
    ///     <paramref name="cancellationToken" /> is cancelled, or discovery propagates an unexpected cancellation. Caller
    ///     cancellation activity is published only while this refresh is current.
    /// </exception>
    /// <exception cref="AggregateException">
    ///     A cancellation callback throws while retiring the previous active refresh.
    /// </exception>
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
        var operation = BeginRefresh(source);

        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                operation.RetirementToken);
            var progress = new DiscoveryProgress(this, operation);
            var result = await _discovery
                .DiscoverAsync(source, progress, linkedCancellation.Token)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            // A non-cooperative discovery adapter can finish after retirement; its result is intentionally ignored.
            if (!IsCurrent(operation))
            {
                return;
            }

            switch (result)
            {
                case PluginListDiscoveryCompleted completed:
                    PublishCompleted(operation, advancedMode, completed.PluginNames);
                    return;

                case PluginListDiscoveryFailed failed:
                    PublishFailure(operation, failed.ErrorMessage);
                    return;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(result),
                        result,
                        "Unsupported Plugin List discovery result.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishCancellation(operation);
            // Caller cancellation always remains exceptional, but only a still-current refresh publishes its activity.
            throw;
        }
        catch (OperationCanceledException) when (!IsCurrent(operation))
        {
            // Supersession, invalidation, and disposal alone are routine retirement, not user-visible cancellation.
        }
        finally
        {
            FinishRefresh(operation);
            operation.Dispose();
        }
    }

    /// <summary>
    ///     Invalidates any confirmed membership and returns the module to no-source activity.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The Plugin List has been disposed.</exception>
    /// <exception cref="AggregateException">A registered refresh cancellation callback throws.</exception>
    public void Invalidate()
    {
        ThrowIfDisposed();
        RefreshOperation? retired;
        EventHandler? changed;
        lock (_gate)
        {
            ThrowIfDisposed();
            retired = _activeRefresh;
            _activeRefresh = null;
            _refreshGeneration++;
            changed = PublishLocked(null, new PluginListNoSourceActivity());
        }

        // Removing the active generation before cancellation prevents non-cooperative work from republishing stale facts.
        retired?.Retire();
        changed?.Invoke(this, EventArgs.Empty);
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
    /// <exception cref="AggregateException">A registered refresh cancellation callback throws.</exception>
    /// <remarks>Disposal prevents subsequent state publication but does not block waiting for synchronous discovery work.</remarks>
    public void Dispose()
    {
        RefreshOperation? retired;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            Volatile.Write(ref _disposed, 1);
            retired = _activeRefresh;
            _activeRefresh = null;
            _refreshGeneration++;
        }

        // Disposal only retires work; it deliberately does not publish another state during workflow shutdown.
        retired?.Retire();
    }

    /// <summary>
    ///     Installs the next refresh generation and synchronously exposes its source-aware refreshing state.
    /// </summary>
    /// <param name="source">The normalized Plugin List Source being refreshed.</param>
    /// <returns>The operation identity and retirement lifetime for the new refresh.</returns>
    private RefreshOperation BeginRefresh(PluginListSource source)
    {
        RefreshOperation? retired;
        RefreshOperation operation;
        EventHandler? changed;
        lock (_gate)
        {
            ThrowIfDisposed();
            operation = new RefreshOperation(++_refreshGeneration, source);
            retired = _activeRefresh;
            _activeRefresh = operation;

            var currentConfirmed = _current.Confirmed;
            // The last confirmed membership remains coherent only when the normalized Plugin List Source is unchanged.
            var retainedConfirmed = currentConfirmed?.Source == source ? currentConfirmed : null;
            changed = PublishLocked(retainedConfirmed, new PluginListRefreshingActivity(source, 0, 0));
        }

        // The generation changes before cancellation so late callbacks cannot win even when discovery ignores its token.
        retired?.Retire();
        try
        {
            changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            FinishRefresh(operation);
            operation.Retire();
            operation.Dispose();
            throw;
        }

        return operation;
    }

    /// <summary>
    ///     Applies base-Plugin and uniqueness rules, then publishes a new immutable confirmed membership when still current.
    /// </summary>
    /// <param name="operation">The refresh generation that produced the discovery result.</param>
    /// <param name="advancedMode">Whether base Plugins remain eligible for membership.</param>
    /// <param name="discoveredNames">Ordered available names reported by discovery.</param>
    private void PublishCompleted(
        RefreshOperation operation,
        AdvancedMode advancedMode,
        ImmutableArray<string> discoveredNames)
    {
        if (!IsCurrent(operation))
        {
            return;
        }

        var basePlugins = new HashSet<string>(
            _gameDetectionService.GetBaseGamePlugins(operation.Source.GameRelease),
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

        EventHandler? changed;
        lock (_gate)
        {
            if (!IsCurrentLocked(operation))
            {
                return;
            }

            var membershipVersion = ++_membershipVersion;
            var confirmed = new ConfirmedPluginList(
                membershipVersion,
                operation.Source,
                advancedMode,
                entries.ToImmutable(),
                []);
            _activeRefresh = null;
            changed = PublishLocked(
                confirmed,
                new PluginListReadyActivity(operation.Source, membershipVersion));
        }

        changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Publishes one expected local discovery failure when its refresh generation is still current.
    /// </summary>
    /// <param name="operation">The refresh generation that produced the failure.</param>
    /// <param name="errorMessage">The UI-neutral local failure detail.</param>
    private void PublishFailure(RefreshOperation operation, string errorMessage)
    {
        PublishTerminal(
            operation,
            new PluginListFailedActivity(operation.Source, errorMessage));
    }

    /// <summary>
    ///     Publishes caller-requested cancellation when its refresh generation is still current.
    /// </summary>
    /// <param name="operation">The refresh generation cancelled by its caller.</param>
    private void PublishCancellation(RefreshOperation operation)
    {
        PublishTerminal(operation, new PluginListCancelledActivity(operation.Source));
    }

    /// <summary>
    ///     Publishes a non-success terminal activity while retaining only coherent same-source confirmation.
    /// </summary>
    /// <param name="operation">The refresh generation that produced the terminal activity.</param>
    /// <param name="activity">The UI-neutral failure or cancellation fact to publish.</param>
    private void PublishTerminal(RefreshOperation operation, PluginListActivity activity)
    {
        EventHandler? changed;
        lock (_gate)
        {
            if (!IsCurrentLocked(operation))
            {
                return;
            }

            var retainedConfirmed = _current.Confirmed?.Source == operation.Source ? _current.Confirmed : null;
            _activeRefresh = null;
            changed = PublishLocked(retainedConfirmed, activity);
        }

        changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Publishes raw discovery counts only while their refresh generation remains current.
    /// </summary>
    /// <param name="operation">The refresh generation whose discovery emitted the counts.</param>
    /// <param name="progress">The scanned and total count facts.</param>
    private void PublishProgress(RefreshOperation operation, PluginListDiscoveryProgress progress)
    {
        EventHandler? changed;
        lock (_gate)
        {
            if (!IsCurrentLocked(operation))
            {
                return;
            }

            changed = PublishLocked(
                _current.Confirmed,
                new PluginListRefreshingActivity(
                    operation.Source,
                    progress.ScannedCount,
                    progress.TotalCount));
        }

        changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Replaces current state while the caller holds the publication gate.
    /// </summary>
    /// <param name="confirmed">The optional confirmed membership exposed by the new state.</param>
    /// <param name="activity">The UI-neutral activity exposed by the new state.</param>
    /// <returns>The signal-only change handlers to invoke after releasing the gate.</returns>
    private EventHandler? PublishLocked(ConfirmedPluginList? confirmed, PluginListActivity activity)
    {
        var state = new PluginListState(++_stateRevision, confirmed, activity);
        Volatile.Write(ref _current, state);
        return Changed;
    }

    /// <summary>
    ///     Reports whether an operation owns the newest live refresh generation.
    /// </summary>
    /// <param name="operation">The refresh operation whose ownership is tested.</param>
    /// <returns><see langword="true" /> when the module is live and the operation is its active generation.</returns>
    private bool IsCurrent(RefreshOperation operation)
    {
        lock (_gate)
        {
            return IsCurrentLocked(operation);
        }
    }

    /// <summary>
    ///     Tests live generation ownership while the caller holds the publication gate.
    /// </summary>
    /// <param name="operation">The refresh operation whose ownership is tested.</param>
    /// <returns><see langword="true" /> when the operation can still publish state.</returns>
    private bool IsCurrentLocked(RefreshOperation operation)
    {
        return Volatile.Read(ref _disposed) == 0 &&
               _activeRefresh?.Generation == operation.Generation;
    }

    /// <summary>
    ///     Releases active ownership when a refresh exits exceptionally without publishing a terminal fact.
    /// </summary>
    /// <param name="operation">The exiting refresh operation.</param>
    private void FinishRefresh(RefreshOperation operation)
    {
        lock (_gate)
        {
            if (_activeRefresh?.Generation == operation.Generation)
            {
                _activeRefresh = null;
            }
        }
    }

    /// <summary>
    ///     Rejects operations and state publication after the workflow-scoped module is disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private sealed class DiscoveryProgress(PluginList owner, RefreshOperation operation)
        : IProgress<PluginListDiscoveryProgress>
    {
        public void Report(PluginListDiscoveryProgress value)
        {
            owner.PublishProgress(operation, value);
        }
    }

    private sealed class RefreshOperation(
        long generation,
        PluginListSource source) : IDisposable
    {
        private readonly object _lifetimeGate = new();
        private readonly CancellationTokenSource _retirement = new();
        private RefreshLifetimeState _lifetimeState;

        public long Generation { get; } = generation;

        public PluginListSource Source { get; } = source;

        public CancellationToken RetirementToken => _retirement.Token;

        /// <summary>
        ///     Requests cooperative retirement while coordinating with completion that can dispose concurrently.
        /// </summary>
        /// <exception cref="AggregateException">One or more registered cancellation callbacks throw.</exception>
        public void Retire()
        {
            lock (_lifetimeGate)
            {
                if (_lifetimeState != RefreshLifetimeState.Active)
                {
                    return;
                }

                _lifetimeState = RefreshLifetimeState.Retiring;
            }

            var disposeAfterRetirement = false;
            try
            {
                _retirement.Cancel();
            }
            finally
            {
                lock (_lifetimeGate)
                {
                    if (_lifetimeState == RefreshLifetimeState.DisposeRequested)
                    {
                        _lifetimeState = RefreshLifetimeState.Disposed;
                        disposeAfterRetirement = true;
                    }
                    else
                    {
                        _lifetimeState = RefreshLifetimeState.Active;
                    }
                }

                if (disposeAfterRetirement)
                {
                    _retirement.Dispose();
                }
            }
        }

        /// <summary>
        ///     Releases the retirement source immediately or defers release until in-progress cancellation returns.
        /// </summary>
        public void Dispose()
        {
            lock (_lifetimeGate)
            {
                if (_lifetimeState == RefreshLifetimeState.Disposed)
                {
                    return;
                }

                if (_lifetimeState is RefreshLifetimeState.Retiring or RefreshLifetimeState.DisposeRequested)
                {
                    // Cancellation can run task continuations inline, so disposal must wait until Cancel has unwound.
                    _lifetimeState = RefreshLifetimeState.DisposeRequested;
                    return;
                }

                _lifetimeState = RefreshLifetimeState.Disposed;
            }

            _retirement.Dispose();
        }

        private enum RefreshLifetimeState
        {
            Active,
            Retiring,
            DisposeRequested,
            Disposed
        }
    }
}
