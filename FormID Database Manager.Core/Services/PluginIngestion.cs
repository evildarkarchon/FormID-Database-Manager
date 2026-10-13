using System.Runtime.ExceptionServices;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Owns ordered Plugin Ingestion for one complete captured selection.
/// </summary>
internal sealed class PluginIngestion : IPluginIngestion
{
    private readonly EntryExtraction _entryExtraction;
    private readonly IGameLoadOrders _gameLoadOrders;
    private readonly IPluginOverlayReader _overlayReader;

    /// <summary>
    ///     Creates production Plugin Ingestion with its Mutagen-backed load-order and overlay adapters.
    /// </summary>
    internal PluginIngestion()
        : this(new GameLoadOrders(), new MutagenPluginOverlayReader(), new EntryExtraction())
    {
    }

    /// <summary>
    ///     Creates Plugin Ingestion with a supplied load-order boundary and production overlay behavior.
    /// </summary>
    /// <param name="gameLoadOrders">Game Load Orders used once to prepare the complete captured selection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="gameLoadOrders" /> is null.</exception>
    internal PluginIngestion(IGameLoadOrders gameLoadOrders)
        : this(gameLoadOrders, new MutagenPluginOverlayReader(), new EntryExtraction())
    {
    }

    /// <summary>
    ///     Creates aggregate Plugin Ingestion from its load-order, overlay, and Entry Extraction adapters.
    /// </summary>
    /// <param name="gameLoadOrders">Game Load Orders used once to prepare the complete captured selection.</param>
    /// <param name="overlayReader">The Plugin overlay adapter used sequentially for available selections.</param>
    /// <param name="entryExtraction">The Entry Extraction module used while the Store enumerates records.</param>
    /// <exception cref="ArgumentNullException">Any adapter is null.</exception>
    internal PluginIngestion(
        IGameLoadOrders gameLoadOrders,
        IPluginOverlayReader overlayReader,
        EntryExtraction entryExtraction)
    {
        _gameLoadOrders = gameLoadOrders ?? throw new ArgumentNullException(nameof(gameLoadOrders));
        _overlayReader = overlayReader ?? throw new ArgumentNullException(nameof(overlayReader));
        _entryExtraction = entryExtraction ?? throw new ArgumentNullException(nameof(entryExtraction));
    }

    /// <summary>
    ///     Prepares one selected-Plugin case per selection and attempts every ready Plugin sequentially through the
    ///     borrowed Plugin-write Store role, returning one authoritative outcome in selection order.
    /// </summary>
    /// <param name="request">The immutable selected-Plugin request.</param>
    /// <param name="recordStore">The Plugin-write Store role borrowed from the surrounding Processing Run.</param>
    /// <param name="progress">Optional transient preparation and current-Plugin facts.</param>
    /// <param name="cancellationToken">Stops the selected set without returning a completed report.</param>
    /// <returns>One ordered outcome for every selected Plugin on normal completion.</returns>
    /// <remarks>
    ///     Only normalized Plugin-read failures become Plugin outcomes. Cancellation and infrastructure failures propagate
    ///     without a report, and the caller retains Store optimization and disposal ownership.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> or <paramref name="recordStore" /> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    /// <exception cref="UnresolvableMasterException">
    ///     A selected Plugin declares a master the prepared read capability cannot resolve. The
    ///     selected set stops there because every remaining Plugin would fail the same way (ADR-0006).
    /// </exception>
    public async Task<PluginIngestionReport> IngestAsync(
        PluginProcessingRunRequest request,
        IPluginFormIdRecordWriter recordStore,
        IProgress<PluginIngestionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(recordStore);

        // A pre-cancelled selection must not announce preparation or consult any external adapter.
        cancellationToken.ThrowIfCancellationRequested();

        var totalPluginCount = request.PluginNames.Count;
        var dataPath = GameInstallations.CanonicalizeDataDirectory(request.GameDirectory);
        progress?.Report(PluginIngestionProgress.PreparingLoadOrder(totalPluginCount));
        // Synchronous progress callbacks can request cancellation before load-order initialization begins.
        cancellationToken.ThrowIfCancellationRequested();
        var preparedPlugins = _gameLoadOrders.PrepareSelectedPlugins(
            request.GameRelease,
            dataPath,
            request.PluginNames,
            cancellationToken);
        var outcomes = new List<PluginIngestionOutcome>(totalPluginCount);

        for (var index = 0; index < preparedPlugins.Length; index++)
        {
            // This boundary makes selection order the cancellation unit: no later Plugin is announced or attempted.
            cancellationToken.ThrowIfCancellationRequested();

            var preparedPlugin = preparedPlugins[index];
            var pluginName = preparedPlugin.PluginName;
            progress?.Report(PluginIngestionProgress.IngestingPlugin(pluginName, index + 1, totalPluginCount));
            // A synchronous reporter can cancel after the selection gate but before this Plugin attempt begins.
            cancellationToken.ThrowIfCancellationRequested();

            var skippedPlugin = GetSkippedPlugin(preparedPlugin);
            if (skippedPlugin is not null)
            {
                outcomes.Add(skippedPlugin);
                continue;
            }

            var outcome = await IngestAvailablePluginAsync(
                    (SelectedPluginReady)preparedPlugin,
                    request.UpdateMode,
                    recordStore,
                    cancellationToken)
                .ConfigureAwait(false);

            outcomes.Add(outcome);
        }

        // Close the final classification race so cancellation cannot produce a misleading completed report.
        cancellationToken.ThrowIfCancellationRequested();
        return new PluginIngestionReport(request, outcomes);
    }

