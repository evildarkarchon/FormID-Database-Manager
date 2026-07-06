using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;

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
///     Owns Plugin Ingestion for one selected Plugin and returns outcome facts to the Processing Run.
/// </summary>
internal class PluginIngestion
{
    private const int WarningDetailLimit = 5;

    private static readonly HashSet<string> IgnorableErrorPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "KSIZ",
        "KWDA",
        "Expected EDID",
        "List with a non zero counter",
        "Unexpected record type",
        "Failed to parse record header",
        "Object reference not set to an instance"
    };

    private static readonly ConcurrentDictionary<Type, Func<IMajorRecordGetter, string?>> NameExtractorCache = new();

    [RequiresUnreferencedCode(
        "Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types for name extraction.")]
    internal virtual async Task<PluginIngestionResult> IngestAsync(
        PluginIngestionRequest request,
        FormIdRecordStore recordStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(recordStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GameDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PluginName);

        if (!request.LoadOrderSnapshot.ContainsPlugin(request.PluginName))
        {
            return PluginIngestionResult.Skipped(
                request.PluginName,
                $"Could not find plugin in load order: {request.PluginName}");
        }

        var dataPath = GameReleaseHelper.ResolveDataPath(request.GameDirectory);
        var pluginPath = Path.Combine(dataPath, request.PluginName);

        if (!File.Exists(pluginPath))
        {
            return PluginIngestionResult.Skipped(
                request.PluginName,
                $"Could not find plugin file: {pluginPath}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        IModDisposeGetter plugin;
        try
        {
            plugin = TryCreateOverlay(
                request.PluginName,
                pluginPath,
                request.GameRelease,
                request.LoadOrderSnapshot.ReadParameters);
        }
        catch (PluginRecordExtractionException ex)
        {
            return PluginIngestionResult.Failed(request.PluginName, ex.Message);
        }

        using var pluginToDispose = plugin;

        var warningCollector = new RecordWarningCollector(request.PluginName, WarningDetailLimit);
        var records = EnumeratePluginRecords(request.PluginName, pluginToDispose, warningCollector, cancellationToken);

        try
        {
            var writeResult = await recordStore.WritePluginAsync(
                    request.PluginName,
                    records,
                    request.UpdateMode,
                    cancellationToken)
                .ConfigureAwait(false);

            if (writeResult.RecordCount == 0)
            {
                return PluginIngestionResult.Skipped(
                    request.PluginName,
                    $"{request.PluginName} produced zero FormID records.");
            }

            return PluginIngestionResult.Succeeded(
                request.PluginName,
                writeResult.RecordCount,
                warningCollector.CreateWarnings());
        }
        catch (PluginRecordExtractionException ex)
        {
            return PluginIngestionResult.Failed(request.PluginName, ex.Message);
        }
    }

    internal virtual IModDisposeGetter CreateOverlay(
        string pluginPath,
        GameRelease gameRelease,
        BinaryReadParameters readParameters)
    {
        return gameRelease switch
        {
            GameRelease.Oblivion => OblivionMod.CreateFromBinaryOverlay(pluginPath,
                OblivionRelease.Oblivion,
                readParameters),
            GameRelease.SkyrimLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimLE,
                readParameters),
            GameRelease.SkyrimSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimSE,
                readParameters),
            GameRelease.SkyrimSEGog => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimSEGog,
                readParameters),
            GameRelease.SkyrimVR => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimVR,
                readParameters),
            GameRelease.EnderalLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.EnderalLE,
                readParameters),
            GameRelease.EnderalSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.EnderalSE,
                readParameters),
            GameRelease.Fallout4 => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                Fallout4Release.Fallout4,
                readParameters),
            GameRelease.Fallout4VR => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                Fallout4Release.Fallout4VR,
                readParameters),
            GameRelease.Starfield => StarfieldMod.CreateFromBinaryOverlay(pluginPath,
                StarfieldRelease.Starfield,
                readParameters),
            _ => throw new NotSupportedException($"Unsupported game release: {gameRelease}")
        };
    }

    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records via GetRecordName.")]
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

            if (TryCreateRecord(pluginName, records.Current, warningCollector) is { } record)
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

    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records via GetRecordName.")]
    private FormIdRecord? TryCreateRecord(
        string pluginName,
        IMajorRecordGetter record,
        RecordWarningCollector warningCollector)
    {
        try
        {
            string formId;
            try
            {
                formId = record.FormKey.ID.ToString("X6");
            }
            catch (Exception)
            {
                return null;
            }

            string entry;
            try
            {
                entry = !string.IsNullOrEmpty(record.EditorID) ? record.EditorID : GetRecordName(record);
            }
            catch (Exception)
            {
                entry = $"[{record.GetType().Name}_{formId}]";
            }

            return new FormIdRecord(formId, entry);
        }
        catch (Exception ex)
        {
            if (!IgnorableErrorPatterns.Any(pattern =>
                    ex.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                warningCollector.Add(ex.Message);
            }

            return null;
        }
    }

    [RequiresUnreferencedCode(
        "Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types.")]
    private string GetRecordName(IMajorRecordGetter record)
    {
        if (!string.IsNullOrEmpty(record.EditorID))
        {
            return record.EditorID;
        }

        if (record is Mutagen.Bethesda.Plugins.Aspects.INamedGetter named && !string.IsNullOrEmpty(named.Name))
        {
            return named.Name;
        }

        var extractor = NameExtractorCache.GetOrAdd(record.GetType(), type =>
        {
            var namedInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.Name.Contains("INamedGetter", StringComparison.Ordinal));

            if (namedInterface == null)
            {
                return _ => null;
            }

            var nameProperty = namedInterface.GetProperty("Name");
            if (nameProperty == null)
            {
                return _ => null;
            }

            var stringProperty = nameProperty.PropertyType.GetProperty("String");
            if (stringProperty == null)
            {
                return _ => null;
            }

            return rec =>
            {
                try
                {
                    var nameValue = nameProperty.GetValue(rec);
                    return nameValue != null ? stringProperty.GetValue(nameValue) as string : null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Name extraction failed for {rec.GetType().Name}: {ex.Message}");
                    return null;
                }
            };
        });

        var name = extractor(record);
        return !string.IsNullOrEmpty(name) ? name : $"[{record.GetType().Name}_{record.FormKey.ID:X6}]";
    }

    private IModDisposeGetter TryCreateOverlay(
        string pluginName,
        string pluginPath,
        GameRelease gameRelease,
        BinaryReadParameters readParameters)
    {
        try
        {
            return CreateOverlay(pluginPath, gameRelease, readParameters);
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
