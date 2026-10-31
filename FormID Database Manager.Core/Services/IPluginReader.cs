namespace FormID_Database_Manager.Services;

/// <summary>
///     The Plugin read seam: opens a ready selected Plugin and hands back an opened Plugin whose records cross the seam
///     as FormIDs and Entries, with every Plugin-read failure already classified (ADR-0015).
/// </summary>
internal interface IPluginReader
{
    /// <summary>
    ///     Opens one ready selected Plugin eagerly, so an opening failure surfaces before any Store write begins and a
    ///     Dry Run can open a Plugin without reading it.
    /// </summary>
    /// <param name="readyPlugin">The prepared selected Plugin and its opaque read capability.</param>
    /// <returns>The opened Plugin, which the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="readyPlugin" /> is null.</exception>
    /// <exception cref="InvalidOperationException">The read capability belongs to an incompatible adapter.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The capability identifies an unsupported GameRelease. This is a programming error, not a Plugin-read
    ///     failure, so it deliberately does not arrive as <see cref="PluginReadException" />.
    /// </exception>
    /// <exception cref="PluginReadException">
    ///     The selected Plugin contains malformed or unreadable data; the phase is
    ///     <see cref="PluginReadPhase.OpeningPlugin" />.
    /// </exception>
    /// <exception cref="UnresolvableMasterException">
    ///     The prepared read capability cannot resolve a master the selected Plugin declares (ADR-0006).
    /// </exception>
    /// <exception cref="UnrecognizedPluginReadFailureException">
    ///     Opening failed for a reason that is neither malformed Plugin data nor an Unresolvable Master. It fails the
    ///     Processing Run rather than the Plugin, names the selected Plugin, carries
    ///     <see cref="PluginReadPhase.OpeningPlugin" />, and keeps the original failure as its inner exception
    ///     (ADR-0016). Cancellation nested in such a failure is rethrown as the original cancellation instead.
    /// </exception>
    IOpenedPlugin Open(SelectedPluginReady readyPlugin);
}

/// <summary>
///     One opened Plugin. Disposing it releases what opening acquired.
/// </summary>
internal interface IOpenedPlugin : IDisposable
{
    /// <summary>
    ///     Lazily reads the Plugin's records as FormID records. Nothing is read until the sequence is consumed.
    /// </summary>
    /// <param name="reportRecordIssue">
    ///     Receives every Record Issue, in observation order, as the message of the Plugin-data cause it was classified
    ///     by. None is filtered out, so each one is counted in the Plugin's Processing Warning. A record whose FormID
    ///     failed is not yielded; a record whose Entry failed is yielded with a Synthesized Entry. A per-record failure
    ///     is never a Failed Plugin: one whose cause is not about the Plugin's data is not reported here, and instead
    ///     fails the Processing Run as an <see cref="UnrecognizedPluginReadFailureException" /> naming the Plugin, in
    ///     <see cref="PluginReadPhase.ReadingRecords" /> (ADR-0016).
    /// </param>
    /// <param name="cancellationToken">Checked once per record while reading.</param>
    /// <returns>The lazy record sequence, intended to be consumed once inside the Store's atomic Plugin write.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reportRecordIssue" /> is null.</exception>
    /// <exception cref="PluginReadException">
    ///     Raised while enumerating: the Plugin's records cannot be read; the phase is
    ///     <see cref="PluginReadPhase.ReadingRecords" />.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Raised while enumerating: <paramref name="cancellationToken" /> requests cancellation.
    /// </exception>
    IEnumerable<FormIdRecord> ReadRecords(Action<string> reportRecordIssue, CancellationToken cancellationToken);
}
