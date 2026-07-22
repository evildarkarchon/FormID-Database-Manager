using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

internal enum PluginIngestionResultKind
{
    Succeeded,
    Skipped,
    Failed
}

internal sealed record PluginIngestionRequest(
    string GameDirectory,
    GameRelease GameRelease,
    string PluginName,
    GameLoadOrderSnapshot LoadOrderSnapshot,
    UpdateMode UpdateMode);

internal sealed record PluginIngestionResult(
    PluginIngestionResultKind Kind,
    string PluginName,
    int RecordCount,
    IReadOnlyList<string> Warnings,
    string? Detail)
{
    public static PluginIngestionResult Succeeded(
        string pluginName,
        int recordCount,
        IReadOnlyList<string> warnings)
    {
        return new PluginIngestionResult(
            PluginIngestionResultKind.Succeeded,
            pluginName,
            recordCount,
            warnings,
            null);
    }

    public static PluginIngestionResult Skipped(string pluginName, string detail)
    {
        return new PluginIngestionResult(
            PluginIngestionResultKind.Skipped,
            pluginName,
            0,
            [],
            detail);
    }

    public static PluginIngestionResult Failed(string pluginName, string detail)
    {
        return new PluginIngestionResult(
            PluginIngestionResultKind.Failed,
            pluginName,
            0,
            [],
            detail);
    }
}

/// <summary>
///     Owns ordered Plugin Ingestion for a complete captured selection and retains the legacy one-Plugin transport until
///     Processing Run migrates to the aggregate seam.
/// </summary>
internal class PluginIngestion : IPluginIngestion
{
    private readonly EntryExtraction _entryExtraction;
    private readonly IGameLoadOrderProvider _loadOrderProvider;
    private readonly IPluginOverlayReader _overlayReader;

    internal PluginIngestion()
        : this(new GameLoadOrderProvider(), new MutagenPluginOverlayReader(), new EntryExtraction())
    {
    }

    /// <summary>
    ///     Creates the temporary legacy one-Plugin path with production load-order preparation available to the aggregate
    ///     operation.
    /// </summary>
    /// <param name="overlayReader">The Plugin overlay adapter.</param>
    /// <param name="entryExtraction">The Entry Extraction module.</param>
    /// <exception cref="ArgumentNullException">Either adapter is null.</exception>
    internal PluginIngestion(IPluginOverlayReader overlayReader, EntryExtraction entryExtraction)
        : this(new GameLoadOrderProvider(), overlayReader, entryExtraction)
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

