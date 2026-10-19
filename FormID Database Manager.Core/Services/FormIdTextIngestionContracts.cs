namespace FormID_Database_Manager.Services;

/// <summary>
///     One parsed, valid row of a FormID text file, handed by FormID Text Ingestion to the FormID Record Store.
/// </summary>
/// <param name="Plugin">The trimmed Plugin value, in the casing of this row.</param>
/// <param name="FormId">The trimmed FormID value.</param>
/// <param name="Entry">The trimmed Entry value.</param>
internal readonly record struct FormIdTextRow(string Plugin, string FormId, string Entry);

/// <summary>
///     Consumer-owned FormID Record Store role for persisting the rows of one FormID text file during FormID Text
///     Ingestion.
/// </summary>
/// <remarks>
///     <para>
///         FormID Text Ingestion owns this role just as Plugin Ingestion owns <see cref="IPluginFormIdRecordWriter" />
///         (ADR-0001, 2026-10-18). The surrounding Processing Run owns the complete Store session and its lifetime;
///         FormID Text Ingestion borrows only this role and does not open, optimize, or dispose the Store.
///     </para>
///     <para>
///         Calls do not overlap. The supplied stream is enumerated exactly once during the operation and is not
///         retained after it completes. Rows are staged, then committed with one transaction per Plugin key matched
///         case-insensitively, appending or replacing according to the Update Mode. Stored Plugin values keep the
///         casing of each row.
///     </para>
/// </remarks>
internal interface IFormIdTextRowWriter
{
    /// <summary>
    ///     Stages every row of the lazy stream and commits each Plugin's rows in its own transaction.
    /// </summary>
    /// <param name="rows">The lazy parsed rows, enumerated exactly once before this operation completes.</param>
    /// <param name="updateMode">Whether to append rows or replace each mentioned Plugin's existing rows.</param>
    /// <param name="cancellationToken">Stops enumeration, staging, and any remaining Plugin commits.</param>
    /// <returns>A task that completes when every Plugin has been committed.</returns>
    /// <remarks>
    ///     Whether the stream fails, a commit fails, or cancellation is requested, staging is cleared before the
    ///     operation returns, so a reused Store never commits stale rows.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="updateMode" /> is not supported.</exception>
    /// <exception cref="Exception">
    ///     The stream itself fails, which propagates unchanged, or a Plugin commit fails. One Plugin's commit failure
    ///     rolls back that Plugin without stopping the others; a single failure is rethrown as is, and several are
    ///     aggregated in an <see cref="AggregateException" />.
    /// </exception>
    Task WriteTextRowsAsync(
        IAsyncEnumerable<FormIdTextRow> rows,
        UpdateMode updateMode,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Defines the complete FormID text-file phase used by a Processing Run: everything about reading one FormID text
///     file, from its Dry Run plan to the rows it hands the FormID Record Store.
/// </summary>
/// <remarks>
///     The sibling of <see cref="IPluginIngestion" /> for the other kind of run (ADR-0001, 2026-10-18). It owns
///     opening and reading the file, parsing and skipping malformed rows, counting records and distinct Plugins,
///     case-insensitive Plugin identity, and import progress. Whether a Plugin it encounters is shown to the user is
///     the Processing Run's decision, not this module's (ADR-0007).
/// </remarks>
internal interface IFormIdTextIngestion
{
    /// <summary>
    ///     Reads the request's FormID text file and hands every valid row, in file order, to the borrowed text-row
    ///     writer role of the already-open Store session.
    /// </summary>
    /// <param name="request">The immutable FormID text-file request; its Update Mode is passed to the writer.</param>
    /// <param name="recordStore">The text-row Store role borrowed from the surrounding Processing Run.</param>
    /// <param name="progress">
    ///     Optional reporter for the opening, periodic counter updates, and each Plugin's first encounter. Every Plugin
    ///     is reported in both Update Modes, before its first row is handed to the Store.
    /// </param>
    /// <param name="cancellationToken">Stops reading, and therefore the stream the Store is consuming.</param>
    /// <returns>The distinct Plugin and valid row counts of the file, as read.</returns>
    /// <remarks>
    ///     A valid row has exactly two pipes; its Plugin, FormID, and Entry are trimmed. Blank and malformed rows are
    ///     skipped rather than failing the run. Store optimization and disposal remain the surrounding Processing
    ///     Run's responsibility.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> or <paramref name="recordStore" /> is null.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist; nothing is reported or written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    /// <exception cref="Exception">
    ///     Reading the file fails, or the Store fails to persist the rows; either propagates unchanged.
    /// </exception>
    Task<FormIdTextFileImportResult> IngestAsync(
        FormIdTextProcessingRunRequest request,
        IFormIdTextRowWriter recordStore,
        IProgress<FormIdTextIngestionProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Plans the request: looks up whether its FormID text file exists and how large it is.
    /// </summary>
    /// <param name="request">The immutable FormID text-file request.</param>
    /// <param name="cancellationToken">Stops planning without returning a plan.</param>
    /// <returns>The file's path, and its size in bytes or <see langword="null" /> when no file is there.</returns>
    /// <remarks>
    ///     This reads file metadata only. The file is never opened, parsed, or counted, because a row count is what
    ///     ingestion measures and a plan that measured it would have done the run. Absence is a fact the plan
    ///     reports, not a failure it raises.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    Task<FormIdTextRunPlan> PlanAsync(
        FormIdTextProcessingRunRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     A transient fact FormID Text Ingestion reports while reading one FormID text file.
/// </summary>
/// <remarks>
///     <para>
///         The three cases are told apart by type rather than by their counters: the import opening, a counter
///         update, and a Plugin's first encounter. Derivation is closed to this assembly.
///     </para>
///     <para>
///         Ordering (ADR-0007, 2026-09-16): the opening comes once, before the first row. If a periodic counter
///         update and a first encounter fall on the same row, the counter update comes first. Both are reported
///         before that row is handed to the Store, so a first encounter never claims the Plugin's rows are committed.
///     </para>
/// </remarks>
internal abstract record FormIdTextIngestionProgress
{
    /// <summary>
    ///     Closes derivation to the three cases declared in this assembly.
    /// </summary>
    private protected FormIdTextIngestionProgress()
    {
    }
}

/// <summary>
///     The FormID text file has been found and is about to be read; reported once, before the first row.
/// </summary>
/// <param name="TotalBytes">The total size of the FormID text file, in bytes.</param>
internal sealed record FormIdTextIngestionOpening(long TotalBytes) : FormIdTextIngestionProgress;

/// <summary>
///     A periodic counter update, reported at a fixed interval of valid rows.
/// </summary>
/// <param name="RecordCount">The number of valid FormID text rows counted so far.</param>
/// <param name="BytesRead">How far into the file the reader has pulled, in bytes.</param>
/// <param name="TotalBytes">The total size of the FormID text file, in bytes.</param>
internal sealed record FormIdTextIngestionCounterUpdate(long RecordCount, long BytesRead, long TotalBytes)
    : FormIdTextIngestionProgress;

/// <summary>
///     A Plugin's first valid row, matched case-insensitively, reported before that row is handed to the Store.
/// </summary>
/// <param name="RecordCount">The number of valid FormID text rows counted so far, including this one.</param>
/// <param name="BytesRead">How far into the file the reader has pulled, in bytes.</param>
/// <param name="TotalBytes">The total size of the FormID text file, in bytes.</param>
/// <param name="PluginName">The Plugin name with the casing of its first valid row.</param>
internal sealed record FormIdTextIngestionPluginFirstEncountered(
    long RecordCount,
    long BytesRead,
    long TotalBytes,
    string PluginName) : FormIdTextIngestionProgress;
