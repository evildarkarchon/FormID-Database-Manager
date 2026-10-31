using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

/// <summary>
///     The production Plugin read adapter, and the one module that decides what every Mutagen failure means: a Failed
///     Plugin while opening or enumerating, a Record Issue per record, an Unresolvable Master that fails the
///     Processing Run, or, for an unrecognised failure while opening or reading one record, a run failure that names
///     the Plugin (ADR-0006, ADR-0015, ADR-0016).
/// </summary>
/// <remarks>
///     <para>
///         A Plugin-read failure becomes the outcome for the narrowest thing it is a fact about, and phase only limits
///         which outcomes are reachable (ADR-0016). <see cref="ClassifyPluginReadFailure" /> is that one
///         classification. It rethrows cancellation, then fails the run as an Unresolvable Master if one appears
///         anywhere in the chain. Next it looks through Mutagen's enrichment wrappers to the cause: a cause on the
///         Plugin-data family list, or a bare <see cref="RecordException" />, is about the Plugin's data, and anything
///         else fails the run naming the Plugin. The diagnostic message is the classified cause's.
///     </para>
///     <para>
///         Opening applies it, and a Plugin-data cause there is a Failed Plugin. Entry Extraction applies it to each
///         record's FormID and Entry getters, and a Plugin-data cause there is a Record Issue, never a Failed Plugin.
///         Enumeration still treats only a top-level <see cref="RecordException" /> as a Failed Plugin and reports
///         its own message; it moves onto the shared classification in its own ticket (#138).
///     </para>
/// </remarks>
internal sealed class MutagenPluginReader : IPluginReader
{
    private readonly EntryExtraction _entryExtraction = new();
    private readonly Func<SupportedGameRelease, string, BinaryReadParameters, IModDisposeGetter> _openOverlay;

    /// <summary>
    ///     Creates the production adapter, which opens each Plugin through its Supported GameRelease's overlay factory.
    /// </summary>
    internal MutagenPluginReader()
        : this(static (release, pluginPath, readParameters) => release.CreateOverlay(pluginPath, readParameters))
    {
    }

    /// <summary>
    ///     Creates the adapter with a replacement for the overlay factory call. This is the adapter's whole test seam.
    /// </summary>
    /// <param name="openOverlay">Opens the resolved Plugin path for the already-resolved Supported GameRelease.</param>
    /// <remarks>
    ///     The delegate receives the resolved Supported GameRelease row, so capability validation and the GameRelease
    ///     lookup necessarily run before it and stay outside it. Keep this one delegate the whole seam; ADR-0008
    ///     removed a multi-delegate constructor of the kind it would otherwise grow into.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="openOverlay" /> is null.</exception>
    internal MutagenPluginReader(
        Func<SupportedGameRelease, string, BinaryReadParameters, IModDisposeGetter> openOverlay)
    {
        _openOverlay = openOverlay ?? throw new ArgumentNullException(nameof(openOverlay));
    }

    /// <inheritdoc />
    public IOpenedPlugin Open(SelectedPluginReady readyPlugin)
    {
        return new OpenedPlugin(OpenOverlay(readyPlugin), readyPlugin.PluginName, _entryExtraction);
    }

