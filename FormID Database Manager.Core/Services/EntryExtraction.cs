using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

internal sealed class EntryExtraction
{
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
    internal FormIdRecord? TryExtract(IMajorRecordGetter record, Action<string> reportWarning)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(reportWarning);

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
                reportWarning(ex.Message);
            }

            return null;
        }
    }

    [RequiresUnreferencedCode(
        "Uses reflection to discover INamedGetter interface and Name/String properties on Mutagen record types.")]
    private static string GetRecordName(IMajorRecordGetter record)
    {
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
}
