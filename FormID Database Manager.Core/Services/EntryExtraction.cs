using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

internal sealed class EntryExtraction
{
    /// <summary>
    ///     Extracts one FormID/Entry pair, reporting each Record Issue to the caller and failing the Processing Run for
    ///     any getter failure the application does not recognise (ADR-0016 rules 4 and 5).
    /// </summary>
    /// <param name="record">The lazy Mutagen record to inspect.</param>
    /// <param name="pluginName">The selected Plugin the record belongs to, which a run failure names.</param>
    /// <param name="reportRecordIssue">
    ///     Receives every Record Issue in observation order, as the classified cause's message. None is filtered, so
    ///     each one is counted in the Plugin's Processing Warning.
    /// </param>
    /// <returns>
    ///     The extracted record, carrying a Synthesized Entry when only its Entry failed; or <see langword="null" />
    ///     when its FormID failed, in which case exactly one Record Issue was reported for it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="record" /> or <paramref name="reportRecordIssue" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="pluginName" /> is null or blank.</exception>
    /// <exception cref="OperationCanceledException">
    ///     A lazy record getter reports cancellation, directly or nested; the original cancellation is rethrown.
    /// </exception>
    /// <exception cref="UnrecognizedPluginReadFailureException">
    ///     A getter failed with a cause off the Plugin-data family list. It names <paramref name="pluginName" />, carries
    ///     <see cref="PluginReadPhase.ReadingRecords" />, and keeps the getter's exception as its inner exception.
    /// </exception>
    /// <exception cref="UnresolvableMasterException">A getter's failure chain contains a master-resolution failure.</exception>
    /// <remarks>
    ///     A per-record failure is never fatal to the Plugin: it never becomes a <see cref="PluginReadException" />.
    ///     Only the two getter reads below are classified. The record-type lookup behind a Synthesized Entry keeps its
    ///     own fallback and reports nothing (ADR-0004).
    /// </remarks>
    internal FormIdRecord? TryExtract(IMajorRecordGetter record, string pluginName, Action<string> reportRecordIssue)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentNullException.ThrowIfNull(reportRecordIssue);

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
            // Without a FormID there is no row to store, so the record is dropped, but never without being counted.
            reportRecordIssue(ClassifyRecordIssue(ex, pluginName));
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
            // The FormID is already in hand, so a failed Entry costs only the label. This includes an IOException from
            // a lazy .STRINGS load behind Name: Mutagen caches that failure, so it repeats for every record using the
            // strings file, and each of those records keeps its FormID with a Synthesized Entry (ADR-0016 rule 4).
            reportRecordIssue(ClassifyRecordIssue(ex, pluginName));
            entry = SynthesizeEntry(record, formId);
        }

        return new FormIdRecord(formId, entry);
    }

    /// <summary>
    ///     Classifies one getter failure with the shared Plugin-read classification and returns the Record Issue's
    ///     diagnostic.
    /// </summary>
    /// <param name="failure">The exception a FormID or Entry getter raised.</param>
    /// <param name="pluginName">The selected Plugin being read.</param>
    /// <returns>The classified Plugin-data cause's message (ADR-0016 rule 6).</returns>
    /// <remarks>
    ///     The classification rethrows nested cancellation before anything else, and throws for every cause that is
    ///     not about the Plugin's data, so only a listed cause comes back here to be reported. Getters throw raw
    ///     exceptions, but the classification looks through Mutagen's enrichment wrappers anyway, so a wrapped cause is
    ///     classified the same way it would be in any other phase.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="failure" />'s chain contains cancellation.</exception>
    /// <exception cref="UnresolvableMasterException">
    ///     <paramref name="failure" />'s chain contains a master-resolution failure.
    /// </exception>
    /// <exception cref="UnrecognizedPluginReadFailureException">The cause is not on the Plugin-data family list.</exception>
    private static string ClassifyRecordIssue(Exception failure, string pluginName)
    {
        return MutagenPluginReader
            .ClassifyPluginReadFailure(failure, pluginName, PluginReadPhase.ReadingRecords)
            .Message;
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
            NestedCancellation.RethrowIfPresent(ex);
        }

        return record.GetType().Name;
    }
}