    /// <summary>
    ///     Opens one ready selected Plugin as a Mutagen overlay, applying every opening classification rule.
    /// </summary>
    /// <param name="readyPlugin">The selected Plugin path and capability prepared by production Game Load Orders.</param>
    /// <returns>The disposable Mutagen overlay for the capability's Supported GameRelease.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="readyPlugin" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The capability identifies a GameRelease this application does not support.
    /// </exception>
    /// <exception cref="InvalidOperationException">The capability belongs to an incompatible adapter.</exception>
    /// <exception cref="PluginReadException">
    ///     Mutagen or the filesystem reports unreadable Plugin data, directly or behind Mutagen's enrichment wrappers;
    ///     the phase is <see cref="PluginReadPhase.OpeningPlugin" /> and the message is the classified cause's.
    /// </exception>
    /// <exception cref="UnresolvableMasterException">
    ///     The failure's chain contains <see cref="MissingModException" /> (naming the master's file name) or
    ///     <see cref="MissingModMappingException" /> (naming no master); the Mutagen exception is the inner exception.
    /// </exception>
    /// <exception cref="UnrecognizedPluginReadFailureException">
    ///     Opening failed for any other reason; it names the selected Plugin, carries
    ///     <see cref="PluginReadPhase.OpeningPlugin" />, and keeps the original exception as its inner exception.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     The failure's chain contains cancellation, which is rethrown before any classification.
    /// </exception>
    private IModDisposeGetter OpenOverlay(SelectedPluginReady readyPlugin)
    {
        ArgumentNullException.ThrowIfNull(readyPlugin);

        // Validate adapter ownership before delegating to any Supported GameRelease factory or filesystem work.
        var payload = readyPlugin.ReadCapability.RequirePayload<MutagenPluginReadCapabilityPayload>();
        // Resolved outside the try: an unsupported GameRelease is a programming error, and the ArgumentOutOfRangeException
        // it raises would otherwise be normalized into a Failed Plugin by the Plugin-read classification below, whose
        // family list deliberately treats ArgumentException as a malformed-Plugin signal from Mutagen.
        var release = SupportedGameReleases.ForRelease(payload.GameRelease);

        try
        {
            return _openOverlay(release, readyPlugin.ResolvedPluginPath, payload.ReadParameters);
        }
        catch (Exception ex)
        {
            // Cancellation, an Unresolvable Master, and an unrecognised cause all leave from inside the classification;
            // only a Plugin-data cause returns, and while opening that can only mean a Failed Plugin.
            var cause = ClassifyPluginReadFailure(ex, readyPlugin.PluginName, PluginReadPhase.OpeningPlugin);

            // The application-owned marker prevents arbitrary adapter failures from becoming Failed Plugins.
            throw new PluginReadException(PluginReadPhase.OpeningPlugin, cause.Message, ex);
        }
    }

    /// <summary>
    ///     The one Plugin-read classification by fault scope, shared by every Plugin-read phase (ADR-0016 rules 1, 2, 3
    ///     and 6). It throws for every failure that is not about the Plugin's data and returns the Plugin-data cause,
    ///     which the caller turns into the outcome its phase allows.
    /// </summary>
    /// <param name="failure">The exception the phase caught, kept whole as the inner exception of any run failure.</param>
    /// <param name="pluginName">The selected Plugin being read, which every run failure names.</param>
    /// <param name="phase">
    ///     The phase the failure arrived in. It changes no classification: it is only carried by the run failure for an
    ///     unrecognised cause. The caller applies it to the returned cause, so phase limits which outcomes are reachable
    ///     without changing what a failure is a fact about.
    /// </param>
    /// <returns>
    ///     The classified Plugin-data cause: the exception that matched the Plugin-data family list, or a bare
    ///     <see cref="RecordException" /> Mutagen raised itself. Its message is the diagnostic (rule 6). The caller
    ///     decides what it means in its phase, such as a Failed Plugin while opening.
    /// </returns>
    /// <remarks>
    ///     The order is the decision. Cancellation goes first so a cancelled read is never reported as a failure. The
    ///     Unresolvable Master check runs next and searches the whole chain, so a master-resolution failure fails the run
    ///     whatever it wraps or is wrapped by. Only then is the cause looked for behind Mutagen's enrichment wrappers.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    ///     <paramref name="failure" />'s chain contains cancellation, which is rethrown as the original.
    /// </exception>
    /// <exception cref="UnresolvableMasterException">
    ///     <paramref name="failure" />'s chain contains a <see cref="MissingModException" /> or
    ///     <see cref="MissingModMappingException" />, kept as the inner exception.
    /// </exception>
    /// <exception cref="UnrecognizedPluginReadFailureException">
    ///     The cause behind the enrichment wrappers is not on the Plugin-data family list. It names
    ///     <paramref name="pluginName" />, carries <paramref name="phase" />, and keeps <paramref name="failure" />.
    /// </exception>
    internal static Exception ClassifyPluginReadFailure(Exception failure, string pluginName, PluginReadPhase phase)
    {
        NestedCancellation.RethrowIfPresent(failure);
        ThrowIfUnresolvableMaster(pluginName, failure);

        // Anything left without a Plugin-data cause is a failure the application does not recognise. It fails the run,
        // and names the Plugin so the user can deselect it (ADR-0016 rule 7).
        return FindPluginDataCause(failure)
               ?? throw new UnrecognizedPluginReadFailureException(pluginName, phase, failure);
    }

