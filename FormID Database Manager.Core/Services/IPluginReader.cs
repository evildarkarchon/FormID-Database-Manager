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
    ///     Raised while enumerating: the Plugin's records cannot be read; the phase is
    ///     <see cref="PluginReadPhase.ReadingRecords" />.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Raised while enumerating: <paramref name="cancellationToken" /> requests cancellation.
    /// </exception>
    IEnumerable<FormIdRecord> ReadRecords(Action<string> reportRecordIssue, CancellationToken cancellationToken);
}