            var skipReason = GetSkipReason(pluginName, dataPath, loadOrderSnapshot);
            if (skipReason is { } reason)
            {
                outcomes.Add(new SkippedPlugin(pluginName, reason));
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
    ///     Preserves the one-Plugin transport used by the current Processing Run caller while delegating ingestion to the
    ///     same eligibility and available-Plugin path as the aggregate operation.
    /// </summary>
    /// <param name="request">The temporary one-Plugin request, including its caller-prepared load-order snapshot.</param>
    /// <param name="recordStore">The already-open Store session.</param>
    /// <param name="cancellationToken">Stops the Plugin attempt.</param>
    /// <returns>The legacy presentation-oriented one-Plugin result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> or <paramref name="recordStore" /> is null.</exception>
    /// <exception cref="ArgumentException">A required request path or Plugin name is blank.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    [RequiresUnreferencedCode(
        "Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types for name extraction.")]
    internal virtual async Task<PluginIngestionResult> IngestAsync(
        PluginIngestionRequest request,
        IFormIdRecordStoreSession recordStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(recordStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GameDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PluginName);

        var dataPath = GameReleaseHelper.ResolveDataPath(request.GameDirectory);
        var pluginPath = Path.Combine(dataPath, request.PluginName);
        var skipReason = GetSkipReason(request.PluginName, dataPath, request.LoadOrderSnapshot);
        if (skipReason is { } reason)
        {
            return CreateLegacySkippedResult(request.PluginName, pluginPath, reason);
        }

        var outcome = await IngestAvailablePluginAsync(
                request.PluginName,
                pluginPath,
                request.GameRelease,
                request.LoadOrderSnapshot,
                request.UpdateMode,
                recordStore,
                cancellationToken)
            .ConfigureAwait(false);

        return CreateLegacyResult(outcome, pluginPath);
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
    /// <returns>The typed skip reason, or <see langword="null" /> when the Plugin can be read.</returns>
    private static SkippedPluginReason? GetSkipReason(
        string pluginName,
        string dataPath,
        GameLoadOrderSnapshot loadOrderSnapshot)
    {
        if (!loadOrderSnapshot.ContainsPlugin(pluginName))
        {
            return SkippedPluginReason.NotPresentInLoadOrder;
        }

        return File.Exists(Path.Combine(dataPath, pluginName))
            ? null
            : SkippedPluginReason.PluginFileUnavailable;
    }

    /// <summary>
    ///     Adapts one typed outcome to the presentation-oriented transport retained by the current Processing Run caller.
    /// </summary>
    /// <param name="outcome">The authoritative Plugin Ingestion facts.</param>
    /// <param name="pluginPath">The resolved path used by the unavailable-file compatibility detail.</param>
    /// <returns>The temporary legacy result.</returns>
    private static PluginIngestionResult CreateLegacyResult(
        PluginIngestionOutcome outcome,
        string pluginPath)
    {
        return outcome switch
        {
            IngestedPlugin ingested => PluginIngestionResult.Succeeded(
                ingested.PluginName,
                ingested.FormIdCount,
                FormatLegacyWarnings(ingested.PluginName, ingested.Warning)),
            SkippedPlugin skipped => CreateLegacySkippedResult(
                skipped.PluginName,
                pluginPath,
                skipped.Reason),
            FailedPlugin failed => PluginIngestionResult.Failed(
                failed.PluginName,
                FormatLegacyFailureDetail(failed)),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported Plugin Ingestion outcome.")
        };
    }

    /// <summary>
    ///     Formats typed Plugin-read diagnostics for the temporary one-Plugin Processing Run transport.
    /// </summary>
    /// <param name="failed">The typed Failed Plugin facts.</param>
    /// <returns>The legacy failure detail expected by the current Processing Run formatter.</returns>
    private static string FormatLegacyFailureDetail(FailedPlugin failed)
    {
        return failed.Diagnostic.Phase switch
        {
            PluginReadPhase.OpeningPlugin =>
                $"Error opening {failed.PluginName}: {failed.Diagnostic.Message}",
            PluginReadPhase.ReadingRecords =>
                $"Error enumerating records in {failed.PluginName}: {failed.Diagnostic.Message}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(failed),
                failed.Diagnostic.Phase,
                "Unsupported Plugin-read phase.")
        };
    }

    /// <summary>
    ///     Adapts a typed skip reason to the presentation-oriented result retained for the current Processing Run caller.
    /// </summary>
    /// <param name="pluginName">The selected Plugin name.</param>
    /// <param name="pluginPath">The resolved Plugin path used by the unavailable-file detail.</param>
    /// <param name="reason">The typed nonfatal skip reason.</param>
    /// <returns>The legacy Skipped result.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason" /> is unsupported.</exception>
    private static PluginIngestionResult CreateLegacySkippedResult(
        string pluginName,
        string pluginPath,
        SkippedPluginReason reason)
    {
        var detail = reason switch
        {
            SkippedPluginReason.NotPresentInLoadOrder => $"Could not find plugin in load order: {pluginName}",
            SkippedPluginReason.PluginFileUnavailable => $"Could not find plugin file: {pluginPath}",
            SkippedPluginReason.ZeroFormIdRecords => $"{pluginName} produced zero FormID records.",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unsupported skipped Plugin reason.")
        };

        return PluginIngestionResult.Skipped(pluginName, detail);
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

    /// <summary>
    ///     Formats structured warning facts for the temporary one-Plugin Processing Run compatibility transport.
    /// </summary>
    /// <param name="pluginName">The selected Plugin name used only by legacy presentation.</param>
    /// <param name="warning">The structured warning facts, or <see langword="null" />.</param>
    /// <returns>Zero or one legacy warning string.</returns>
    private static IReadOnlyList<string> FormatLegacyWarnings(string pluginName, ProcessingWarning? warning)
    {
        if (warning is null)
        {
            return [];
        }

        var message = $"{pluginName}: {warning.TotalIssueCount} recoverable record issue" +
                      $"{(warning.TotalIssueCount == 1 ? string.Empty : "s")}.";
        if (!warning.DiagnosticDetails.IsEmpty)
        {
            message += $" {string.Join("; ", warning.DiagnosticDetails)}";
        }

        if (warning.OmittedDetailCount > 0)
        {
            message += $"; and {warning.OmittedDetailCount} more.";
        }

        return [message];
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