    /// <summary>
    ///     Prepares one selected-Plugin case per selection and opens every ready Plugin's overlay, without a Store
    ///     session and without enumerating a single record, returning what each selection would contribute to a run.
    /// </summary>
    /// <param name="request">The immutable selected-Plugin request.</param>
    /// <param name="cancellationToken">Stops planning without returning a plan.</param>
    /// <returns>One planned outcome per selected Plugin, in selection order.</returns>
    /// <remarks>
    ///     <para>
    ///         Planning shares the classification and overlay-opening steps of a real run so the two cannot drift, and
    ///         stops exactly where record enumeration would begin. Every overlay it opens is released as soon as it has
    ///         opened, because opening is the only question a plan asks of one — including when a later Plugin fails the
    ///         plan, since no overlay outlives the Plugin it was opened for.
    ///     </para>
    ///     <para>
    ///         The method is deliberately synchronous inside: it does no I/O that has an asynchronous form, and its
    ///         caller already runs a Processing Run on a background worker.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    /// <exception cref="UnresolvableMasterException">
    ///     A selected Plugin declares a master the prepared read capability cannot resolve. The
    ///     plan stops there because every remaining Plugin would fail the same way (ADR-0006).
    /// </exception>
    public Task<PluginIngestionPlan> PlanAsync(
        PluginProcessingRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A pre-cancelled plan must not consult any external adapter, exactly as a pre-cancelled run must not.
        cancellationToken.ThrowIfCancellationRequested();

        var dataPath = GameInstallations.CanonicalizeDataDirectory(request.GameDirectory);
        var preparedPlugins = _gameLoadOrders.PrepareSelectedPlugins(
            request.GameRelease,
            dataPath,
            request.PluginNames,
            cancellationToken);
        var planned = new List<PlannedPlugin>(request.PluginNames.Count);

        foreach (var preparedPlugin in preparedPlugins)
        {
            // Selection order is the cancellation unit here too: no later Plugin is classified or opened.
            cancellationToken.ThrowIfCancellationRequested();

            planned.Add(PlanSelectedPlugin(preparedPlugin, cancellationToken));
        }

        // Close the final race so cancellation cannot produce a plan that looks complete.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PluginIngestionPlan(request, planned));
    }

    /// <summary>
    ///     Classifies one selected Plugin for a plan, opening its overlay only when nothing cheaper rules it out.
    /// </summary>
    /// <param name="preparedPlugin">The immutable selected-Plugin preparation case.</param>
    /// <param name="cancellationToken">Preserves cancellation that races with immediate overlay cleanup.</param>
    /// <returns>What this Plugin would contribute to a run.</returns>
    /// <exception cref="UnresolvableMasterException">
    ///     The Plugin declares a master the prepared capability cannot resolve, which fails the plan
    ///     rather than this Plugin.
    /// </exception>
    private PlannedPlugin PlanSelectedPlugin(
        PreparedSelectedPlugin preparedPlugin,
        CancellationToken cancellationToken)
    {
        if (GetSkippedPlugin(preparedPlugin) is { } skippedPlugin)
        {
            return ToPlannedSkip(skippedPlugin);
        }

        var readyPlugin = (SelectedPluginReady)preparedPlugin;
        IModDisposeGetter plugin;
        try
        {
            plugin = TryCreateOverlay(readyPlugin);
        }
        catch (PluginReadException ex)
        {
            // A Plugin-specific read failure is this Plugin's, exactly as it is during a run; the run-level master
            // failure raised by the same call is not caught here and fails the whole plan.
            return new PlannedPluginFailure(
                readyPlugin.PluginName,
                new PluginReadDiagnostic(ex.Phase, ex.DiagnosticMessage));
        }

        // Nothing runs between opening and releasing the overlay, because opening it was the entire question.
        try
        {
            plugin.Dispose();
        }
        catch when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation won the completion race, so cleanup cannot replace the plan's cancellation identity.
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new PlannedPluginIngestion(readyPlugin.PluginName);
    }