    /// <summary>
    ///     Classifies Mutagen's two master-resolution failures, found anywhere in the inner-exception chain, as an
    ///     Unresolvable Master that fails the Processing Run.
    /// </summary>
    /// <param name="pluginName">The selected Plugin whose declared master could not be resolved.</param>
    /// <param name="exception">The top-level Plugin-read failure.</param>
    /// <remarks>
    ///     This is a classification of its own ahead of the Plugin-data family list, not an entry in it. These failures
    ///     are not Plugin-specific: they are facts about the Data directory that would fail every selected Plugin
    ///     identically (ADR-0006, ADR-0015, issue #52). The whole chain is searched, and before the family list, so a
    ///     master failure that wraps a listed cause, or sits behind an enrichment wrapper, is never a Failed Plugin
    ///     (ADR-0016 rule 1). The first master failure from the top is the one reported.
    /// </remarks>
    /// <exception cref="UnresolvableMasterException">
    ///     <paramref name="exception" />'s chain contains a <see cref="MissingModException" /> or
    ///     <see cref="MissingModMappingException" />, which is kept as the inner exception.
    /// </exception>
    private static void ThrowIfUnresolvableMaster(string pluginName, Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            ThrowIfMasterResolutionFailure(pluginName, current);
        }
    }

    /// <summary>
    ///     Throws the Unresolvable Master for one exception of the chain if it is a master-resolution failure.
    /// </summary>
    /// <param name="pluginName">The selected Plugin whose declared master could not be resolved.</param>
    /// <param name="exception">One exception of the failure's inner-exception chain.</param>
    /// <exception cref="UnresolvableMasterException">
    ///     <paramref name="exception" /> is a <see cref="MissingModException" /> or
    ///     <see cref="MissingModMappingException" />, which is kept as the inner exception.
    /// </exception>
    private static void ThrowIfMasterResolutionFailure(string pluginName, Exception exception)
    {
        switch (exception)
        {
            case MissingModException missingMod:
                // ModPath is the first of the exception's keys, which is the only one here: Mutagen's separated-master
                // path raises this per unresolved master, from a ModKey rather than a path, so it carries exactly one
                // and that key has no directory to report — only its file name.
                throw new UnresolvableMasterException(
                    pluginName,
                    missingMod.ModPath.ModKey.FileName.ToString(),
                    missingMod);
            case MissingModMappingException missingMapping:
                // Mutagen reports only that no lookup was supplied, so there is no individual master to name.
                throw new UnresolvableMasterException(pluginName, null, missingMapping);
        }
    }

    /// <summary>
    ///     Finds a Plugin-read failure's cause behind Mutagen's enrichment wrappers and returns it if it is about the
    ///     Plugin's data (ADR-0016 rules 2 and 3).
    /// </summary>
    /// <param name="failure">The top-level Plugin-read failure, already cleared of cancellation and master failures.</param>
    /// <returns>
    ///     The first exception from the top that is on the Plugin-data family list, reached through only
    ///     <see cref="RecordException" />, <see cref="SubrecordException" /> and
    ///     <see cref="ModGroupsMalformedException" />; or a bare <see cref="RecordException" /> at the end of those
    ///     wrappers; or <see langword="null" /> when the cause is anything else.
    /// </returns>
    /// <remarks>
    ///     Only Mutagen's own enrichment wrappers are looked through. Any other exception is the cause itself, so an
    ///     unrecognised exception wrapping a listed one is classified as unrecognised: what wraps the data failure is
    ///     not something the application knows to be about the Plugin. Mutagen raises a <see cref="RecordException" />
    ///     (a <see cref="SubrecordException" /> among them) with no cause for data it rejects itself, so that one is a
    ///     Failed Plugin; a <see cref="ModGroupsMalformedException" /> is only ever an enrichment, so with no cause it is
    ///     not.
    /// </remarks>
    private static Exception? FindPluginDataCause(Exception failure)
    {
        var current = failure;
        while (true)
        {
            if (IsPluginDataFailureFamily(current))
            {
                return current;
            }

            switch (current)
            {
                case RecordException or ModGroupsMalformedException when current.InnerException is { } cause:
                    current = cause;
                    break;
                case RecordException bareRecordException:
                    return bareRecordException;
                default:
                    return null;
            }
        }
    }

    /// <summary>
    ///     The one Plugin-data family list every phase classifies against (ADR-0016 rule 2), apart from the bare
    ///     <see cref="RecordException" />, which <see cref="FindPluginDataCause" /> recognises by its having no cause.
    /// </summary>
    /// <param name="exception">One exception from a failure's chain.</param>
    /// <returns><see langword="true" /> when the exception is about the Plugin's data.</returns>
    /// <remarks>
    ///     <see cref="InvalidDataException" /> comes from a short group read, <see cref="IndexOutOfRangeException" />
    ///     from truncated subrecord slices, and <see cref="RecordCollisionException" /> from duplicate FormKeys within
    ///     the Plugin. Keep the list narrow: whatever is not on it fails the run, which is how a defect in the
    ///     application or in Mutagen stays loud instead of passing for a broken Plugin.
    /// </remarks>
    private static bool IsPluginDataFailureFamily(Exception exception)
    {
        return exception is MalformedDataException
            or ArgumentException
            or OverflowException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or IndexOutOfRangeException
            or RecordCollisionException;
    }

    /// <summary>
    ///     One opened Mutagen overlay behind the seam. Its owner disposes it, which disposes the overlay.
    /// </summary>
    /// <param name="overlay">The overlay opening produced; owned by this instance from here on.</param>
    /// <param name="pluginName">The selected Plugin the overlay was opened for, which every run failure names.</param>
    /// <param name="entryExtraction">The adapter's per-record Entry Extraction rules.</param>
    private sealed class OpenedPlugin(IModDisposeGetter overlay, string pluginName, EntryExtraction entryExtraction)
        : IOpenedPlugin
    {
        /// <inheritdoc />
        public IEnumerable<FormIdRecord> ReadRecords(
            Action<string> reportRecordIssue,
            CancellationToken cancellationToken)
        {
            // Checked here rather than in the iterator, whose body (argument checks included) runs only on first use.
            ArgumentNullException.ThrowIfNull(reportRecordIssue);
            return EnumerateRecords(reportRecordIssue, cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            overlay.Dispose();
        }

        /// <summary>
        ///     Lazily extracts storable records from the opened overlay, applying the enumeration and per-record
        ///     rules.
        /// </summary>
        /// <param name="reportRecordIssue">
        ///     Receives every Record Issue in observation order, as the classified cause's message.
        /// </param>
        /// <param name="cancellationToken">Checked once per record.</param>
        /// <returns>The lazy sequence consumed inside the Store's atomic Plugin write.</returns>
        /// <exception cref="PluginReadException">Mutagen reports an expected record-read failure.</exception>
        /// <exception cref="OperationCanceledException">
        ///     <paramref name="cancellationToken" /> requests cancellation.
        /// </exception>
        private IEnumerable<FormIdRecord> EnumerateRecords(
            Action<string> reportRecordIssue,
            CancellationToken cancellationToken)
        {
            using var records = CreateRecordEnumerator(overlay);

            while (MoveNextRecord(records))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entryExtraction.TryExtract(records.Current, pluginName, reportRecordIssue) is { } record)
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
                throw CreateReadingRecordsFailure(ex);
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
                throw CreateReadingRecordsFailure(ex);
            }
        }

        /// <summary>
        ///     Classifies a top-level <see cref="RecordException" /> raised while enumerating as a Failed Plugin in the
        ///     record-reading phase, keeping Mutagen's own message rather than the classified cause's that opening
        ///     reports. It moves onto <see cref="ClassifyPluginReadFailure" /> in #138.
        /// </summary>
        /// <param name="exception">The record-read failure Mutagen raised.</param>
        /// <returns>The classified failure, for the caller to throw from its own catch.</returns>
        /// <exception cref="OperationCanceledException">
        ///     <paramref name="exception" /> wraps a cancellation, which is rethrown instead of being classified.
        /// </exception>
        /// <remarks>
        ///     It returns rather than throws so each caller's <c>throw</c> keeps the compiler's flow analysis intact.
        /// </remarks>
        private static PluginReadException CreateReadingRecordsFailure(RecordException exception)
        {
            // Cancellation is preserved before the record-read failure is normalized below, as on the opening path.
            NestedCancellation.RethrowIfPresent(exception);
            return new PluginReadException(
                PluginReadPhase.ReadingRecords,
                exception.Message,
                exception);
        }
    }
}
