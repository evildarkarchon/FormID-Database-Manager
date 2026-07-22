using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
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
    private const int WarningDetailLimit = 5;

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

        cancellationToken.ThrowIfCancellationRequested();

        var totalPluginCount = request.PluginNames.Length;
        var dataPath = GameReleaseHelper.ResolveDataPath(request.GameDirectory);
        progress?.Report(PluginIngestionProgress.PreparingLoadOrder(totalPluginCount));
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

            var skipReason = GetSkipReason(pluginName, dataPath, loadOrderSnapshot);
            if (skipReason is { } reason)
            {
                outcomes.Add(new SkippedPlugin(pluginName, reason));
                continue;
            }

            var legacyResult = await IngestAvailablePluginAsync(
                    pluginName,
                    Path.Combine(dataPath, pluginName),
                    request.GameRelease,
                    loadOrderSnapshot,
                    request.UpdateMode,
                    recordStore,
                    cancellationToken)
                .ConfigureAwait(false);

            // Failed Plugin diagnostics are completed in the next contract slice; do not publish a guessed read phase.
            outcomes.Add(legacyResult.Kind switch
            {
                PluginIngestionResultKind.Succeeded => new IngestedPlugin(pluginName, legacyResult.RecordCount),
                PluginIngestionResultKind.Skipped => new SkippedPlugin(
                    pluginName,
                    SkippedPluginReason.ZeroFormIdRecords),
                PluginIngestionResultKind.Failed => throw new InvalidOperationException(
                    legacyResult.Detail ?? $"Could not read {pluginName}."),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(legacyResult),
                    legacyResult.Kind,
                    "Unsupported legacy Plugin Ingestion outcome.")
            });
        }

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

        return await IngestAvailablePluginAsync(
                request.PluginName,
                pluginPath,
                request.GameRelease,
                request.LoadOrderSnapshot,
                request.UpdateMode,
                recordStore,
                cancellationToken)
            .ConfigureAwait(false);
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
    /// <returns>The temporary one-Plugin result consumed by the aggregate and compatibility transports.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    [RequiresUnreferencedCode(
        "Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types for name extraction.")]
    private async Task<PluginIngestionResult> IngestAvailablePluginAsync(
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
            plugin = TryCreateOverlay(
                pluginName,
                pluginPath,
                gameRelease,
                loadOrderSnapshot.ReadParameters);
        }
        catch (PluginRecordExtractionException ex)
        {
            return PluginIngestionResult.Failed(pluginName, ex.Message);
        }

        using var pluginToDispose = plugin;

        var warningCollector = new RecordWarningCollector(pluginName, WarningDetailLimit);
        var records = EnumeratePluginRecords(pluginName, pluginToDispose, warningCollector, cancellationToken);

        try
        {
            var writeResult = await recordStore.WritePluginAsync(
                    pluginName,
                    records,
                    updateMode,
                    cancellationToken)
                .ConfigureAwait(false);

            if (writeResult.RecordCount == 0)
            {
                return CreateLegacySkippedResult(
                    pluginName,
                    pluginPath,
                    SkippedPluginReason.ZeroFormIdRecords);
            }

            return PluginIngestionResult.Succeeded(
                pluginName,
                writeResult.RecordCount,
                warningCollector.CreateWarnings());
        }
        catch (PluginRecordExtractionException ex)
        {
            return PluginIngestionResult.Failed(pluginName, ex.Message);
        }
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

    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records via EntryExtraction.")]
    private IEnumerable<FormIdRecord> EnumeratePluginRecords(
        string pluginName,
        IModGetter plugin,
        RecordWarningCollector warningCollector,
        CancellationToken cancellationToken)
    {
        using var records = CreateRecordEnumerator(pluginName, plugin);

        while (MoveNextRecord(pluginName, records))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_entryExtraction.TryExtract(records.Current, warningCollector.Add) is { } record)
            {
                yield return record;
            }
        }
    }

    private static IEnumerator<IMajorRecordGetter> CreateRecordEnumerator(string pluginName, IModGetter plugin)
    {
        try
        {
            return plugin.EnumerateMajorRecords().GetEnumerator();
        }
        catch (Exception ex)
        {
            throw new PluginRecordExtractionException($"Error enumerating records in {pluginName}: {ex.Message}", ex);
        }
    }

    private static bool MoveNextRecord(string pluginName, IEnumerator<IMajorRecordGetter> records)
    {
        try
        {
            return records.MoveNext();
        }
        catch (Exception ex)
        {
            throw new PluginRecordExtractionException($"Error enumerating records in {pluginName}: {ex.Message}", ex);
        }
    }

    private IModDisposeGetter TryCreateOverlay(
        string pluginName,
        string pluginPath,
        GameRelease gameRelease,
        BinaryReadParameters readParameters)
    {
        try
        {
            return _overlayReader.ReadOverlay(pluginPath, gameRelease, readParameters);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PluginRecordExtractionException($"Error opening {pluginName}: {ex.Message}", ex);
        }
    }

    private sealed class RecordWarningCollector(string pluginName, int detailLimit)
    {
        private readonly List<string> _details = [];
        private int _count;

        public void Add(string detail)
        {
            _count++;
            if (_details.Count < detailLimit)
            {
                _details.Add(detail);
            }
        }

        public IReadOnlyList<string> CreateWarnings()
        {
            if (_count == 0)
            {
                return [];
            }

            var message = $"{pluginName}: {_count} recoverable record issue{(_count == 1 ? string.Empty : "s")}.";
            if (_details.Count > 0)
            {
                message += $" {string.Join("; ", _details)}";
            }

            var remaining = _count - _details.Count;
            if (remaining > 0)
            {
                message += $"; and {remaining} more.";
            }

            return [message];
        }
    }

    private sealed class PluginRecordExtractionException(string message, Exception? innerException = null)
        : Exception(message, innerException);
}