    /// <summary>
    ///     Restates a run's typed skip as the narrower planned skip, which cannot represent a zero-record Plugin.
    /// </summary>
    /// <param name="skippedPlugin">The skip classification shared with a real run.</param>
    /// <returns>The planned skip carrying the same reason and resolved path.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The skip reason is one a plan cannot reach, which means the shared classification started producing a reason
    ///     that needs record enumeration to know.
    /// </exception>
    private static PlannedPluginSkip ToPlannedSkip(SkippedPlugin skippedPlugin)
    {
        return skippedPlugin.Reason switch
        {
            SkippedPluginReason.NotPresentInLoadOrder => new PlannedPluginSkip(
                skippedPlugin.PluginName,
                PlannedSkipReason.NotPresentInLoadOrder),
            SkippedPluginReason.PluginFileUnavailable => new PlannedPluginSkip(
                skippedPlugin.PluginName,
                PlannedSkipReason.PluginFileUnavailable,
                skippedPlugin.ResolvedPluginPath),
            _ => throw new ArgumentOutOfRangeException(
                nameof(skippedPlugin),
                skippedPlugin.Reason,
                "A Processing Run plan cannot represent this skip reason.")
        };
    }

    /// <summary>
    ///     Opens, extracts, and stores one Plugin whose load-order membership and file availability were already verified.
    /// </summary>
    /// <param name="readyPlugin">The prepared selected Plugin passed intact to the overlay seam.</param>
    /// <param name="updateMode">The Store update behavior.</param>
    /// <param name="recordStore">The borrowed Plugin-write Store role.</param>
    /// <param name="cancellationToken">Stops overlay enumeration or the Store write.</param>
    /// <returns>Facts describing the one selected Plugin attempt.</returns>
    /// <remarks>
    ///     The overlay is always disposed. A standalone disposal failure propagates, while cleanup is best-effort when a
    ///     cancellation or infrastructure exception is already in flight so the primary exception retains its identity.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    /// <exception cref="UnresolvableMasterException">
    ///     The Plugin declares a master the prepared capability cannot resolve, which fails the run
    ///     rather than this Plugin.
    /// </exception>
    /// <exception cref="Exception">An unexpected infrastructure or standalone overlay-cleanup failure occurs.</exception>
    private async Task<PluginIngestionOutcome> IngestAvailablePluginAsync(
        SelectedPluginReady readyPlugin,
        UpdateMode updateMode,
        IPluginFormIdRecordWriter recordStore,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IModDisposeGetter plugin;
        try
        {
            plugin = TryCreateOverlay(readyPlugin);
        }
        catch (PluginReadException ex)
        {
            // Only normalized Plugin-read failures are nonfatal; every infrastructure exception aborts the selected set.
            return CreateFailedPlugin(readyPlugin.PluginName, ex);
        }

        Exception? primaryException = null;
        try
        {
            var warningCollector = new RecordWarningCollector();
            var records = EnumeratePluginRecords(plugin, warningCollector, cancellationToken);

            try
            {
                var writeResult = await recordStore.WritePluginAsync(
                        readyPlugin.PluginName,
                        records,
                        updateMode,
                        cancellationToken)
                    .ConfigureAwait(false);

                // The Store may finish as cancellation arrives, so recheck before turning that write into a normal outcome.
                cancellationToken.ThrowIfCancellationRequested();

                if (writeResult.RecordCount == 0)
                {
                    return new SkippedPlugin(readyPlugin.PluginName, SkippedPluginReason.ZeroFormIdRecords);
                }

                return new IngestedPlugin(
                    readyPlugin.PluginName,
                    writeResult.RecordCount,
                    warningCollector.CreateWarning());
            }
            catch (PluginReadException ex)
            {
                // Lazy enumeration uses the same narrow marker boundary while Store and cancellation failures escape unchanged.
                // Retain it as the active failure even though it becomes an outcome, so cleanup cannot replace the
                // Plugin classification that record enumeration already determined.
                primaryException = ex;
                return CreateFailedPlugin(readyPlugin.PluginName, ex);
            }
        }
        catch (Exception ex)
        {
            primaryException = ex;
            throw;
        }
        finally
        {
            try
            {
                plugin.Dispose();
            }
            catch when (primaryException is not null || cancellationToken.IsCancellationRequested)
            {
                // Cleanup is best-effort while preserving an active failure or cancellation that won completion.
            }
        }
    }

