using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
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

    /// <summary>
    ///     Extracts one FormID/Entry pair while reporting recoverable record diagnostics to the caller.
    /// </summary>
    /// <param name="record">The lazy Mutagen record to inspect.</param>
    /// <param name="reportWarning">Receives non-ignorable recoverable diagnostic messages in observation order.</param>
    /// <returns>The extracted record, or <see langword="null" /> when a recoverable issue prevents storage.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">A lazy record getter reports cancellation.</exception>
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
            catch (OperationCanceledException)
            {
                // Cancellation remains run control flow even when a lazy Mutagen record getter observes it first.
                throw;
            }
            catch (Exception ex)
            {
                RethrowNestedCancellation(ex);
                ReportRecoverableIssue(ex, reportWarning);
                return null;
            }

            string entry;
            try
            {
                entry = !string.IsNullOrEmpty(record.EditorID) ? record.EditorID : GetRecordName(record);
            }
            catch (OperationCanceledException)
            {
                // Entry fallback must not turn cancellation into a recoverable Processing Warning.
                throw;
            }
            catch (Exception ex)
            {
                RethrowNestedCancellation(ex);
                ReportRecoverableIssue(ex, reportWarning);
                entry = $"[{record.GetType().Name}_{formId}]";
            }

            return new FormIdRecord(formId, entry);
        }
        catch (OperationCanceledException)
        {
            // Preserve cancellation from any later lazy record access in the extraction path.
            throw;
        }
        catch (Exception ex)
        {
            RethrowNestedCancellation(ex);
            ReportRecoverableIssue(ex, reportWarning);
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
                catch (OperationCanceledException)
                {
                    // Cached reflection delegates follow the same cancellation contract as direct record access.
                    throw;
                }
                catch (Exception ex)
                {
                    RethrowNestedCancellation(ex);
                    System.Diagnostics.Debug.WriteLine(
                        $"Name extraction failed for {rec.GetType().Name}: {ex.Message}");
                    return null;
                }
            };
        });

        var name = extractor(record);
        return !string.IsNullOrEmpty(name) ? name : $"[{record.GetType().Name}_{record.FormKey.ID:X6}]";
    }

    /// <summary>
    ///     Reports non-ignorable record diagnostics while preserving the existing recoverable extraction policy.
    /// </summary>
    private static void ReportRecoverableIssue(Exception exception, Action<string> reportWarning)
    {
        if (!IgnorableErrorPatterns.Any(pattern =>
                exception.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
        {
            reportWarning(exception.Message);
        }
    }

    /// <summary>
    ///     Preserves cancellation when reflection or Mutagen wraps a lazy getter exception.
    /// </summary>
    /// <param name="exception">The possible wrapper raised during Entry Extraction.</param>
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
}
