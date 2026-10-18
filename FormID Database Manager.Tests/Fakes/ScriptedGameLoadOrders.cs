using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     A scripted Game Load Orders fake that lets a test drive a real Plugin List into any of its states, because only
///     the Plugin List may construct its states (ADR-0009).
/// </summary>
/// <remarks>
///     Each discovery consumes the next scripted step: completes, held, fails, cancels or faults (see
///     <see cref="ScriptedDiscovery" />). A <see cref="Fixed" /> script repeats one completed result for every discovery.
///     Selected-Plugin preparation is rejected, as in every Plugin List stub. The fake lives in this test project, not in the test utilities project, because Plugin List and
///     its states are Core internals that only this project can see.
/// </remarks>
internal sealed class ScriptedGameLoadOrders : PluginListGameLoadOrdersStub
{
    private readonly List<PluginListSource> _discoveredSources = [];
    private readonly object _gate = new();
    private readonly ScriptedDiscovery? _repeating;
    private readonly Queue<ScriptedDiscovery> _sequence;

    private ScriptedGameLoadOrders(IEnumerable<ScriptedDiscovery> sequence, ScriptedDiscovery? repeating)
    {
        _sequence = new Queue<ScriptedDiscovery>(sequence);
        _repeating = repeating;
    }

    /// <summary>
    ///     Gets the Plugin List Source of every discovery started so far, in the order they started.
    /// </summary>
    /// <remarks>
    ///     A snapshot, so a test can tell which source a caller refreshed, and how often, without reaching into the
    ///     Plugin List. Each source is rebuilt from the GameRelease and canonical Data directory the Plugin List passed.
    /// </remarks>
    public IReadOnlyList<PluginListSource> DiscoveredSources
    {
        get
        {
            lock (_gate)
            {
                return _discoveredSources.ToArray();
            }
        }
    }

    /// <summary>
    ///     Creates a fake whose every discovery completes with the same ordered Plugin set.
    /// </summary>
    /// <param name="pluginNames">The ordered Plugin names every discovery returns.</param>
    /// <returns>A fake that never runs out of discoveries.</returns>
    public static ScriptedGameLoadOrders Fixed(params string[] pluginNames)
    {
        return new ScriptedGameLoadOrders([], ScriptedDiscovery.Completes(pluginNames));
    }

    /// <summary>
    ///     Creates a fake whose successive discoveries run successive steps, one step per discovery.
    /// </summary>
    /// <param name="steps">The steps in the order discoveries consume them.</param>
    /// <returns>A fake whose discovery faults with <see cref="InvalidOperationException" /> once the steps run out.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="steps" /> or one of its steps is null.</exception>
    public static ScriptedGameLoadOrders Sequenced(params ScriptedDiscovery[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        foreach (var step in steps)
        {
            ArgumentNullException.ThrowIfNull(step, nameof(steps));
        }

        return new ScriptedGameLoadOrders(steps, null);
    }

    /// <summary>
    ///     Runs the next scripted step for one Plugin List discovery.
    /// </summary>
    /// <inheritdoc />
    public override Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ScriptedDiscovery? step;
        lock (_gate)
        {
            _discoveredSources.Add(PluginListSource.Create(gameRelease, canonicalDataDirectory));
            if (!_sequence.TryDequeue(out step))
            {
                step = _repeating;
            }
        }

        // Running out of script is a test mistake; faulting makes it loud instead of inventing a Plugin set.
        return step is null
            ? Task.FromException<AvailablePluginsDiscoveryResult>(new InvalidOperationException(
                "The scripted Game Load Orders ran out of discovery steps."))
            : step.Discover(cancellationToken);
    }
}

/// <summary>
///     One scripted Game Load Orders discovery step consumed by <see cref="ScriptedGameLoadOrders" />.
/// </summary>
internal abstract class ScriptedDiscovery
{
    /// <summary>
    ///     Creates a step that completes with the given ordered Plugin set.
    /// </summary>
    /// <param name="pluginNames">The ordered Plugin names discovery returns.</param>
    /// <returns>A reusable completing step.</returns>
    public static ScriptedDiscovery Completes(params string[] pluginNames)
    {
        ArgumentNullException.ThrowIfNull(pluginNames);
        return new ResultDiscovery(PluginListGameLoadOrdersStub.Discovered(pluginNames));
    }

    /// <summary>
    ///     Creates a step whose discovery stays in flight until the test releases it, keeping the Plugin List loading
    ///     (no Confirmed Plugin List yet) or refreshing (same-source Confirmed Plugin List retained).
    /// </summary>
    /// <param name="pluginNames">The ordered Plugin names discovery completes with once released.</param>
    /// <returns>A single-use held step; keep the reference to call <see cref="HeldDiscovery.Release" />.</returns>
    public static HeldDiscovery Held(params string[] pluginNames)
    {
        ArgumentNullException.ThrowIfNull(pluginNames);
        return new HeldDiscovery(PluginListGameLoadOrdersStub.Discovered(pluginNames));
    }

