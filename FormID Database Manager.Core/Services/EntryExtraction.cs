using System.Runtime.ExceptionServices;
using Mutagen.Bethesda.Plugins.Aspects;
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

    /// <summary>
    ///     Extracts one FormID/Entry pair while reporting recoverable record diagnostics to the caller.
    /// </summary>
    /// <param name="record">The lazy Mutagen record to inspect.</param>
    /// <param name="reportWarning">Receives non-ignorable recoverable diagnostic messages in observation order.</param>
    /// <returns>The extracted record, or <see langword="null" /> when a recoverable issue prevents storage.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">A lazy record getter reports cancellation.</exception>
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
                entry = !string.IsNullOrEmpty(record.EditorID) ? record.EditorID : GetRecordName(record, formId);
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
                entry = SynthesizeEntry(record, formId);
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

    /// <summary>
    ///     Resolves the Entry for a record that carries no EditorID, from its display name where it has one.
    /// </summary>
    /// <param name="record">The record to name.</param>
    /// <param name="formId">
    ///     The record's already-formatted FormID, used only if no name is found. Passed in rather than re-read from
    ///     <see cref="IMajorRecordGetter.FormKey" />, so the fallback never touches a lazy getter a second time.
    /// </param>
    /// <returns>The record's display name, or the synthesized fallback Entry when it has none.</returns>
    /// <remarks>
    ///     Mutagen exposes a record's display name through two aspects: an optional <c>INamedGetter</c>, whose
    ///     <c>Name</c> may be null, and a required <see cref="INamedRequiredGetter" />, whose <c>Name</c> always has a
    ///     value. The optional aspect derives from the required one, so the single cast below reaches every named
    ///     record type — including the ten that carry only the required aspect, such as Skyrim's <c>Class</c> and
    ///     Starfield's <c>Planet</c>, whose names a cast to the optional aspect misses (ADR-0005).
    ///     <para>
    ///         The required aspect never yields null, but it does yield the empty string for a record whose name
    ///         subrecord is absent — Mutagen substitutes an empty value rather than reporting the field missing — so
    ///         the emptiness check is what routes a genuinely anonymous record to its Synthesized Entry.
    ///     </para>
    /// </remarks>
    private static string GetRecordName(IMajorRecordGetter record, string formId)
    {
        if (record is INamedRequiredGetter named)
        {
            // Read once rather than testing and re-reading, for the reason DescribeRecordType gives below and because
            // an overlay's Name is not a stored field: each access re-parses the subrecord out of the record's bytes.
            var name = named.Name;
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        return SynthesizeEntry(record, formId);
    }

    /// <summary>
    ///     Builds the deterministic fallback Entry stored for a record that offers no EditorID and no name.
    /// </summary>
    /// <param name="record">The record the Entry is being synthesized for.</param>
    /// <param name="formId">The record's already-formatted FormID, so the fallback never re-reads a lazy getter.</param>
    /// <returns>A label of the form <c>[RecordType_FORMID]</c>.</returns>
    private static string SynthesizeEntry(IMajorRecordGetter record, string formId)
    {
        return $"[{DescribeRecordType(record)}_{formId}]";
    }

    /// <summary>
    ///     Names the record type a synthesized Entry is built from.
    /// </summary>
    /// <param name="record">The record to describe.</param>
    /// <returns>
    ///     The Mutagen record type name, or the runtime type name when the record does not supply a usable one.
    /// </returns>
    /// <exception cref="OperationCanceledException">The registration lookup reports cancellation.</exception>
    /// <remarks>
    ///     Mutagen routes a binary overlay and an in-memory record of the same type to one Loqui registration, so its
    ///     <c>Name</c> is the one label both reads agree on — <c>Npc</c>, never the overlay class
    ///     <c>NpcBinaryOverlay</c> that the runtime type name yields for the overlay reads every Processing Run
    ///     actually performs (ADR-0004). Reading it from Mutagen's own record metadata rather than parsing a CLR class
    ///     name also keeps the label stable across a Mutagen release that renames overlay classes.
    ///     <para>
    ///         <see cref="IMajorRecordGetter" /> inherits <c>ILoquiObject</c>, so a conforming record always has a
    ///         registration and the runtime type name below is a last resort for one that breaks that contract by
    ///         returning null, an empty name, or throwing. No record Mutagen generates does, which leaves only test
    ///         doubles on that path — it is kept because a record that got this far has already lost its EditorID and
    ///         its name, and dropping its row over a third missing value would cost the user FormID coverage.
    ///     </para>
    /// </remarks>
    private static string DescribeRecordType(IMajorRecordGetter record)
    {
        try
        {
            // Read once rather than testing and re-reading: the contract does not forbid a registration that is
            // computed per call, so a second access could differ from the one that passed the check.
            var registeredName = record.Registration?.Name;
            if (!string.IsNullOrEmpty(registeredName))
            {
                return registeredName;
            }
        }
        catch (OperationCanceledException)
        {
            // Same contract as the rest of the module: cancellation stays run control flow, never a fallback label.
            throw;
        }
        catch (Exception ex)
        {
            // A record that cannot describe its own registration must still produce an Entry, so this is not a
            // Processing Warning — it degrades to the runtime type name below.
            RethrowNestedCancellation(ex);
        }

        return record.GetType().Name;
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
    ///     Preserves cancellation when a lazy getter's exception reaches Entry Extraction wrapped in another one.
    /// </summary>
    /// <remarks>
    ///     Mutagen wraps some record-read failures, and a getter invoked through a dynamic proxy surfaces its exception
    ///     inside a <c>TargetInvocationException</c>, so the innermost cancellation is not necessarily the caught type.
    /// </remarks>
    /// <param name="exception">The possible wrapper raised during Entry Extraction.</param>
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
}
