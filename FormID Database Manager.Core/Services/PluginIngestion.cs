using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Owns ordered Plugin Ingestion for one complete captured selection.
/// </summary>
internal sealed class PluginIngestion : IPluginIngestion
{
    private readonly EntryExtraction _entryExtraction;
    private readonly IGameLoadOrderProvider _loadOrderProvider;
    private readonly IPluginOverlayReader _overlayReader;

    /// <summary>
    ///     Creates production Plugin Ingestion with its Mutagen-backed load-order and overlay adapters.
    /// </summary>
    internal PluginIngestion()
        : this(new GameLoadOrderProvider(), new MutagenPluginOverlayReader(), new EntryExtraction())
    {
    }

    /// <summary>
    ///     Creates Plugin Ingestion with a supplied load-order boundary and production overlay behavior.
    /// </summary>
    /// <param name="loadOrderProvider">The provider used once for the complete captured selection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loadOrderProvider" /> is null.</exception>
    internal PluginIngestion(IGameLoadOrderProvider loadOrderProvider)
        : this(loadOrderProvider, new MutagenPluginOverlayReader(), new EntryExtraction())
    {
    }

    /// <summary>
    ///     Creates aggregate Plugin Ingestion from its load-order, overlay, and Entry Extraction adapters.
    /// </summary>
    /// <param name="loadOrderProvider">The provider used once for the complete captured selection.</param>
    /// <param name="overlayReader">The Plugin overlay adapter used sequentially for available selections.</param>
    /// <param name="entryExtraction">The Entry Extraction module used while the Store enumerates records.</param>
    /// <exception cref="ArgumentNullException">Any adapter is null.</exception>
    internal PluginIngestion(
        IGameLoadOrderProvider loadOrderProvider,
        IPluginOverlayReader overlayReader,
        EntryExtraction entryExtraction)
    {
        _loadOrderProvider = loadOrderProvider ?? throw new ArgumentNullException(nameof(loadOrderProvider));
        _overlayReader = overlayReader ?? throw new ArgumentNullException(nameof(overlayReader));
        _entryExtraction = entryExtraction ?? throw new ArgumentNullException(nameof(entryExtraction));
    }

