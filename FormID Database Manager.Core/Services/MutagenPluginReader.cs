using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

/// <summary>
///     The production Plugin read adapter, and the one module that decides what every Mutagen failure means: a Failed
///     Plugin while opening or enumerating, a Processing Warning or silent drop per record, or an Unresolvable Master
///     that fails the Processing Run (ADR-0006, ADR-0015).
/// </summary>
/// <remarks>
///     The three classification rules keep their current, differing scopes. Opening walks the whole inner-exception
///     chain against a list of six families and reports the deepest message; enumeration treats only a top-level
///     <see cref="RecordException" /> as a Failed Plugin and reports its own message; per record, Entry Extraction's
///     catch-all applies. Whether they should become one rule is #127.
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
        return new OpenedPlugin(OpenOverlay(readyPlugin), _entryExtraction);
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
    ///     Mutagen or the filesystem reports unreadable Plugin data; the phase is
    ///     <see cref="PluginReadPhase.OpeningPlugin" /> and the message is the deepest underlying one.
    /// </exception>
    /// <exception cref="UnresolvableMasterException">
    ///     Mutagen raises <see cref="MissingModException" /> (naming the master's file name) or
    ///     <see cref="MissingModMappingException" /> (naming no master); the Mutagen exception is the inner exception.
    /// </exception>
    private IModDisposeGetter OpenOverlay(SelectedPluginReady readyPlugin)
    {
        ArgumentNullException.ThrowIfNull(readyPlugin);

        // Validate adapter ownership before delegating to any Supported GameRelease factory or filesystem work.
        var payload = readyPlugin.ReadCapability.RequirePayload<MutagenPluginReadCapabilityPayload>();
        // Resolved outside the try: an unsupported GameRelease is a programming error, and the ArgumentOutOfRangeException
        // it raises would otherwise be normalized into a Failed Plugin by the expected-failure check below, which
        // deliberately treats ArgumentException as a malformed-Plugin signal from Mutagen.
        var release = SupportedGameReleases.ForRelease(payload.GameRelease);

        try
        {
            return _openOverlay(release, readyPlugin.ResolvedPluginPath, payload.ReadParameters);
        }
        catch (Exception ex)
        {
            // Cancellation is preserved before any expected Plugin-opening failure is normalized below.
            NestedCancellation.RethrowIfPresent(ex);
            if (IsExpectedPluginOverlayFailure(ex))
            {
                // The application-owned marker prevents arbitrary adapter failures from becoming Failed Plugins.
                throw new PluginReadException(
                    PluginReadPhase.OpeningPlugin,
                    GetUnderlyingPluginReadMessage(ex),
                    ex);
            }

            // Classified after the Failed Plugin list, as Plugin Ingestion used to classify whatever escaped it, so a
            // master-resolution failure wrapping a listed cause still becomes the same Failed Plugin it did before.
            ThrowIfUnresolvableMaster(readyPlugin.PluginName, ex);
            throw;
        }
    }

    /// <summary>
    ///     Classifies Mutagen's two master-resolution failures as an Unresolvable Master that fails the Processing Run.
    /// </summary>
    /// <param name="pluginName">The selected Plugin whose declared master could not be resolved.</param>
    /// <param name="exception">The top-level exception raised while opening the overlay.</param>
    /// <remarks>
    ///     This is a classification of its own beside the Failed Plugin list, not an entry in it. The list stays narrow
    ///     so an unexpected internal failure still aborts loudly, and these failures are not Plugin-specific anyway:
    ///     they are facts about the Data directory that would fail every selected Plugin identically (ADR-0006,
    ///     ADR-0015, issue #52). Only the top-level exception is inspected, matching the catch Plugin Ingestion held.
    /// </remarks>
    /// <exception cref="UnresolvableMasterException">
    ///     <paramref name="exception" /> is a <see cref="MissingModException" /> or
    ///     <see cref="MissingModMappingException" />, which is kept as the inner exception.
    /// </exception>
    private static void ThrowIfUnresolvableMaster(string pluginName, Exception exception)
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
    ///     Identifies the concrete Mutagen and filesystem failures that mean the selected Plugin could not be opened.
    /// </summary>
    /// <param name="exception">The exception raised by Mutagen while opening the configured Plugin path.</param>
    /// <returns><see langword="true" /> only for expected malformed or unreadable Plugin failures.</returns>
    private static bool IsExpectedPluginOverlayFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MalformedDataException
                or RecordException
                or ArgumentException
                or OverflowException
                or IOException
                or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Retains the deepest underlying Plugin-read message when Mutagen enriches an opening failure.
    /// </summary>
    /// <param name="exception">The adapter exception chain.</param>
    /// <returns>The deepest underlying exception message.</returns>
    private static string GetUnderlyingPluginReadMessage(Exception exception)
    {
        var underlying = exception;
        while (underlying.InnerException is { } innerException)
        {
            underlying = innerException;
        }

        return underlying.Message;
    }

    /// <summary>
    ///     One opened Mutagen overlay behind the seam. Its owner disposes it, which disposes the overlay.
    /// </summary>
    /// <param name="overlay">The overlay opening produced; owned by this instance from here on.</param>
    /// <param name="entryExtraction">The adapter's per-record Entry Extraction rules.</param>
    private sealed class OpenedPlugin(IModDisposeGetter overlay, EntryExtraction entryExtraction) : IOpenedPlugin
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
        ///     Receives non-ignorable recoverable record issues in observation order.
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

                if (entryExtraction.TryExtract(records.Current, reportRecordIssue) is { } record)
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
        ///     record-reading phase, keeping Mutagen's own message rather than the deepest one opening reports.
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
