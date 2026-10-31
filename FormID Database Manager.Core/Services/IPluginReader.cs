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
    ///     Receives each recoverable record issue worth reporting, in observation order, after the ignore list has run.
    /// </param>
    /// <param name="cancellationToken">Checked once per record while reading.</param>
    /// <returns>The lazy record sequence, intended to be consumed once inside the Store's atomic Plugin write.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reportRecordIssue" /> is null.</exception>
    /// <exception cref="PluginReadException">
    ///     Raised while enumerating: the Plugin's records contain malformed or unreadable data; the phase is
    ///     <see cref="PluginReadPhase.ReadingRecords" />.
    /// </exception>
    /// <exception cref="UnresolvableMasterException">
    ///     Raised while enumerating: reading the records needed a master the prepared read capability cannot resolve
    ///     (ADR-0006).
    /// </exception>
    /// <exception cref="UnrecognizedPluginReadFailureException">
    ///     Raised while enumerating: reading the records failed for a reason that is neither malformed Plugin data nor
    ///     an Unresolvable Master. It fails the Processing Run rather than the Plugin, names the selected Plugin,
    ///     carries <see cref="PluginReadPhase.ReadingRecords" />, and keeps the original failure as its inner exception
    ///     (ADR-0016).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Raised while enumerating: <paramref name="cancellationToken" /> requests cancellation, or a failure carries
    ///     a nested cancellation, which is rethrown as the original cancellation instead of being classified.
    /// </exception>
    /// <remarks>
    ///     Every exception raised while enumerating leaves through the caller's consumption of the sequence, so when
    ///     that consumption is the Store's atomic Plugin write, the records already yielded are rolled back.
    /// </remarks>
    IEnumerable<FormIdRecord> ReadRecords(Action<string> reportRecordIssue, CancellationToken cancellationToken);
}