    /// <summary>
    ///     Prepares one load-order snapshot and attempts every selected Plugin sequentially through the supplied Store
    ///     session, returning one authoritative outcome in selection order.
    /// </summary>
    /// <param name="request">The immutable selected-Plugin request.</param>
    /// <param name="recordStore">The already-open Store session owned by the surrounding Processing Run.</param>
    /// <param name="progress">Optional transient preparation and current-Plugin facts.</param>
    /// <param name="cancellationToken">Stops the selected set without returning a completed report.</param>
    /// <returns>One ordered outcome for every selected Plugin on normal completion.</returns>
    /// <remarks>
    ///     Only normalized Plugin-read failures become Plugin outcomes. Cancellation and infrastructure failures propagate
    ///     without a report, and the caller retains Store optimization and disposal ownership.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> or <paramref name="recordStore" /> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    [RequiresUnreferencedCode(
        "Uses reflection-based name extraction for Mutagen records through EntryExtraction.")]
    public async Task<PluginIngestionReport> IngestAsync(
        SelectedPluginIngestionRequest request,
        IFormIdRecordStoreSession recordStore,
        IProgress<PluginIngestionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(recordStore);

        // A pre-cancelled selection must not announce preparation or consult any external adapter.
        cancellationToken.ThrowIfCancellationRequested();

        var totalPluginCount = request.PluginNames.Length;
        var dataPath = GameReleaseHelper.ResolveDataPath(request.GameDirectory);
        progress?.Report(PluginIngestionProgress.PreparingLoadOrder(totalPluginCount));
        // Synchronous progress callbacks can request cancellation before load-order initialization begins.
        cancellationToken.ThrowIfCancellationRequested();
        var loadOrderSnapshot = _loadOrderProvider.BuildSnapshot(
            request.GameRelease,
            dataPath,
            includeMasterFlagsLookup: true);
        var outcomes = new List<PluginIngestionOutcome>(totalPluginCount);

        for (var index = 0; index < totalPluginCount; index++)
        {
            // This boundary makes selection order the cancellation unit: no later Plugin is announced or attempted.
            cancellationToken.ThrowIfCancellationRequested();

            var pluginName = request.PluginNames[index];
            progress?.Report(PluginIngestionProgress.IngestingPlugin(pluginName, index + 1, totalPluginCount));
            // A synchronous reporter can cancel after the selection gate but before this Plugin attempt begins.
            cancellationToken.ThrowIfCancellationRequested();

            var skippedPlugin = GetSkippedPlugin(pluginName, dataPath, loadOrderSnapshot);
            if (skippedPlugin is not null)
            {
                outcomes.Add(skippedPlugin);
                continue;
            }

            var outcome = await IngestAvailablePluginAsync(
                    pluginName,
                    Path.Combine(dataPath, pluginName),
                    request.GameRelease,
                    loadOrderSnapshot,
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
    ///     Opens, extracts, and stores one Plugin whose load-order membership and file availability were already verified.
    /// </summary>
    /// <param name="pluginName">The selected Plugin name.</param>
    /// <param name="pluginPath">The resolved Plugin file path.</param>
    /// <param name="gameRelease">The GameRelease whose overlay rules apply.</param>
    /// <param name="loadOrderSnapshot">The one snapshot shared by the complete selection.</param>
    /// <param name="updateMode">The Store update behavior.</param>
    /// <param name="recordStore">The already-open Store session.</param>
    /// <param name="cancellationToken">Stops overlay enumeration or the Store write.</param>
    /// <returns>Facts describing the one selected Plugin attempt.</returns>
    /// <remarks>
    ///     The overlay is always disposed. A standalone disposal failure propagates, while cleanup is best-effort when a
    ///     cancellation or infrastructure exception is already in flight so the primary exception retains its identity.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    /// <exception cref="Exception">An unexpected infrastructure or standalone overlay-cleanup failure occurs.</exception>
    [RequiresUnreferencedCode(
        "Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types for name extraction.")]
    private async Task<PluginIngestionOutcome> IngestAvailablePluginAsync(
        string pluginName,
        string pluginPath,
        GameRelease gameRelease,
        GameLoadOrderSnapshot loadOrderSnapshot,
        UpdateMode updateMode,
        IFormIdRecordStoreSession recordStore,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IModDisposeGetter plugin;
        try
        {
            plugin = TryCreateOverlay(pluginPath, gameRelease, loadOrderSnapshot.ReadParameters);
        }
        catch (PluginReadException ex)
        {
            // Only normalized Plugin-read failures are nonfatal; every infrastructure exception aborts the selected set.
            return CreateFailedPlugin(pluginName, ex);
        }

        Exception? primaryException = null;
        try
        {
            var warningCollector = new RecordWarningCollector();
            var records = EnumeratePluginRecords(plugin, warningCollector, cancellationToken);

            try
            {
                var writeResult = await recordStore.WritePluginAsync(
                        pluginName,
                        records,
                        updateMode,
                        cancellationToken)
                    .ConfigureAwait(false);

                // The Store may finish as cancellation arrives, so recheck before turning that write into a normal outcome.
                cancellationToken.ThrowIfCancellationRequested();

                if (writeResult.RecordCount == 0)
                {
                    return new SkippedPlugin(pluginName, SkippedPluginReason.ZeroFormIdRecords);
                }

                return new IngestedPlugin(
                    pluginName,
                    writeResult.RecordCount,
                    warningCollector.CreateWarning());
            }
            catch (PluginReadException ex)
            {
                // Lazy enumeration uses the same narrow marker boundary while Store and cancellation failures escape unchanged.
                return CreateFailedPlugin(pluginName, ex);
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
            catch when (primaryException is not null)
            {
                // Overlay cleanup is best-effort while preserving the cancellation or infrastructure failure in flight.
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
    ///     Classifies a selection that cannot reach overlay reading from the shared load-order and filesystem facts.
    /// </summary>
    /// <param name="pluginName">The selected Plugin name.</param>
    /// <param name="dataPath">The resolved Data directory.</param>
    /// <param name="loadOrderSnapshot">The one snapshot prepared for the complete selection.</param>
    /// <returns>The complete typed skip fact, or <see langword="null" /> when the Plugin can be read.</returns>
    private static SkippedPlugin? GetSkippedPlugin(
        string pluginName,
        string dataPath,
        GameLoadOrderSnapshot loadOrderSnapshot)
    {
        if (!loadOrderSnapshot.ContainsPlugin(pluginName))
        {
            return new SkippedPlugin(pluginName, SkippedPluginReason.NotPresentInLoadOrder);
        }

        var pluginPath = Path.Combine(dataPath, pluginName);
        return File.Exists(pluginPath)
            ? null
            : new SkippedPlugin(pluginName, SkippedPluginReason.PluginFileUnavailable, pluginPath);
    }

    /// <summary>
    ///     Lazily extracts storable records while retaining recoverable warning facts.
    /// </summary>
    /// <param name="plugin">The opened Plugin overlay.</param>
    /// <param name="warningCollector">The bounded recoverable-diagnostic collector.</param>
    /// <param name="cancellationToken">Stops record extraction.</param>
    /// <returns>The lazy sequence consumed inside the Store's atomic Plugin write.</returns>
    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records via EntryExtraction.")]
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
    /// <param name="pluginPath">The available selected Plugin path.</param>
    /// <param name="gameRelease">The target GameRelease.</param>
    /// <param name="readParameters">The shared load-order-aware binary read parameters.</param>
    /// <returns>The disposable Plugin overlay.</returns>
    /// <exception cref="PluginReadException">The overlay adapter reports an expected Plugin-specific failure.</exception>
    private IModDisposeGetter TryCreateOverlay(
        string pluginPath,
        GameRelease gameRelease,
        BinaryReadParameters readParameters)
    {
        try
        {
            return _overlayReader.ReadOverlay(pluginPath, gameRelease, readParameters);
        }
        catch (PluginOverlayReadException ex)
        {
            RethrowNestedCancellation(ex);
            throw new PluginReadException(
                PluginReadPhase.OpeningPlugin,
                ex.Message,
                ex);
        }
    }

    /// <summary>
    ///     Preserves cancellation even when Mutagen enriches it inside a record-reading exception.
    /// </summary>
    /// <param name="exception">The possible wrapper raised by the Plugin adapter or enumerator.</param>
    /// <exception cref="OperationCanceledException">The exception chain contains cancellation.</exception>
    private static void RethrowNestedCancellation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
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