    /// <summary>
    ///     Creates a step that returns an expected local-access failure, which the Plugin List publishes as Failed.
    /// </summary>
    /// <param name="errorMessage">The UI-neutral failure detail discovery returns.</param>
    /// <returns>A reusable failing step.</returns>
    public static ScriptedDiscovery Fails(string errorMessage = "The scripted Game Load Order could not be read.")
    {
        return new ResultDiscovery(PluginListGameLoadOrdersStub.LocalAccessFailure(errorMessage));
    }

    /// <summary>
    ///     Creates a step whose discovery ends only through cancellation of its discovery token.
    /// </summary>
    /// <returns>A reusable cancelling step.</returns>
    /// <remarks>
    ///     The Plugin List publishes Cancelled only for caller cancellation, so refresh with a caller token that is
    ///     already cancelled or that the test cancels while the discovery is in flight. The refresh rethrows
    ///     <see cref="OperationCanceledException" />.
    /// </remarks>
    public static ScriptedDiscovery Cancels()
    {
        return new CancellingDiscovery();
    }

    /// <summary>
    ///     Creates a step whose discovery faults, which the Plugin List publishes as Faulted before rethrowing.
    /// </summary>
    /// <param name="exception">The unexpected failure; defaults to an <see cref="IOException" />.</param>
    /// <returns>A reusable faulting step.</returns>
    public static ScriptedDiscovery Faults(Exception? exception = null)
    {
        return new FaultingDiscovery(exception ?? new IOException("The scripted Game Load Order discovery faulted."));
    }

    /// <summary>
    ///     Runs this step for one discovery.
    /// </summary>
    /// <param name="cancellationToken">The Plugin List's linked caller and retirement token.</param>
    /// <returns>The discovery task the Plugin List awaits.</returns>
    internal abstract Task<AvailablePluginsDiscoveryResult> Discover(CancellationToken cancellationToken);

    private sealed class ResultDiscovery(AvailablePluginsDiscoveryResult result) : ScriptedDiscovery
    {
        internal override Task<AvailablePluginsDiscoveryResult> Discover(CancellationToken cancellationToken)
        {
            // Cooperative like the production adapter: an already-cancelled discovery never returns a result.
            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<AvailablePluginsDiscoveryResult>(cancellationToken)
                : Task.FromResult(result);
        }
    }

    private sealed class CancellingDiscovery : ScriptedDiscovery
    {
        internal override async Task<AvailablePluginsDiscoveryResult> Discover(CancellationToken cancellationToken)
        {
            // The infinite delay ends only by throwing OperationCanceledException for this token, which is what a
            // cooperative production discovery raises when its caller cancels.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("A cancelling discovery cannot complete.");
        }
    }

    private sealed class FaultingDiscovery(Exception exception) : ScriptedDiscovery
    {
        internal override Task<AvailablePluginsDiscoveryResult> Discover(CancellationToken cancellationToken)
        {
            return Task.FromException<AvailablePluginsDiscoveryResult>(exception);
        }
    }
}

/// <summary>
///     A single-use scripted discovery that stays in flight until <see cref="Release" /> is called or its discovery
///     token is cancelled.
/// </summary>
internal sealed class HeldDiscovery : ScriptedDiscovery
{
    private readonly AvailablePluginsDiscoveryResult _result;

    // Continuations run asynchronously so Release never runs Plugin List publication inline on the test's thread.
    private readonly TaskCompletionSource<AvailablePluginsDiscoveryResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _started;

    /// <summary>
    ///     Creates a held step that completes with the given result once released.
    /// </summary>
    /// <param name="result">The discovery result published on release.</param>
    internal HeldDiscovery(AvailablePluginsDiscoveryResult result)
    {
        _result = result;
    }

    /// <summary>
    ///     Completes the held discovery with its scripted Plugin set, so the Plugin List leaves loading or refreshing.
    /// </summary>
    /// <remarks>
    ///     Releasing a discovery that a newer refresh already retired does nothing; the Plugin List ignores it anyway.
    ///     Releasing before the Plugin List starts the discovery makes that discovery complete immediately.
    /// </remarks>
    public void Release()
    {
        _completion.TrySetResult(_result);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The same held step is scripted for more than one discovery.</exception>
    internal override Task<AvailablePluginsDiscoveryResult> Discover(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A held discovery step can serve only one discovery.");
        }

        // Cooperative like the production adapter: a superseding refresh, disposal, or caller cancellation ends the
        // held discovery, so a test never has to release a discovery the Plugin List has already retired.
        var registration = cancellationToken.Register(
            () => _completion.TrySetCanceled(cancellationToken));
        _ = _completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return _completion.Task;
    }
}