    /// <summary>
    ///     Converts an application-owned read marker into the stable Failed Plugin facts exposed by aggregate ingestion.
    /// </summary>
    /// <param name="pluginName">The selected Plugin name.</param>
    /// <param name="exception">The phase-aware Plugin-read marker.</param>
    /// <returns>The stable Failed Plugin outcome.</returns>
    private static FailedPlugin CreateFailedPlugin(string pluginName, PluginReadException exception)
    {
        return new FailedPlugin(
            pluginName,
            new PluginReadDiagnostic(exception.Phase, exception.DiagnosticMessage));
    }

    /// <summary>
    ///     Classifies a prepared selection that cannot reach overlay reading.
    /// </summary>
    /// <param name="preparedPlugin">The immutable selected-Plugin preparation case.</param>
    /// <returns>The complete typed skip fact, or <see langword="null" /> when the Plugin can be read.</returns>
    private static SkippedPlugin? GetSkippedPlugin(PreparedSelectedPlugin preparedPlugin)
    {
        return preparedPlugin switch
        {
            SelectedPluginNotListed notListed => new SkippedPlugin(
                notListed.PluginName,
                SkippedPluginReason.NotPresentInLoadOrder),
            SelectedPluginFileUnavailable unavailable => new SkippedPlugin(
                unavailable.PluginName,
                SkippedPluginReason.PluginFileUnavailable,
                unavailable.ResolvedPluginPath),
            SelectedPluginReady => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(preparedPlugin),
                preparedPlugin,
                "Unsupported prepared selected-Plugin case.")
        };
    }

    /// <summary>
    ///     Lazily extracts storable records while retaining recoverable warning facts.
    /// </summary>
    /// <param name="plugin">The opened Plugin overlay.</param>
    /// <param name="warningCollector">The bounded recoverable-diagnostic collector.</param>
    /// <param name="cancellationToken">Stops record extraction.</param>
    /// <returns>The lazy sequence consumed inside the Store's atomic Plugin write.</returns>
    private IEnumerable<FormIdRecord> EnumeratePluginRecords(
        IModGetter plugin,
        RecordWarningCollector warningCollector,
        CancellationToken cancellationToken)
    {
        using var records = CreateRecordEnumerator(plugin);

        while (MoveNextRecord(records))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_entryExtraction.TryExtract(records.Current, warningCollector.Add) is { } record)
            {
                yield return record;
            }
        }
    }

    /// <summary>
    ///     Creates the Mutagen record enumerator and marks expected record-read failures with their internal phase.
    /// </summary>
    /// <param name="plugin">The opened Plugin overlay.</param>
    /// <returns>The lazy major-record enumerator.</returns>
    /// <exception cref="PluginReadException">Mutagen reports an expected record-read failure.</exception>
    private static IEnumerator<IMajorRecordGetter> CreateRecordEnumerator(IModGetter plugin)
    {
        try
        {
            return plugin.EnumerateMajorRecords().GetEnumerator();
        }
        catch (RecordException ex)
        {
            RethrowNestedCancellation(ex);
            throw new PluginReadException(
                PluginReadPhase.ReadingRecords,
                ex.Message,
                ex);
        }
    }

    /// <summary>
    ///     Advances a lazy Mutagen enumerator while preserving expected record-read classification.
    /// </summary>
    /// <param name="records">The active major-record enumerator.</param>
    /// <returns><see langword="true" /> when another record is available.</returns>
    /// <exception cref="PluginReadException">Mutagen reports an expected record-read failure.</exception>
    private static bool MoveNextRecord(IEnumerator<IMajorRecordGetter> records)
    {
        try
        {
            return records.MoveNext();
        }
        catch (RecordException ex)
        {
            RethrowNestedCancellation(ex);
            throw new PluginReadException(
                PluginReadPhase.ReadingRecords,
                ex.Message,
                ex);
        }
    }

    /// <summary>
    ///     Opens an overlay and attaches the opening phase to adapter-normalized Plugin-read failures.
    /// </summary>
    /// <param name="readyPlugin">The selected Plugin and opaque capability prepared by Game Load Orders.</param>
    /// <returns>The disposable Plugin overlay.</returns>
    /// <remarks>
    ///     The two master-resolution failures below are classified here rather than in the overlay adapter on purpose.
    ///     The adapter's expected-failure list stays narrow so an unexpected internal failure still aborts loudly, and
    ///     these are not Plugin-specific anyway: they are facts about the Data directory that would fail every selected
    ///     Plugin identically (ADR-0006, issue #52).
    /// </remarks>
    /// <exception cref="PluginReadException">The overlay adapter reports an expected Plugin-specific failure.</exception>
    /// <exception cref="UnresolvableMasterException">
    ///     The Plugin declares a master the prepared capability cannot resolve.
    /// </exception>
    private IModDisposeGetter TryCreateOverlay(SelectedPluginReady readyPlugin)
    {
        try
        {
            return _overlayReader.ReadOverlay(readyPlugin);
        }
        catch (PluginOverlayReadException ex)
        {
            RethrowNestedCancellation(ex);
            throw new PluginReadException(
                PluginReadPhase.OpeningPlugin,
                ex.Message,
                ex);
        }
        catch (MissingModException ex)
        {
            // ModPath is the first of the exception's keys, which is the only one here: Mutagen's separated-master
            // path raises this per unresolved master, from a ModKey rather than a path, so it carries exactly one and
            // that key has no directory to report — only its file name.
            throw new UnresolvableMasterException(
                readyPlugin.PluginName,
                ex.ModPath.ModKey.FileName.ToString(),
                ex);
        }
        catch (MissingModMappingException ex)
        {
            // Mutagen reports only that no lookup was supplied, so there is no individual master to name.
            throw new UnresolvableMasterException(readyPlugin.PluginName, null, ex);
        }
    }

    /// <summary>
    ///     Preserves cancellation even when Mutagen enriches it inside a record-reading exception.
    /// </summary>
    /// <param name="exception">The possible wrapper raised by the Plugin adapter or enumerator.</param>
    /// <exception cref="OperationCanceledException">The exception chain contains cancellation.</exception>
    private static void RethrowNestedCancellation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException cancellation)
            {
                ExceptionDispatchInfo.Capture(cancellation).Throw();
            }
        }
    }

    private sealed class RecordWarningCollector
    {
        private readonly List<string> _details = [];
        private int _count;

        /// <summary>
        ///     Retains the total issue count and at most the first five ordered diagnostic details.
        /// </summary>
        /// <param name="detail">The raw recoverable diagnostic message.</param>
        public void Add(string detail)
        {
            _count++;
            if (_details.Count < ProcessingWarning.MaximumDiagnosticDetailCount)
            {
                _details.Add(detail);
            }
        }

        /// <summary>
        ///     Creates immutable warning facts when at least one recoverable issue was observed.
        /// </summary>
        /// <returns>The warning facts, or <see langword="null" /> when no issue was observed.</returns>
        public ProcessingWarning? CreateWarning()
        {
            if (_count == 0)
            {
                return null;
            }

            return new ProcessingWarning(_count, _details);
        }
    }

    /// <summary>
    ///     Carries application-owned Plugin-read phase and message facts across lazy Store enumeration.
    /// </summary>
    /// <param name="phase">The internal read phase that failed.</param>
    /// <param name="diagnosticMessage">The underlying Plugin-read message.</param>
    /// <param name="innerException">The adapter or Mutagen exception retained for diagnostics.</param>
    private sealed class PluginReadException(
        PluginReadPhase phase,
        string diagnosticMessage,
        Exception? innerException = null)
        : Exception(diagnosticMessage, innerException)
    {
        public PluginReadPhase Phase { get; } = phase;

        public string DiagnosticMessage { get; } = diagnosticMessage;
    }
}
